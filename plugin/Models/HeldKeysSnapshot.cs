namespace KeySender.Models;

/// <summary>
/// UI-facing held-key snapshot (design 04). Independent of Run terminal state.
/// </summary>
public sealed class HeldKeysSnapshot
{
    public long SequenceNumber { get; init; }

    public IReadOnlyList<HeldKeyEntry> Keys { get; init; } = [];

    public bool IsReleasing { get; init; }

    public bool SenderAvailable { get; init; } = true;
}
