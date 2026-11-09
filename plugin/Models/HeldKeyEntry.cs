namespace KeySender.Models;

/// <summary>
/// One plugin-owned logical held/uncertain key. Not reference-counted.
/// </summary>
public sealed class HeldKeyEntry
{
    public required string Key { get; init; }

    public required ushort VirtualKey { get; init; }

    public required ushort ScanCode { get; init; }

    public required bool Extended { get; init; }

    public required Guid SourceRunId { get; init; }

    public required string SourceNodeId { get; init; }

    public required DateTimeOffset HeldSince { get; init; }

    public bool Uncertain { get; set; }
}
