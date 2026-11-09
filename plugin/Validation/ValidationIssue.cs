namespace KeySender.Validation;

/// <summary>
/// One hard error or confirmable warning from sequence analysis.
/// </summary>
public sealed class ValidationIssue
{
    public required string Code { get; init; }

    public string? Message { get; init; }

    public string? FieldPath { get; init; }

    public string? Key { get; init; }

    public int? EventIndex { get; init; }
}
