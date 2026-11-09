using System.Diagnostics;
using KeySender.Models;
using KeySender.Validation;

namespace KeySender.Services;

/// <summary>
/// Continuous recording session: one fragment, owned Hook lifecycle, gate reservation (design 03 §3–5).
/// </summary>
public sealed class RecordingService : IDisposable
{
    private readonly IInputActivityGate _gate;
    private readonly IKeyboardHookSource _hook;
    private readonly IMonotonicClock _clock;
    private readonly Func<bool> _isRecordingWindowForeground;
    private readonly object _sync = new();

    private Session? _session;
    private bool _disposed;

    public RecordingService(
        IInputActivityGate gate,
        IKeyboardHookSource hook,
        IMonotonicClock? clock = null,
        Func<bool>? isRecordingWindowForeground = null)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _hook = hook ?? throw new ArgumentNullException(nameof(hook));
        _clock = clock ?? new StopwatchMonotonicClock();
        _isRecordingWindowForeground = isRecordingWindowForeground ?? (() => true);
    }

    public event EventHandler<RecordingSnapshot>? RecordingChanged;

    public RecordingOperationResult Start(
        string windowSessionId,
        IEnumerable<PhysicalKeyIdentity>? baselineHeld = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowSessionId);

        lock (_sync)
        {
            if (_session is not null
                && _session.State is not RecordingState.Cancelled
                    and not RecordingState.Failed
                    and not RecordingState.Idle)
            {
                return RecordingOperationResult.Fail(
                    ValidationCodes.RecordingBusy,
                    "A recording session is already active.",
                    SnapshotUnlocked());
            }

            if (!_gate.TryReserveForRecording(out var reject))
            {
                return RecordingOperationResult.Fail(
                    reject ?? ValidationCodes.RecordingUnavailable,
                    "Cannot start recording: input activity gate rejected reservation.");
            }

            var session = new Session
            {
                RecordingId = Guid.NewGuid().ToString("D"),
                WindowSessionId = windowSessionId,
                State = RecordingState.Preparing,
                StartedAtMs = _clock.ElapsedMilliseconds,
            };

            if (baselineHeld is not null)
            {
                foreach (var id in baselineHeld)
                {
                    session.BaselineHeld.Add(id);
                }
            }

            _session = session;

            try
            {
                _hook.Start(OnHookEvent);
            }
            catch (Exception ex)
            {
                _session = null;
                _gate.EndRecordingReservation();
                return RecordingOperationResult.Fail(
                    ValidationCodes.RecordingUnavailable,
                    ex.Message);
            }

            // Baseline wait is sampled outside the hook callback. Empty baseline → recording.
            session.BaselineDeadlineMs = _clock.ElapsedMilliseconds + KeySenderConst.RecordingBaselineTimeoutMs;
            TryEnterRecordingIfBaselineClearUnlocked();

            var snap = SnapshotUnlocked()!;
            RaiseChanged(snap);
            return RecordingOperationResult.Success(snap);
        }
    }

    /// <summary>
    /// Marks baseline physical keys that were already down before recording.
    /// Their Ups pass through and are not recorded. Empty set allows immediate recording.
    /// </summary>
    public void SetBaselineHeldKeys(IEnumerable<PhysicalKeyIdentity> held)
    {
        ArgumentNullException.ThrowIfNull(held);
        lock (_sync)
        {
            if (_session is null || _session.State != RecordingState.Preparing)
            {
                return;
            }

            _session.BaselineHeld.Clear();
            foreach (var id in held)
            {
                _session.BaselineHeld.Add(id);
            }

            TryEnterRecordingIfBaselineClearUnlocked();
            RaiseChanged(SnapshotUnlocked()!);
        }
    }

    /// <summary>
    /// Advance preparing → recording when baseline is clear or timed out (call from UI poll / tests).
    /// </summary>
    public RecordingSnapshot? TickPreparing()
    {
        lock (_sync)
        {
            if (_session is null || _session.State != RecordingState.Preparing)
            {
                return SnapshotUnlocked();
            }

            TryEnterRecordingIfBaselineClearUnlocked();
            var snap = SnapshotUnlocked();
            RaiseChanged(snap!);
            return snap;
        }
    }

    public RecordingOperationResult Stop(string recordingId, string? notice = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_sync)
        {
            if (!TryGetSession(recordingId, out var session))
            {
                return RecordingOperationResult.Fail(ValidationCodes.RecordingNotReady, "Unknown recording session.");
            }

            if (session.State is RecordingState.Review)
            {
                return RecordingOperationResult.Success(SnapshotUnlocked()!, BuildFragmentUnlocked());
            }

            if (session.State is RecordingState.Cancelled or RecordingState.Failed)
            {
                return RecordingOperationResult.Fail(
                    ValidationCodes.RecordingNotReady,
                    "Recording session already ended.",
                    SnapshotUnlocked());
            }

            if (notice is not null)
            {
                session.Notices.Add(notice);
            }

            BeginDrainUnlocked(session, discard: false);
            var snap = SnapshotUnlocked()!;
            RaiseChanged(snap);
            return RecordingOperationResult.Success(
                snap,
                session.State == RecordingState.Review ? BuildFragmentUnlocked() : null);
        }
    }

    public RecordingOperationResult NotifyFocusLost(string recordingId)
        => Stop(recordingId, ValidationCodes.RecordingInterrupted);

    public RecordingOperationResult Cancel(string recordingId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_sync)
        {
            if (!TryGetSession(recordingId, out var session))
            {
                // Idempotent cancel for unknown/already-ended sessions.
                return RecordingOperationResult.Success(new RecordingSnapshot
                {
                    RecordingId = recordingId,
                    WindowSessionId = "",
                    State = RecordingState.Cancelled,
                    SequenceNumber = 0,
                });
            }

            if (session.State is RecordingState.Cancelled)
            {
                return RecordingOperationResult.Success(SnapshotUnlocked()!);
            }

            if (session.State is RecordingState.Review)
            {
                DiscardAndEndUnlocked(session, RecordingState.Cancelled);
                var snap = SnapshotUnlocked()!;
                RaiseChanged(snap);
                return RecordingOperationResult.Success(snap);
            }

            if (session.State == RecordingState.Draining)
            {
                session.DiscardAfterDrain = true;
                CompleteDrainIfReadyUnlocked();
                var drainingSnap = SnapshotUnlocked()!;
                RaiseChanged(drainingSnap);
                return RecordingOperationResult.Success(drainingSnap);
            }

            BeginDrainUnlocked(session, discard: true);
            var resultSnap = SnapshotUnlocked()!;
            RaiseChanged(resultSnap);
            return RecordingOperationResult.Success(resultSnap);
        }
    }

    public RecordingSnapshot? GetSnapshot(string? recordingId = null)
    {
        lock (_sync)
        {
            if (recordingId is not null
                && _session is not null
                && !string.Equals(_session.RecordingId, recordingId, StringComparison.Ordinal))
            {
                return null;
            }

            return SnapshotUnlocked();
        }
    }

    public RecordingOperationResult GetFragment(string recordingId)
    {
        lock (_sync)
        {
            if (!TryGetSession(recordingId, out var session) || session.State != RecordingState.Review)
            {
                return RecordingOperationResult.Fail(
                    ValidationCodes.RecordingNotReady,
                    "Fragment is only available in review state.");
            }

            return RecordingOperationResult.Success(SnapshotUnlocked()!, BuildFragmentUnlocked());
        }
    }

    public RecordingOperationResult Apply(string recordingId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_sync)
        {
            if (!TryGetSession(recordingId, out var session))
            {
                return RecordingOperationResult.Fail(ValidationCodes.RecordingNotReady, "Unknown recording session.");
            }

            if (session.State != RecordingState.Review)
            {
                return RecordingOperationResult.Fail(
                    ValidationCodes.RecordingNotReady,
                    "Apply is only allowed from review state.");
            }

            if (session.Events.Count == 0)
            {
                return RecordingOperationResult.Fail(
                    ValidationCodes.EmptySequence,
                    "Recording fragment is empty.");
            }

            var fragment = BuildFragmentUnlocked()!;
            var snap = SnapshotUnlocked()!;
            EndSessionUnlocked(clearReservation: true);
            RaiseChanged(new RecordingSnapshot
            {
                RecordingId = recordingId,
                WindowSessionId = snap.WindowSessionId,
                State = RecordingState.Idle,
                SequenceNumber = snap.SequenceNumber + 1,
            });
            return RecordingOperationResult.Success(snap, fragment);
        }
    }

    /// <summary>
    /// Complete drain when owned suppressed keys are empty or timeout elapsed.
    /// Safe to call from tests after Push of drain Ups.
    /// </summary>
    public RecordingSnapshot? TickDrain()
    {
        lock (_sync)
        {
            if (_session is null || _session.State != RecordingState.Draining)
            {
                return SnapshotUnlocked();
            }

            CompleteDrainIfReadyUnlocked();
            var snap = SnapshotUnlocked();
            RaiseChanged(snap!);
            return snap;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_sync)
        {
            if (_session is not null
                && _session.State is not RecordingState.Cancelled
                    and not RecordingState.Failed
                    and not RecordingState.Idle)
            {
                try
                {
                    _hook.Stop();
                }
                catch
                {
                    // best-effort
                }

                _gate.EndRecordingReservation();
            }

            _session = null;
        }

        _hook.Dispose();
    }

    private KeyboardHookAction OnHookEvent(RawKeyboardEvent ev)
    {
        // Keep light: decode/append/notify only. No I/O, no await.
        lock (_sync)
        {
            var session = _session;
            if (session is null)
            {
                return KeyboardHookAction.PassThrough;
            }

            if (ev.Injected || ev.LowerIntegrityInjected)
            {
                return KeyboardHookAction.PassThrough;
            }

            var identity = PhysicalKeyIdentity.From(ev);

            if (session.State == RecordingState.Preparing)
            {
                if (ev.IsKeyUp && session.BaselineHeld.Remove(identity))
                {
                    TryEnterRecordingIfBaselineClearUnlocked();
                }

                return KeyboardHookAction.PassThrough;
            }

            if (session.State == RecordingState.Draining)
            {
                return HandleDrainUnlocked(session, ev, identity);
            }

            if (session.State != RecordingState.Recording)
            {
                return KeyboardHookAction.PassThrough;
            }

            if (!_isRecordingWindowForeground())
            {
                // Focus loss: freeze via drain path with notice (design 03 §4).
                session.Notices.Add(ValidationCodes.RecordingInterrupted);
                BeginDrainUnlocked(session, discard: false);
                return KeyboardHookAction.PassThrough;
            }

            if (session.BaselineHeld.Contains(identity))
            {
                // Still releasing a pre-start key — pass through, never record.
                if (ev.IsKeyUp)
                {
                    session.BaselineHeld.Remove(identity);
                }

                return KeyboardHookAction.PassThrough;
            }

            if (!KeyTokenCatalog.TryResolve(ev.VkCode, ev.ScanCode, ev.Extended, out var tokenInfo))
            {
                session.Notices.Add(ValidationCodes.RecordingUnsupportedKey);
                BeginDrainUnlocked(session, discard: false);
                return KeyboardHookAction.PassThrough;
            }

            // Auto-repeat Downs: consume but only record the first Down per physical key.
            if (!ev.IsKeyUp && session.PhysicalDown.Contains(identity))
            {
                return KeyboardHookAction.Consume;
            }

            AppendEventUnlocked(session, tokenInfo.Token, isKeyUp: ev.IsKeyUp);

            if (!ev.IsKeyUp)
            {
                session.PhysicalDown.Add(identity);
                session.OwnedSuppressed.Add(identity);
            }
            else
            {
                session.PhysicalDown.Remove(identity);
                session.OwnedSuppressed.Remove(identity);
            }

            if (session.Events.Count >= KeySenderConst.RecordingEventCap)
            {
                session.Notices.Add(ValidationCodes.RecordingLimitReached);
                BeginDrainUnlocked(session, discard: false);
            }
            else
            {
                MaybeNotifyProgressUnlocked(session);
            }

            return KeyboardHookAction.Consume;
        }
    }

    private KeyboardHookAction HandleDrainUnlocked(
        Session session,
        RawKeyboardEvent ev,
        PhysicalKeyIdentity identity)
    {
        // Only consume owned suppressed keys' repeat Downs and pairing Ups; do not append.
        if (!session.OwnedSuppressed.Contains(identity))
        {
            return KeyboardHookAction.PassThrough;
        }

        if (ev.IsKeyUp)
        {
            session.OwnedSuppressed.Remove(identity);
            session.PhysicalDown.Remove(identity);
        }

        CompleteDrainIfReadyUnlocked();
        return KeyboardHookAction.Consume;
    }

    private void AppendEventUnlocked(Session session, string token, bool isKeyUp)
    {
        var now = _clock.ElapsedMilliseconds;
        if (session.Events.Count == 0)
        {
            session.FirstEventAtMs = now;
            session.LastEventAtMs = now;
        }
        else
        {
            session.LastEventAtMs = now;
        }

        // Recording defaults all intervals to 0; users adjust timing in the detail editor.
        session.Events.Add(new KeyboardEvent
        {
            EventId = Guid.NewGuid().ToString("D"),
            Type = isKeyUp ? KeySenderConst.EventTypeKeyUp : KeySenderConst.EventTypeKeyDown,
            Key = token,
            DelayBeforeMs = 0,
        });
    }

    private void TryEnterRecordingIfBaselineClearUnlocked()
    {
        var session = _session;
        if (session is null || session.State != RecordingState.Preparing)
        {
            return;
        }

        if (session.BaselineHeld.Count == 0
            || _clock.ElapsedMilliseconds >= session.BaselineDeadlineMs)
        {
            session.State = RecordingState.Recording;
            session.SequenceNumber++;
        }
    }

    private void BeginDrainUnlocked(Session session, bool discard)
    {
        if (session.State is RecordingState.Draining or RecordingState.Review
            or RecordingState.Cancelled or RecordingState.Failed)
        {
            return;
        }

        session.Frozen = true;
        session.DiscardAfterDrain = discard;
        session.State = RecordingState.Draining;
        session.DrainDeadlineMs = _clock.ElapsedMilliseconds + KeySenderConst.RecordingDrainTimeoutMs;
        session.SequenceNumber++;

        CompleteDrainIfReadyUnlocked();
    }

    private void CompleteDrainIfReadyUnlocked()
    {
        var session = _session;
        if (session is null || session.State != RecordingState.Draining)
        {
            return;
        }

        var timedOut = _clock.ElapsedMilliseconds >= session.DrainDeadlineMs;
        if (session.OwnedSuppressed.Count > 0 && !timedOut)
        {
            return;
        }

        if (timedOut && session.OwnedSuppressed.Count > 0 && !session.DiscardAfterDrain)
        {
            session.Notices.Add(ValidationCodes.RecordingReleaseTimeout);
        }

        try
        {
            _hook.Stop();
        }
        catch (Exception ex)
        {
            session.State = RecordingState.Failed;
            session.Error = ValidationCodes.RecordingCleanupFailed;
            session.Notices.Add(ValidationCodes.RecordingCleanupFailed);
            session.SequenceNumber++;
            _gate.EndRecordingReservation();
            Debug.WriteLine(ex);
            return;
        }

        if (session.DiscardAfterDrain)
        {
            DiscardAndEndUnlocked(session, RecordingState.Cancelled);
            return;
        }

        session.State = RecordingState.Review;
        session.OwnedSuppressed.Clear();
        session.PhysicalDown.Clear();
        session.SequenceNumber++;
        // Keep gate reservation until Apply/Cancel.
    }

    private void DiscardAndEndUnlocked(Session session, RecordingState terminal)
    {
        session.Events.Clear();
        session.OwnedSuppressed.Clear();
        session.PhysicalDown.Clear();
        session.State = terminal;
        session.SequenceNumber++;
        try
        {
            if (_hook.IsRunning)
            {
                _hook.Stop();
            }
        }
        catch
        {
            // best-effort
        }

        _gate.EndRecordingReservation();
    }

    private void EndSessionUnlocked(bool clearReservation)
    {
        if (_hook.IsRunning)
        {
            try
            {
                _hook.Stop();
            }
            catch
            {
                // best-effort
            }
        }

        _session = null;
        if (clearReservation)
        {
            _gate.EndRecordingReservation();
        }
    }

    private void MaybeNotifyProgressUnlocked(Session session)
    {
        var now = _clock.ElapsedMilliseconds;
        if (now - session.LastNotifyMs < 100)
        {
            return;
        }

        session.LastNotifyMs = now;
        session.SequenceNumber++;
        RaiseChanged(SnapshotUnlocked()!);
    }

    private bool TryGetSession(string recordingId, out Session session)
    {
        session = _session!;
        return _session is not null
               && string.Equals(_session.RecordingId, recordingId, StringComparison.Ordinal);
    }

    private RecordingSnapshot? SnapshotUnlocked()
    {
        var session = _session;
        if (session is null)
        {
            return null;
        }

        var elapsed = Math.Max(0, _clock.ElapsedMilliseconds - session.StartedAtMs);
        return new RecordingSnapshot
        {
            RecordingId = session.RecordingId,
            WindowSessionId = session.WindowSessionId,
            State = session.State,
            EventCount = session.Events.Count,
            ElapsedMs = elapsed,
            SequenceNumber = session.SequenceNumber,
            Notices = session.Notices.ToArray(),
            Error = session.Error,
        };
    }

    private RecordingFragment? BuildFragmentUnlocked()
    {
        var session = _session;
        if (session is null)
        {
            return null;
        }

        return new RecordingFragment
        {
            RecordingId = session.RecordingId,
            Events = session.Events.Select(CloneEvent).ToList(),
            Notices = session.Notices.ToArray(),
        };
    }

    private static KeyboardEvent CloneEvent(KeyboardEvent ev) => new()
    {
        EventId = ev.EventId,
        Type = ev.Type,
        Key = ev.Key,
        DelayBeforeMs = ev.DelayBeforeMs,
    };

    private void RaiseChanged(RecordingSnapshot snapshot)
    {
        RecordingChanged?.Invoke(this, snapshot);
    }

    private sealed class Session
    {
        public required string RecordingId { get; init; }
        public required string WindowSessionId { get; init; }
        public RecordingState State { get; set; }
        public long SequenceNumber { get; set; }
        public long StartedAtMs { get; set; }
        public long FirstEventAtMs { get; set; }
        public long LastEventAtMs { get; set; }
        public long LastNotifyMs { get; set; }
        public long BaselineDeadlineMs { get; set; }
        public long DrainDeadlineMs { get; set; }
        public bool Frozen { get; set; }
        public bool DiscardAfterDrain { get; set; }
        public string? Error { get; set; }
        public List<KeyboardEvent> Events { get; } = [];
        public List<string> Notices { get; } = [];
        public HashSet<PhysicalKeyIdentity> BaselineHeld { get; } = [];
        public HashSet<PhysicalKeyIdentity> PhysicalDown { get; } = [];
        public HashSet<PhysicalKeyIdentity> OwnedSuppressed { get; } = [];
    }
}
