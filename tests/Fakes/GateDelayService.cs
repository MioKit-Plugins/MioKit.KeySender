using KeySender.Services;

namespace MioKit.KeySender.Tests.Fakes;

/// <summary>
/// Completes immediately for delays below <see cref="BlockThresholdMs"/>;
/// longer delays wait until <see cref="Release"/> or cancellation.
/// </summary>
public sealed class GateDelayService : IDelayService
{
    private readonly object _sync = new();
    private TaskCompletionSource? _gate;

    public int BlockThresholdMs { get; set; } = 1;

    public Task? CurrentGate
    {
        get
        {
            lock (_sync)
            {
                return _gate?.Task;
            }
        }
    }

    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (milliseconds < BlockThresholdMs)
        {
            return Task.CompletedTask;
        }

        TaskCompletionSource gate;
        lock (_sync)
        {
            gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _gate = gate;
        }

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => gate.TrySetCanceled(cancellationToken));
        }

        return gate.Task;
    }

    public void Release()
    {
        TaskCompletionSource? gate;
        lock (_sync)
        {
            gate = _gate;
            _gate = null;
        }

        gate?.TrySetResult();
    }
}
