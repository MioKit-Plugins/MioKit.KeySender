using KeySender.Models;
using KeySender.Validation;
using Serilog;

namespace KeySender.Services;

/// <summary>
/// Serial FIFO runner (max 64 waiting + 1 active). Ledger mutations only happen here.
/// Successful completion intentionally retains held keys (no automatic KeyUp).
/// </summary>
public sealed class ExecutionService : IKeySenderExecutionService, IInputActivityGate
{
    public const int MaxQueued = 64;

    private readonly HeldKeyLedger _ledger;
    private readonly IKeyboardSender _sender;
    private readonly TargetWindowService _targets;
    private readonly IRunConfigurationSource _configs;
    private readonly IDelayService _delay;
    private readonly IExternalKeyPrep _externalKeys;
    private readonly IMioTaskService? _tasks;
    private readonly IGlobalHotKeyService? _hotkeys;
    private readonly ILogger? _logger;

    private readonly object _gate = new();
    private readonly Queue<RunRequest> _queue = new();
    private readonly List<RunSnapshot> _recent = [];
    private readonly SemaphoreSlim _serial = new(1, 1);

    private RunRequest? _active;
    private Task? _drainLoop;
    private bool _accepting = true;
    private bool _senderAvailable = true;
    private bool _isReleasing;
    private bool _recordingReserved;
    private int _recentSequence;

    public ExecutionService(
        HeldKeyLedger ledger,
        IKeyboardSender sender,
        TargetWindowService targets,
        IRunConfigurationSource configs,
        IDelayService delay,
        IExternalKeyPrep externalKeys,
        IMioTaskService? tasks = null,
        IGlobalHotKeyService? hotkeys = null,
        ILogger? logger = null)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _configs = configs ?? throw new ArgumentNullException(nameof(configs));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        _externalKeys = externalKeys ?? throw new ArgumentNullException(nameof(externalKeys));
        _tasks = tasks;
        _hotkeys = hotkeys;
        _logger = logger;
    }

    public bool SenderAvailable
    {
        get
        {
            lock (_gate)
            {
                return _senderAvailable;
            }
        }
    }

    public bool IsRecordingActive
    {
        get
        {
            lock (_gate)
            {
                return _recordingReserved;
            }
        }
    }

    public bool TryReserveForRecording(out string? rejectCode)
    {
        lock (_gate)
        {
            if (!_senderAvailable)
            {
                rejectCode = ValidationCodes.SenderUnavailable;
                return false;
            }

            if (_recordingReserved)
            {
                rejectCode = ValidationCodes.RecordingBusy;
                return false;
            }

            if (_active is not null || _queue.Count > 0)
            {
                rejectCode = ValidationCodes.ExecutionBusy;
                return false;
            }

            if (!_ledger.IsEmpty)
            {
                rejectCode = ValidationCodes.KeysHeld;
                return false;
            }

            _recordingReserved = true;
            rejectCode = null;
            return true;
        }
    }

    public void EndRecordingReservation()
    {
        lock (_gate)
        {
            _recordingReserved = false;
        }
    }

    public HeldKeysSnapshot GetHeldKeys()
    {
        lock (_gate)
        {
            return _ledger.CreateSnapshot(_isReleasing, _senderAvailable);
        }
    }

    public IReadOnlyList<RunSnapshot> GetActiveAndQueuedRuns()
    {
        lock (_gate)
        {
            var list = new List<RunSnapshot>(_queue.Count + (_active is null ? 0 : 1));
            if (_active is not null)
            {
                list.Add(_active.ToSnapshot(queuePosition: null));
            }

            var pos = 0;
            foreach (var item in _queue)
            {
                list.Add(item.ToSnapshot(queuePosition: pos++));
            }

            return list;
        }
    }

    public IReadOnlyList<RunSnapshot> GetRecentRuns()
    {
        lock (_gate)
        {
            return _recent.ToArray();
        }
    }

    public Task ExecuteAsync(KeySenderNode node, InvokeContext context)
    {
        ArgumentNullException.ThrowIfNull(node);

        // Extract light fields synchronously — do not capture InvokeContext past this point.
        var nodeId = node.Id;
        var nodeName = node.GetName();
        var source = context.Source;
        var target = _targets.Extract(context);
        var invokeCt = context.CancellationToken;

        if (target.IsMissing)
        {
            throw new InvalidOperationException($"{ValidationCodes.TargetMissing}: target window missing");
        }

        return EnqueueCoreAsync(nodeId, nodeName, target, source, invokeCt);
    }

    public Task EnqueueAsync(
        string nodeId,
        string? nodeName,
        TargetIdentity target,
        InvokeSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(target);

        if (target.IsMissing)
        {
            throw new InvalidOperationException($"{ValidationCodes.TargetMissing}: target window missing");
        }

        return EnqueueCoreAsync(nodeId, nodeName, target, source, cancellationToken);
    }

    public bool TryCancel(Guid runId)
    {
        RunRequest? queued = null;
        RunRequest? active = null;
        lock (_gate)
        {
            if (_active?.RunId == runId)
            {
                active = _active;
            }
            else
            {
                var kept = new Queue<RunRequest>(_queue.Count);
                while (_queue.Count > 0)
                {
                    var item = _queue.Dequeue();
                    if (item.RunId == runId)
                    {
                        queued = item;
                    }
                    else
                    {
                        kept.Enqueue(item);
                    }
                }

                while (kept.Count > 0)
                {
                    _queue.Enqueue(kept.Dequeue());
                }
            }
        }

        if (queued is not null)
        {
            // Queued-only cancel: complete immediately, do NOT release ledger.
            queued.Cts.Cancel();
            CompleteRequest(queued, RunState.Cancelled, error: null, releaseLedger: false);
            return true;
        }

        if (active is not null)
        {
            active.Cts.Cancel();
            return true;
        }

        return false;
    }

    public async Task<HeldKeysSnapshot> ReleaseAllAsync(CancellationToken cancellationToken = default)
    {
        List<RunRequest> queued;
        RunRequest? active;
        lock (_gate)
        {
            _accepting = false;
            _isReleasing = true;
            queued = _queue.ToList();
            _queue.Clear();
            active = _active;
        }

        foreach (var item in queued)
        {
            item.Cts.Cancel();
            CompleteRequest(item, RunState.Cancelled, error: null, releaseLedger: false);
        }

        active?.Cts.Cancel();

        // Wait for the active run (and its in-flight native call) to leave the serial slot.
        if (active is not null)
        {
            try
            {
                await active.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Terminal state already published on the request.
            }
        }

        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cleanupOk = await CleanupHeldKeysAsync(CancellationToken.None).ConfigureAwait(false);
            lock (_gate)
            {
                _isReleasing = false;
                if (cleanupOk)
                {
                    _senderAvailable = true;
                    _accepting = true;
                }
                else
                {
                    _senderAvailable = false;
                    _accepting = false;
                }

                return _ledger.CreateSnapshot(_isReleasing, _senderAvailable);
            }
        }
        finally
        {
            _serial.Release();
        }
    }

    private Task EnqueueCoreAsync(
        string nodeId,
        string? nodeName,
        TargetIdentity target,
        InvokeSource source,
        CancellationToken cancellationToken)
    {
        var request = new RunRequest(nodeId, nodeName, target, source, cancellationToken);

        lock (_gate)
        {
            if (!_accepting || !_senderAvailable)
            {
                var code = !_senderAvailable
                    ? ValidationCodes.SenderUnavailable
                    : ValidationCodes.PluginStopping;
                throw new InvalidOperationException($"{code}: execution not accepting");
            }

            if (_recordingReserved)
            {
                throw new InvalidOperationException($"{ValidationCodes.RecordingBusy}: recording reserved");
            }

            if (_queue.Count >= MaxQueued)
            {
                throw new InvalidOperationException($"{ValidationCodes.QueueFull}: waiting queue is full");
            }

            request.SetState(RunState.Queued);
            _queue.Enqueue(request);
            _drainLoop ??= Task.Run(DrainAsync);
        }

        return request.Completion.Task;
    }

    private async Task DrainAsync()
    {
        try
        {
            while (true)
            {
                RunRequest? next;
                lock (_gate)
                {
                    if (_queue.Count == 0)
                    {
                        _drainLoop = null;
                        return;
                    }

                    next = _queue.Dequeue();
                    _active = next;
                }

                await _serial.WaitAsync().ConfigureAwait(false);
                try
                {
                    try
                    {
                        await RunManagedAsync(next).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // Run terminal state is published on the request; keep draining.
                        _logger?.Warning(ex, "KeySender run ended with exception {RunId}", next.RunId);
                        if (!next.Completion.Task.IsCompleted)
                        {
                            CompleteRequest(
                                next,
                                RunState.Failed,
                                ValidationCodes.InputSendFailed,
                                releaseLedger: false);
                        }
                    }
                }
                finally
                {
                    lock (_gate)
                    {
                        if (ReferenceEquals(_active, next))
                        {
                            _active = null;
                        }
                    }

                    _serial.Release();
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "KeySender drain loop failed");
            lock (_gate)
            {
                _drainLoop = null;
            }
        }
    }

    private Task RunManagedAsync(RunRequest request)
    {
        if (_tasks is null)
        {
            return ExecuteRunAsync(request, request.Token);
        }

        var options = new MioTaskOptions
        {
            Name = "模拟按键",
            Description = request.NodeName ?? request.NodeId,
            Source = request.Source.ToString(),
            NodeId = request.NodeId,
        };

        return _tasks.RunAsync(options, ct =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(request.Token, ct);
            return ExecuteRunAsync(request, linked.Token);
        }, request.Token);
    }

    private async Task ExecuteRunAsync(RunRequest request, CancellationToken cancellationToken)
    {
        // If ReleaseAll already owns cleanup, exit quietly after cancel.
        bool releasing;
        lock (_gate)
        {
            releasing = _isReleasing;
        }

        if (releasing || cancellationToken.IsCancellationRequested)
        {
            CompleteRequest(request, RunState.Cancelled, error: null, releaseLedger: !releasing);
            return;
        }

        request.StartedAt = DateTimeOffset.UtcNow;
        string? failCode = null;

        try
        {
            request.SetState(RunState.Loading);
            var config = await _configs.GetAsync(request.NodeId, cancellationToken).ConfigureAwait(false);
            if (config is null)
            {
                failCode = ValidationCodes.NodeNotFound;
                throw new InvalidOperationException($"{failCode}: node not found");
            }

            request.NodeName ??= config.Write.Name;
            var write = config.Write;

            IReadOnlyCollection<string> initialHeld;
            lock (_gate)
            {
                initialHeld = _ledger.Tokens.ToArray();
            }

            var analysis = SequenceAnalyzer.AnalyzeSequence(write, initialHeld);
            if (!analysis.IsValid)
            {
                failCode = analysis.HardErrors.FirstOrDefault()?.Code ?? ValidationCodes.ValidationFailed;
                throw new InvalidOperationException($"{failCode}: hard validation failed");
            }

            var conflict = PreviewHotkeyConflicts(write, initialHeld);
            if (conflict is not null)
            {
                failCode = conflict;
                throw new InvalidOperationException($"{failCode}: output hotkey conflict");
            }

            request.EventCount = write.Sequence.Events?.Count ?? 0;

            request.SetState(RunState.BeforeDelay);
            await _delay.DelayAsync(write.BeforeDelayMs, cancellationToken).ConfigureAwait(false);

            request.SetState(RunState.WaitingExternalKeys);
            var firstUps = CollectExplicitFirstUps(write);
            await _externalKeys.WaitReadyAsync(
                    new HashSet<string>(initialHeld, StringComparer.Ordinal),
                    firstUps,
                    cancellationToken)
                .ConfigureAwait(false);

            var logicalPressed = new HashSet<string>(initialHeld, StringComparer.Ordinal);
            var events = write.Sequence.Events ?? [];
            for (var i = 0; i < events.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ev = events[i];
                request.EventIndex = i;
                request.SetState(RunState.EventDelay);
                await _delay.DelayAsync(ev.DelayBeforeMs, cancellationToken).ConfigureAwait(false);

                var focusError = await _targets.EnsureForegroundAsync(request.Target, cancellationToken)
                    .ConfigureAwait(false);
                if (focusError is not null)
                {
                    failCode = focusError;
                    throw new InvalidOperationException($"{focusError}: target focus failed");
                }

                // Per-event hotkey re-check against projected state after this event.
                var projected = new HashSet<string>(logicalPressed, StringComparer.Ordinal);
                ApplyLogical(projected, ev);
                var perEventConflict = OutputHotkeyConflict.FindConflict(_hotkeys, projected);
                if (perEventConflict is not null)
                {
                    failCode = perEventConflict;
                    throw new InvalidOperationException($"{perEventConflict}: output hotkey conflict");
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (!KeyTokenCatalog.TryGet(ev.Key, out var keyInfo))
                {
                    failCode = ValidationCodes.UnsupportedKey;
                    throw new InvalidOperationException($"{failCode}: unknown key");
                }

                var isDown = string.Equals(ev.Type, KeySenderConst.EventTypeKeyDown, StringComparison.Ordinal);
                request.SetState(RunState.Sending);
                KeyboardSendResult sendResult;
                try
                {
                    sendResult = await _sender.SendAsync(keyInfo, isDown, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    sendResult = KeyboardSendResult.Ambiguous();
                }

                if (sendResult.Submitted)
                {
                    ApplyLedger(keyInfo, isDown, uncertain: false, request);
                    ApplyLogical(logicalPressed, ev);
                }
                else if (sendResult.Uncertain && isDown)
                {
                    ApplyLedger(keyInfo, isDown: true, uncertain: true, request);
                    failCode = ValidationCodes.InputSendFailed;
                    throw new InvalidOperationException($"{failCode}: uncertain send");
                }
                else
                {
                    failCode = ValidationCodes.InputSendFailed;
                    throw new InvalidOperationException(
                        $"{failCode}: SendInput failed (native={sendResult.NativeError})");
                }
            }

            request.SetState(RunState.AfterDelay);
            await _delay.DelayAsync(write.AfterDelayMs, cancellationToken).ConfigureAwait(false);

            // CRITICAL: succeeded WITH held keys retained — no automatic KeyUp.
            CompleteRequest(request, RunState.Succeeded, error: null, releaseLedger: false);
        }
        catch (OperationCanceledException)
        {
            request.SetState(RunState.Cancelling);
            bool skipCleanup;
            lock (_gate)
            {
                skipCleanup = _isReleasing;
            }

            if (!skipCleanup)
            {
                CancelAllQueued();
                var ok = await CleanupHeldKeysAsync(CancellationToken.None).ConfigureAwait(false);
                if (!ok)
                {
                    lock (_gate)
                    {
                        _senderAvailable = false;
                        _accepting = false;
                    }
                }
            }

            CompleteRequest(request, RunState.Cancelled, error: null, releaseLedger: false);
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "KeySender run failed {RunId} {NodeId}", request.RunId, request.NodeId);
            failCode ??= ValidationCodes.InputSendFailed;

            bool skipCleanup;
            lock (_gate)
            {
                skipCleanup = _isReleasing;
            }

            if (!skipCleanup)
            {
                CancelAllQueued();
                var ok = await CleanupHeldKeysAsync(CancellationToken.None).ConfigureAwait(false);
                if (!ok)
                {
                    lock (_gate)
                    {
                        _senderAvailable = false;
                        _accepting = false;
                    }
                }
            }

            CompleteRequest(request, RunState.Failed, error: failCode, releaseLedger: false);
        }
    }

    private void ApplyLedger(KeyTokenInfo key, bool isDown, bool uncertain, RunRequest request)
    {
        // Serial path only (held by _serial).
        if (isDown)
        {
            _ledger.NoteDown(key, request.RunId, request.NodeId, DateTimeOffset.UtcNow, uncertain);
        }
        else if (!uncertain)
        {
            _ledger.NoteUp(key.Token);
        }
    }

    private async Task<bool> CleanupHeldKeysAsync(CancellationToken cancellationToken)
    {
        var toRelease = _ledger.SnapshotReverseHoldOrder();
        if (toRelease.Count == 0)
        {
            return true;
        }

        var allOk = true;
        foreach (var entry in toRelease)
        {
            if (!KeyTokenCatalog.TryGet(entry.Key, out var keyInfo))
            {
                keyInfo = new KeyTokenInfo(entry.Key, entry.VirtualKey, entry.ScanCode, entry.Extended);
            }

            var ok = await TrySendCleanupUpAsync(keyInfo, cancellationToken).ConfigureAwait(false);
            if (ok)
            {
                _ledger.NoteUp(entry.Key);
            }
            else
            {
                allOk = false;
            }
        }

        return allOk;
    }

    private async Task<bool> TrySendCleanupUpAsync(KeyTokenInfo key, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await _sender.SendAsync(key, keyDown: false, cancellationToken).ConfigureAwait(false);
                if (result.Submitted)
                {
                    return true;
                }
            }
            catch
            {
                // Retry once per design 03 §10.
            }
        }

        return false;
    }

    private void CancelAllQueued()
    {
        List<RunRequest> queued;
        lock (_gate)
        {
            queued = _queue.ToList();
            _queue.Clear();
        }

        foreach (var item in queued)
        {
            item.Cts.Cancel();
            CompleteRequest(item, RunState.Cancelled, error: null, releaseLedger: false);
        }
    }

    private void CompleteRequest(RunRequest request, RunState state, string? error, bool releaseLedger)
    {
        if (releaseLedger)
        {
            // Only used when a cancelled active run must clean without going through catch —
            // ReleaseAll path sets releaseLedger=false and cleans itself.
        }

        request.FinishedAt = DateTimeOffset.UtcNow;
        request.SetState(state);
        request.Error = error;

        lock (_gate)
        {
            _recent.Insert(0, request.ToSnapshot(queuePosition: null));
            while (_recent.Count > 50)
            {
                _recent.RemoveAt(_recent.Count - 1);
            }

            _recentSequence++;
        }

        if (state == RunState.Succeeded)
        {
            request.Completion.TrySetResult();
        }
        else if (state == RunState.Cancelled)
        {
            request.Completion.TrySetCanceled(request.Token);
        }
        else
        {
            request.Completion.TrySetException(
                new InvalidOperationException($"{error ?? ValidationCodes.InputSendFailed}: run failed"));
        }
    }

    private string? PreviewHotkeyConflicts(NodeWrite write, IEnumerable<string> initialHeld)
    {
        var pressed = new HashSet<string>(initialHeld, StringComparer.Ordinal);
        var conflict = OutputHotkeyConflict.FindConflict(_hotkeys, pressed);
        if (conflict is not null)
        {
            return conflict;
        }

        foreach (var ev in write.Sequence.Events ?? [])
        {
            ApplyLogical(pressed, ev);
            conflict = OutputHotkeyConflict.FindConflict(_hotkeys, pressed);
            if (conflict is not null)
            {
                return conflict;
            }
        }

        return null;
    }

    private static void ApplyLogical(HashSet<string> pressed, KeyboardEvent ev)
    {
        if (string.Equals(ev.Type, KeySenderConst.EventTypeKeyDown, StringComparison.Ordinal))
        {
            pressed.Add(ev.Key);
        }
        else if (string.Equals(ev.Type, KeySenderConst.EventTypeKeyUp, StringComparison.Ordinal))
        {
            pressed.Remove(ev.Key);
        }
    }

    private static HashSet<string> CollectExplicitFirstUps(NodeWrite write)
    {
        var seenDown = new HashSet<string>(StringComparer.Ordinal);
        var firstUps = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ev in write.Sequence.Events ?? [])
        {
            if (string.Equals(ev.Type, KeySenderConst.EventTypeKeyDown, StringComparison.Ordinal))
            {
                seenDown.Add(ev.Key);
            }
            else if (string.Equals(ev.Type, KeySenderConst.EventTypeKeyUp, StringComparison.Ordinal)
                     && !seenDown.Contains(ev.Key))
            {
                firstUps.Add(ev.Key);
            }
        }

        return firstUps;
    }

    private sealed class RunRequest
    {
        public RunRequest(
            string nodeId,
            string? nodeName,
            TargetIdentity target,
            InvokeSource source,
            CancellationToken externalToken)
        {
            RunId = Guid.NewGuid();
            NodeId = nodeId;
            NodeName = nodeName;
            Target = target;
            Source = source;
            SubmittedAt = DateTimeOffset.UtcNow;
            Cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            Completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Guid RunId { get; }
        public string NodeId { get; }
        public string? NodeName { get; set; }
        public TargetIdentity Target { get; }
        public InvokeSource Source { get; }
        public DateTimeOffset SubmittedAt { get; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public RunState State { get; private set; } = RunState.Queued;
        public int? EventIndex { get; set; }
        public int? EventCount { get; set; }
        public string? Error { get; set; }
        public CancellationTokenSource Cts { get; }
        public CancellationToken Token => Cts.Token;
        public TaskCompletionSource Completion { get; }

        public void SetState(RunState state) => State = state;

        public RunSnapshot ToSnapshot(int? queuePosition) => new()
        {
            RunId = RunId,
            NodeId = NodeId,
            NodeName = NodeName,
            State = State,
            EventIndex = EventIndex,
            EventCount = EventCount,
            QueuePosition = queuePosition,
            SubmittedAt = SubmittedAt,
            StartedAt = StartedAt,
            FinishedAt = FinishedAt,
            IsCancellationRequested = Cts.IsCancellationRequested,
            Error = Error,
        };
    }
}
