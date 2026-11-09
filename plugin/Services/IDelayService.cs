namespace KeySender.Services;

/// <summary>Clock/delay seam so unit tests avoid real multi-second waits.</summary>
public interface IDelayService
{
    Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default);
}

/// <summary>Production delay using <see cref="Task.Delay(int, CancellationToken)"/>.</summary>
public sealed class TaskDelayService : IDelayService
{
    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default)
    {
        if (milliseconds <= 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        return Task.Delay(milliseconds, cancellationToken);
    }
}
