namespace KeySender.Models;

/// <summary>
/// NodeWrite plus identity/capability fields for GetDetail responses.
/// </summary>
public sealed class NodeDetail
{
    public required string NodeId { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public int BeforeDelayMs { get; init; }

    public int AfterDelayMs { get; init; }

    public required KeyboardSequence Sequence { get; init; }

    public bool IsBuiltIn { get; init; }

    public bool IsReadOnly { get; init; }

    public bool CanEdit { get; init; }

    public bool CanDelete { get; init; }

    public NodeWrite ToWrite() => new()
    {
        Name = Name,
        Description = Description,
        BeforeDelayMs = BeforeDelayMs,
        AfterDelayMs = AfterDelayMs,
        Sequence = Sequence,
    };
}
