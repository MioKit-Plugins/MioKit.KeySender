using KeySender.Models;

namespace KeySender.Services;

/// <summary>
/// Execution coordinator: FIFO queue, cancellation, recording gate.
/// <see cref="ExecuteAsync"/> remains the invoke adapter.
/// </summary>
public interface IKeySenderExecutionService
{
    /// <summary>
    /// Synchronously extract light fields from context, enqueue, and await that run's completion.
    /// Must not retain <see cref="InvokeContext"/> / SearchResult across the queue wait.
    /// </summary>
    Task ExecuteAsync(KeySenderNode node, InvokeContext context);

    Task EnqueueAsync(
        string nodeId,
        string? nodeName,
        TargetIdentity target,
        InvokeSource source,
        CancellationToken cancellationToken = default);

    bool TryCancel(Guid runId);

    IReadOnlyList<RunSnapshot> GetActiveAndQueuedRuns();

    IReadOnlyList<RunSnapshot> GetRecentRuns();

}
