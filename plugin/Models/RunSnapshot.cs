namespace KeySender.Models;

/// <summary>Lightweight run status for UI / tests (design 04).</summary>
public sealed class RunSnapshot
{
    public required Guid RunId { get; init; }

    public required string NodeId { get; init; }

    public string? NodeName { get; init; }

    public required RunState State { get; init; }

    public int? EventIndex { get; init; }

    public int? EventCount { get; init; }

    public int? QueuePosition { get; init; }

    public DateTimeOffset SubmittedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public bool IsCancellationRequested { get; init; }

    public string? Error { get; init; }
}
