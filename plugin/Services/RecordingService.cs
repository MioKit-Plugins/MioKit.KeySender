using KeySender.Models;
using KeySender.Validation;

namespace KeySender.Services;

/// <summary>Records key transitions without waiting for or synthesizing key releases.</summary>
public sealed class RecordingService : IDisposable, IPluginLifecycleComponent
{
    private readonly IInputActivityGate _gate;
    private readonly IKeyboardHookSource _hook;
    private readonly ITargetWindowOps _windows;
    private readonly IMonotonicClock _clock;
    private readonly object _sync = new();
    private Session? _session;
    private bool _disposed;

    public RecordingService(IInputActivityGate gate, IKeyboardHookSource hook,
        ITargetWindowOps windows, IMonotonicClock? clock = null)
    {
        _gate = gate;
        _hook = hook;
        _windows = windows;
        _clock = clock ?? new StopwatchMonotonicClock();
    }

    public event EventHandler<RecordingSnapshot>? RecordingChanged;

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = GetSnapshot();
        if (snapshot?.State is RecordingState.Recording or RecordingState.Review)
            Cancel(snapshot.RecordingId);
        return Task.CompletedTask;
    }

    public RecordingOperationResult Start(string windowSessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowSessionId);
        RecordingSnapshot snapshot;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session?.State is RecordingState.Recording or RecordingState.Review)
                return RecordingOperationResult.Fail(ValidationCodes.RecordingBusy,
                    "A recording session is already active.", SnapshotUnlocked());
            if (!_gate.TryReserveForRecording(out var reject))
                return RecordingOperationResult.Fail(reject ?? ValidationCodes.RecordingUnavailable);

            _session = new Session
            {
                RecordingId = Guid.NewGuid().ToString("D"), WindowSessionId = windowSessionId,
                Window = _windows.GetForegroundWindow(), StartedAtMs = _clock.ElapsedMilliseconds,
            };
            try
            {
                if (_session.Window == 0)
                    throw new InvalidOperationException("Recording window is not foreground.");
                _hook.Start(OnHookEvent);
            }
            catch (Exception ex)
            {
                _session = null;
                _gate.EndRecordingReservation();
                return RecordingOperationResult.Fail(ValidationCodes.RecordingUnavailable, ex.Message);
            }
            snapshot = SnapshotUnlocked()!;
        }
        RaiseChanged(snapshot);
        return RecordingOperationResult.Success(snapshot);
    }

    public RecordingOperationResult Stop(string recordingId, string? notice = null)
    {
        RecordingSnapshot snapshot;
        RecordingFragment fragment;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!TryGetSession(recordingId, out var session)
                || session.State is not (RecordingState.Recording or RecordingState.Review))
                return RecordingOperationResult.Fail(ValidationCodes.RecordingNotReady, "Unknown recording session.");
            if (session.State == RecordingState.Review)
                return RecordingOperationResult.Success(SnapshotUnlocked()!, FragmentUnlocked());
            if (notice is not null) session.Notices.Add(notice);
            session.State = RecordingState.Review;
            session.SequenceNumber++;
            snapshot = SnapshotUnlocked()!;
            fragment = FragmentUnlocked();
        }
        StopHook();
        RaiseChanged(snapshot);
        return RecordingOperationResult.Success(snapshot, fragment);
    }

    public void CheckForeground()
    {
        string? id;
        lock (_sync)
        {
            id = _session is { State: RecordingState.Recording } session
                && _windows.GetForegroundWindow() != session.Window ? session.RecordingId : null;
        }
        if (id is not null) Stop(id, ValidationCodes.RecordingInterrupted);
    }

    public RecordingOperationResult Cancel(string recordingId)
    {
        RecordingSnapshot snapshot;
        lock (_sync)
        {
            if (!TryGetSession(recordingId, out var session))
                return RecordingOperationResult.Success(new RecordingSnapshot
                {
                    RecordingId = recordingId, WindowSessionId = "", State = RecordingState.Cancelled,
                });
            session.Events.Clear();
            session.State = RecordingState.Cancelled;
            session.SequenceNumber++;
            snapshot = SnapshotUnlocked()!;
        }
        StopHook();
        RaiseChanged(snapshot);
        return RecordingOperationResult.Success(snapshot);
    }

    public RecordingSnapshot? GetSnapshot(string? recordingId = null)
    {
        lock (_sync)
        {
            return recordingId is not null && _session?.RecordingId != recordingId
                ? null : SnapshotUnlocked();
        }
    }

    public RecordingOperationResult GetFragment(string recordingId)
    {
        lock (_sync)
        {
            if (!TryGetSession(recordingId, out var session) || session.State != RecordingState.Review)
                return RecordingOperationResult.Fail(ValidationCodes.RecordingNotReady, "Fragment is not ready.");
            return RecordingOperationResult.Success(SnapshotUnlocked()!, FragmentUnlocked());
        }
    }

    public RecordingOperationResult Apply(string recordingId)
    {
        RecordingSnapshot snapshot;
        RecordingFragment fragment;
        lock (_sync)
        {
            if (!TryGetSession(recordingId, out var session) || session.State != RecordingState.Review)
                return RecordingOperationResult.Fail(ValidationCodes.RecordingNotReady, "Fragment is not ready.");
            if (session.Events.Count == 0)
                return RecordingOperationResult.Fail(ValidationCodes.EmptySequence, "Recording fragment is empty.");
            snapshot = SnapshotUnlocked()!;
            fragment = FragmentUnlocked();
            _session = null;
        }
        RaiseChanged(new RecordingSnapshot
        {
            RecordingId = recordingId, WindowSessionId = snapshot.WindowSessionId,
            State = RecordingState.Idle, SequenceNumber = snapshot.SequenceNumber + 1,
        });
        return RecordingOperationResult.Success(snapshot, fragment);
    }

    private KeyboardHookAction OnHookEvent(RawKeyboardEvent ev)
    {
        RecordingSnapshot? changed = null;
        var stop = false;
        var action = KeyboardHookAction.PassThrough;
        lock (_sync)
        {
            if (_session is not { State: RecordingState.Recording } session || ev.Injected || ev.LowerIntegrityInjected)
                return action;
            if (_windows.GetForegroundWindow() != session.Window)
            {
                session.Notices.Add(ValidationCodes.RecordingInterrupted);
                stop = true;
            }
            else if (KeyTokenCatalog.TryResolve(ev.VkCode, ev.ScanCode, ev.Extended, out var key))
            {
                // Only the first occurrence of each state is recorded for this key.
                // This also deduplicates Ups while preserving a standalone first Up.
                if (session.LastKeyStates.TryGetValue(key.Token, out var wasUp) && wasUp == ev.IsKeyUp)
                    return KeyboardHookAction.Consume;
                session.LastKeyStates[key.Token] = ev.IsKeyUp;
                session.Events.Add(new KeyboardEvent
                {
                    EventId = Guid.NewGuid().ToString("D"), Key = key.Token,
                    Type = ev.IsKeyUp ? KeySenderConst.EventTypeKeyUp : KeySenderConst.EventTypeKeyDown,
                    DelayBeforeMs = 0,
                });
                action = KeyboardHookAction.Consume;
                if (session.Events.Count >= KeySenderConst.RecordingEventCap)
                {
                    session.Notices.Add(ValidationCodes.RecordingLimitReached);
                    stop = true;
                }
            }
            if (stop)
            {
                session.State = RecordingState.Review;
                session.SequenceNumber++;
                changed = SnapshotUnlocked();
            }
            else if (_clock.ElapsedMilliseconds - session.LastNotifyMs >= 100)
            {
                session.LastNotifyMs = _clock.ElapsedMilliseconds;
                session.SequenceNumber++;
                changed = SnapshotUnlocked();
            }
        }
        if (stop) StopHook();
        if (changed is not null) RaiseChanged(changed);
        return action;
    }

    private void StopHook()
    {
        // Callback threads also take _sync, so stop/join only after releasing that lock.
        try { _hook.Stop(); }
        finally { _gate.EndRecordingReservation(); }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _session = null;
        }
        try { _hook.Dispose(); }
        finally { _gate.EndRecordingReservation(); }
    }

    private bool TryGetSession(string id, out Session session)
    {
        session = _session!;
        return session?.RecordingId == id;
    }

    private RecordingSnapshot? SnapshotUnlocked() => _session is not { } session ? null : new()
    {
        RecordingId = session.RecordingId, WindowSessionId = session.WindowSessionId,
        State = session.State, SequenceNumber = session.SequenceNumber, EventCount = session.Events.Count,
        ElapsedMs = Math.Max(0, _clock.ElapsedMilliseconds - session.StartedAtMs),
        Notices = session.Notices.ToArray(),
    };

    private RecordingFragment FragmentUnlocked() => new()
    {
        RecordingId = _session!.RecordingId, Notices = _session.Notices.ToArray(),
        Events = _session.Events.Select(ev => new KeyboardEvent
        {
            EventId = ev.EventId, Type = ev.Type, Key = ev.Key, DelayBeforeMs = ev.DelayBeforeMs,
        }).ToList(),
    };

    private void RaiseChanged(RecordingSnapshot snapshot) => RecordingChanged?.Invoke(this, snapshot);

    private sealed class Session
    {
        public required string RecordingId { get; init; }
        public required string WindowSessionId { get; init; }
        public nint Window { get; init; }
        public RecordingState State { get; set; } = RecordingState.Recording;
        public long SequenceNumber { get; set; } = 1;
        public long StartedAtMs { get; init; }
        public long LastNotifyMs { get; set; }
        public List<KeyboardEvent> Events { get; } = [];
        public Dictionary<string, bool> LastKeyStates { get; } = new(StringComparer.Ordinal);
        public List<string> Notices { get; } = [];
    }
}
