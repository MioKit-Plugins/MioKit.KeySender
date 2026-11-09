namespace KeySender.Models;

/// <summary>
/// Versioned ordered event list. First-release version is 2.
/// </summary>
public sealed class KeyboardSequence
{
    public int Version { get; set; } = KeySenderConst.SequenceVersion;

    public List<KeyboardEvent> Events { get; set; } = [];
}
