namespace KeySender.Validation;

/// <summary>
/// Result of <see cref="SequenceAnalyzer.AnalyzeSequence"/>.
/// <see cref="IsValid"/> means no hard errors; warnings may still require confirmation.
/// </summary>
public sealed class SequenceAnalysisResult
{
    public required IReadOnlyList<ValidationIssue> HardErrors { get; init; }

    public required IReadOnlyList<ValidationIssue> Warnings { get; init; }

    /// <summary>
    /// UTF-8 SHA-256 lowercase hex of the semantic JSON fingerprint.
    /// </summary>
    public required string WarningFingerprint { get; init; }

    public bool IsValid => HardErrors.Count == 0;

    public bool HasWarnings => Warnings.Count > 0;
}
