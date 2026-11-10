using KeySender.Models;
using KeySender.Validation;
using Serilog;

namespace KeySender.Services;

/// <summary>
/// Serial FIFO runner (max 64 waiting + 1 active). Sends only configured events.
/// Cancellation and shutdown stop future sends without synthesizing key releases.
/// </summary>
public sealed class ExecutionService : IKeySenderExecutionService, IInputActivityGate, IPluginLifecycleComponent
{
    public const int MaxQueued = 64;

    private readonly IKeyboardSender _sender;
    private readonly TargetWindowService _targets;
    private readonly IRunConfigurationSource _configs;
    private readonly IDelayService _delay;
    private readonly IMioTaskService? _tasks;
    private readonly IGlobalHotKeyService? _hotkeys;
    private readonly ILogger? _logger;

    private readonly object _gate = new();
    private readonly Queue<RunRequest> _queue = new();
    private readonly List<RunSnapshot> _recent = [];

    private RunRequest? _active;
    private Task? _drainLoop;
    private bool _accepting = true;
    private bool _recordingReserved;

    public ExecutionService(
        IKeyboardSender sender,
        TargetWindowService targets,
        IRunConfigurationSource configs,
        IDelayService delay,
        IMioTaskService? tasks = null,
        IGlobalHotKeyService? hotkeys = null,
        ILogger? logger = null)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _configs = configs ?? throw new ArgumentNullException(nameof(configs));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        _tasks = tasks;
        _hotkeys = hotkeys;
        _logger = logger;
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
            if (!_accepting)
            {
                rejectCode = ValidationCodes.PluginStopping;
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
            queued.Cts.Cancel();
            CompleteRequest(queued, RunState.Cancelled, error: null);
            return true;
        }

        if (active is not null)
        {
            active.Cts.Cancel();
            return true;
        }

        return false;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) { _accepting = true; }
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        List<RunRequest> queued;
        RunRequest? active;
        Task? drain;
        lock (_gate)
        {
            _accepting = false;
            queued = _queue.ToList();
            _queue.Clear();
            active = _active;
            drain = _drainLoop;
        }

        foreach (var request in queued)
        {
            request.Cts.Cancel();
            CompleteRequest(request, RunState.Cancelled, error: null);
        }
        active?.Cts.Cancel();
        if (drain is not null)
        {
            await drain.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private Task EnqueueCoreAsync(
        string nodeId,
        string? nodeName,
        TargetIdentity target,
        InvokeSource source,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_accepting)
            {
                throw new InvalidOperationException($"{ValidationCodes.PluginStopping}: execution not accepting");
            }

            if (_recordingReserved)
            {
                throw new InvalidOperationException($"{ValidationCodes.RecordingBusy}: recording reserved");
            }

            if (_queue.Count >= MaxQueued)
            {
                throw new InvalidOperationException($"{ValidationCodes.QueueFull}: waiting queue is full");
            }

            var request = new RunRequest(nodeId, nodeName, target, source, cancellationToken);
            request.SetState(RunState.Queued);
            _queue.Enqueue(request);
            _drainLoop ??= Task.Run(DrainAsync);
            return request.Completion.Task;
        }
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            RunRequest next;
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

            try
            {
                await RunManagedAsync(next).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!next.Completion.Task.IsCompleted)
                    CompleteRequest(next, RunState.Cancelled, error: null);
            }
            catch (Exception ex)
            {
                _logger?.Warning(ex, "KeySender managed run failed {RunId}", next.RunId);
                if (!next.Completion.Task.IsCompleted)
                    CompleteRequest(next, RunState.Failed, ValidationCodes.InputSendFailed);
            }
            finally
            {
                lock (_gate) { _active = null; }
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

        return _tasks.RunAsync(options, async ct =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(request.Token, ct);
            await ExecuteRunAsync(request, linked.Token).ConfigureAwait(false);
        }, request.Token);
    }

    private async Task ExecuteRunAsync(RunRequest request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            CompleteRequest(request, RunState.Cancelled, error: null);
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

            var analysis = SequenceAnalyzer.AnalyzeSequence(write);
            if (!analysis.IsValid)
            {
                failCode = analysis.HardErrors.FirstOrDefault()?.Code ?? ValidationCodes.ValidationFailed;
                throw new InvalidOperationException($"{failCode}: hard validation failed");
            }

            var conflict = PreviewHotkeyConflicts(write);
            if (conflict is not null)
            {
                failCode = conflict;
                throw new InvalidOperationException($"{failCode}: output hotkey conflict");
            }

            request.EventCount = write.Sequence.Events?.Count ?? 0;

            request.SetState(RunState.BeforeDelay);
            await _delay.DelayAsync(write.BeforeDelayMs, cancellationToken).ConfigureAwait(false);

            var logicalPressed = new HashSet<string>(StringComparer.Ordinal);
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

                if (!sendResult.Submitted)
                {
                    failCode = ValidationCodes.InputSendFailed;
                    throw new InvalidOperationException($"{failCode}: SendInput failed (native={sendResult.NativeError})");
                }
                ApplyLogical(logicalPressed, ev);
            }

            request.SetState(RunState.AfterDelay);
            await _delay.DelayAsync(write.AfterDelayMs, cancellationToken).ConfigureAwait(false);

            CompleteRequest(request, RunState.Succeeded, error: null);
        }
        catch (OperationCanceledException)
        {
            CompleteRequest(request, RunState.Cancelled, error: null);
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "KeySender run failed {RunId} {NodeId}", request.RunId, request.NodeId);
            CompleteRequest(request, RunState.Failed, failCode ?? ValidationCodes.InputSendFailed);
        }
    }

    private void CompleteRequest(RunRequest request, RunState state, string? error)
    {
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

    private string? PreviewHotkeyConflicts(NodeWrite write)
    {
        var pressed = new HashSet<string>(StringComparer.Ordinal);
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
