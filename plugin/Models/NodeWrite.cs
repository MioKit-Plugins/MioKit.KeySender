namespace KeySender.Models;

/// <summary>
/// Editable node configuration payload used by validation and later save paths.
/// </summary>
public sealed class NodeWrite
{
    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public int BeforeDelayMs { get; set; }

    public int AfterDelayMs { get; set; }

    public KeyboardSequence Sequence { get; set; } = new();
}
