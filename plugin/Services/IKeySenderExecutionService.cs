using KeySender.Models;

namespace KeySender.Services;

/// <summary>
/// Execution coordinator: FIFO queue, ledger updates, ReleaseAll, recording gate.
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

    Task<HeldKeysSnapshot> ReleaseAllAsync(CancellationToken cancellationToken = default);

    HeldKeysSnapshot GetHeldKeys();

    IReadOnlyList<RunSnapshot> GetActiveAndQueuedRuns();

    IReadOnlyList<RunSnapshot> GetRecentRuns();

    bool SenderAvailable { get; }
}
