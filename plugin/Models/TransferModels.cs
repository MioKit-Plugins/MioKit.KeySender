using KeySender.Validation;

namespace KeySender.Models;

public sealed class TransferParseResult
{
    public bool Ok { get; init; }

    public string? Code { get; init; }

    public string? Message { get; init; }

    public IReadOnlyList<NodeWrite> Nodes { get; init; } = [];

    public IReadOnlyList<SequenceAnalysisResult> Analyses { get; init; } = [];

    public bool CanImport { get; init; }

    public string? WarningFingerprint { get; init; }

    public byte[]? ContentSha256 { get; init; }

    public static TransferParseResult Fail(string code, string? message = null) => new()
    {
        Ok = false,
        Code = code,
        Message = message,
        CanImport = false,
    };
}

public sealed class ImportPreviewItem
{
    public int ItemIndex { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public int EventCount { get; init; }

    public long TotalDelayMs { get; init; }

    public IReadOnlyList<ValidationIssue> Errors { get; init; } = [];

    public IReadOnlyList<ValidationIssue> Warnings { get; init; } = [];
}

public sealed class ImportPreview
{
    public required string PreviewId { get; init; }

    public required string WindowSessionId { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public int ItemCount { get; init; }

    public bool CanImport { get; init; }

    public string? WarningFingerprint { get; init; }

    public IReadOnlyList<ImportPreviewItem> Items { get; init; } = [];

    public string? SourcePath { get; init; }

    public byte[] ContentSha256 { get; init; } = [];
}

public sealed class TransferOperationResult
{
    public bool Ok { get; init; }

    public string? Code { get; init; }

    public string? Message { get; init; }

    public ImportPreview? Preview { get; init; }

    public IReadOnlyList<string>? CreatedIds { get; init; }

    public IReadOnlyList<NodeDetail>? CreatedItems { get; init; }

    public bool Persisted { get; init; }

    public int ExportedCount { get; init; }

    public string? FileName { get; init; }

    public static TransferOperationResult SuccessPreview(ImportPreview preview) => new()
    {
        Ok = true,
        Preview = preview,
    };

    public static TransferOperationResult SuccessCommit(
        IReadOnlyList<string> createdIds,
        IReadOnlyList<NodeDetail> createdItems) => new()
    {
        Ok = true,
        CreatedIds = createdIds,
        CreatedItems = createdItems,
    };

    public static TransferOperationResult SuccessExport(int count, string? fileName = null) => new()
    {
        Ok = true,
        ExportedCount = count,
        FileName = fileName,
    };

    public static TransferOperationResult Fail(
        string code,
        string? message = null,
        bool persisted = false,
        IReadOnlyList<string>? createdIds = null) => new()
    {
        Ok = false,
        Code = code,
        Message = message,
        Persisted = persisted,
        CreatedIds = createdIds,
    };
}
