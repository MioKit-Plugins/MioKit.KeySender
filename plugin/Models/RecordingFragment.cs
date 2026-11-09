namespace KeySender.Models;

public sealed class RecordingFragment
{
    public required string RecordingId { get; init; }

    public IReadOnlyList<KeyboardEvent> Events { get; init; } = [];

    public IReadOnlyList<string> Notices { get; init; } = [];
}
