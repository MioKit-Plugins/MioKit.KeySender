namespace KeySender.Models;

/// <summary>
/// Lightweight recording status (design 04). Does not carry the full event list.
/// </summary>
public sealed class RecordingSnapshot
{
    public required string RecordingId { get; init; }

    public required string WindowSessionId { get; init; }

    public RecordingState State { get; init; }

    public int EventCount { get; init; }

    public long ElapsedMs { get; init; }

    public long SequenceNumber { get; init; }

    public IReadOnlyList<string> Notices { get; init; } = [];

    public string? Error { get; init; }
}
