using KeySender.Validation;

namespace KeySender.Models.Ui;

/// <summary>Bridge envelope matching docs/04 (ok / data / error).</summary>
public sealed class ApiResult<T>
{
    public bool Ok { get; init; }

    public T? Data { get; init; }

    public ApiError? Error { get; init; }

    public static ApiResult<T> Success(T data) => new()
    {
        Ok = true,
        Data = data,
        Error = null,
    };

    public static ApiResult<T> Fail(
        string code,
        string? message = null,
        string? fieldPath = null,
        int? eventIndex = null,
        string? nodeId = null,
        string? runId = null,
        object? details = null) => new()
    {
        Ok = false,
        Data = default,
        Error = new ApiError
        {
            Code = code,
            Message = message ?? code,
            FieldPath = fieldPath,
            EventIndex = eventIndex,
            NodeId = nodeId,
            RunId = runId,
            Details = details,
        },
    };
}

public sealed class ApiError
{
    public required string Code { get; init; }

    public string? Message { get; init; }

    public string? FieldPath { get; init; }

    public int? EventIndex { get; init; }

    public string? NodeId { get; init; }

    public string? RunId { get; init; }

    public object? Details { get; init; }
}

public class UiSessionRequest
{
    public string? WindowSessionId { get; set; }
}

public sealed class InitializeRequest : UiSessionRequest
{
}

public sealed class ListRequest : UiSessionRequest
{
    public string? Query { get; set; }

    /// <summary><c>all</c> | <c>builtin</c> | <c>category</c>.</summary>
    public string? Scope { get; set; }

    public string? CategoryId { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }
}

public sealed class NodeIdRequest : UiSessionRequest
{
    public string? NodeId { get; set; }
}

public sealed class ValidateDraftRequest : UiSessionRequest
{
    public NodeWrite? Detail { get; set; }
}

public sealed class CreateNodeRequest : UiSessionRequest
{
    public NodeWrite? Detail { get; set; }

    public string? CategoryId { get; set; }

}

public sealed class UpdateNodeRequest : UiSessionRequest
{
    public string? NodeId { get; set; }

    public NodeWrite? Detail { get; set; }

}

public sealed class CopyNodeRequest : UiSessionRequest
{
    public string? NodeId { get; set; }

    public string? CategoryId { get; set; }

}

public sealed class CreateCategoryRequest : UiSessionRequest
{
    public string? Name { get; set; }

    public string? Description { get; set; }
}

public sealed class RenameCategoryRequest : UiSessionRequest
{
    public string? CategoryId { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }
}

public sealed class CategoryIdRequest : UiSessionRequest
{
    public string? CategoryId { get; set; }
}

public sealed class MoveNodesRequest : UiSessionRequest
{
    public List<string>? NodeIds { get; set; }

    public string? TargetCategoryId { get; set; }
}

public sealed class DeleteNodesRequest : UiSessionRequest
{
    public List<string>? NodeIds { get; set; }

}

public sealed class RecordingIdRequest : UiSessionRequest
{
    public string? RecordingId { get; set; }
}

public sealed class CancelRunRequest : UiSessionRequest
{
    public string? RunId { get; set; }
}

public sealed class ImportPreviewIdRequest : UiSessionRequest
{
    public string? PreviewId { get; set; }
}

public sealed class CommitImportRequest : UiSessionRequest
{
    public string? PreviewId { get; set; }

}

public sealed class ExportNodesRequest : UiSessionRequest
{
    public List<string>? NodeIds { get; set; }
}

public sealed class InitializeDataDto
{
    public required string DisplayName { get; init; }

    public required UiLimitsDto Limits { get; init; }

    public required IReadOnlyList<KeyCatalogEntryDto> KeyCatalog { get; init; }

    public required IReadOnlyList<CategoryListItemDto> Categories { get; init; }

    public required NodePageDto Nodes { get; init; }

    public required RunPageDto Runs { get; init; }


    /// <summary>Optional node to select after bootstrap (search-result deep link).</summary>
    public string? FocusNodeId { get; init; }
}

public sealed class FocusNodeRequestedDto
{
    public required string NodeId { get; init; }
}

public sealed class CategoryListItemDto
{
    public required string CategoryId { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public int NodeCount { get; init; }

    public bool CanRename { get; init; } = true;

    public bool CanDelete { get; init; } = true;
}

public sealed class CategoryListDto
{
    public IReadOnlyList<CategoryListItemDto> Items { get; init; } = [];

    public long SequenceNumber { get; init; }
}

public sealed class UiLimitsDto
{
    public int MaxEvents { get; init; } = KeySenderConst.EventCountMax;

    public int MaxPageSize { get; init; } = 100;

    public int DefaultPageSize { get; init; } = 20;

    public int MaxNameLength { get; init; } = KeySenderConst.NameMaxLength;

    public int MaxDescriptionLength { get; init; } = KeySenderConst.DescriptionMaxLength;

    public int MaxQueuedRuns { get; init; } = 64;

    public int MaxTransferBytes { get; init; } = KeySenderConst.TransferMaxBytes;

    public int MaxTransferNodes { get; init; } = KeySenderConst.TransferMaxNodes;

    public int RecordingEventCap { get; init; } = KeySenderConst.RecordingEventCap;
}

public sealed class KeyCatalogEntryDto
{
    public required string Key { get; init; }

    public required string Display { get; init; }

    public required string Group { get; init; }
}

public sealed class NodeListItemDto
{
    public required string NodeId { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public IReadOnlyList<string> AliasNames { get; init; } = [];

    public string HotkeyDisplay { get; init; } = "";

    public bool IsPinned { get; init; }

    public string DragText { get; init; } = "";

    public bool IsBuiltIn { get; init; }

    public bool IsReadOnly { get; init; }

    public bool CanEdit { get; init; }

    public bool CanDelete { get; init; }

    public string? CategoryId { get; init; }

    public string? CategoryName { get; init; }
}

public sealed class NodePageDto
{
    public IReadOnlyList<NodeListItemDto> Items { get; init; } = [];

    public int Total { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }

    public long SequenceNumber { get; init; }
}

public sealed class ValidationResultDto
{
    public bool Valid { get; init; }

    public IReadOnlyList<ValidationIssue> Errors { get; init; } = [];

    public IReadOnlyList<ValidationIssue> Warnings { get; init; } = [];

    public string? WarningFingerprint { get; init; }
}

public sealed class DeleteNodesResultDto
{
    public IReadOnlyList<string> DeletedIds { get; init; } = [];
}

public sealed class RecordingSnapshotDto
{
    public required string RecordingId { get; init; }

    public required string WindowSessionId { get; init; }

    public required string State { get; init; }

    public int EventCount { get; init; }

    public long ElapsedMs { get; init; }

    public long SequenceNumber { get; init; }

    public IReadOnlyList<string> Notices { get; init; } = [];

    public string? Error { get; init; }
}

public sealed class RecordingFragmentDto
{
    public required string RecordingId { get; init; }

    public IReadOnlyList<KeyboardEvent> Events { get; init; } = [];

    public IReadOnlyList<string> Notices { get; init; } = [];
}

public sealed class RunSnapshotDto
{
    public required string RunId { get; init; }

    public required string NodeId { get; init; }

    public string? NodeName { get; init; }

    public required string State { get; init; }

    public int? EventIndex { get; init; }

    public int? EventCount { get; init; }

    public int? QueuePosition { get; init; }

    public DateTimeOffset SubmittedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public bool IsCancellationRequested { get; init; }

    public string? Error { get; init; }
}

public sealed class RunPageDto
{
    public long SequenceNumber { get; init; }

    public IReadOnlyList<RunSnapshotDto> Active { get; init; } = [];

    public IReadOnlyList<RunSnapshotDto> Recent { get; init; } = [];
}

public sealed class CancelRunResultDto
{
    public bool CancellationRequested { get; init; }
}

public sealed class ImportPreviewDto
{
    public required string PreviewId { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public int ItemCount { get; init; }

    public bool CanImport { get; init; }

    public string? WarningFingerprint { get; init; }

    public IReadOnlyList<ImportPreviewItem> Items { get; init; } = [];
}

public sealed class CommitImportResultDto
{
    public IReadOnlyList<NodeListItemDto> CreatedItems { get; init; } = [];
}

public sealed class ReleaseImportPreviewResultDto
{
    public bool Released { get; init; }
}

public sealed class ExportNodesResultDto
{
    public int ExportedCount { get; init; }

    public string? FileName { get; init; }
}

public sealed class NodesChangedDto
{
    public required string Kind { get; init; }

    public IReadOnlyList<string> NodeIds { get; init; } = [];

    public long SequenceNumber { get; init; }
}

public sealed class CategoriesChangedDto
{
    public required string Kind { get; init; }

    public IReadOnlyList<string> CategoryIds { get; init; } = [];

    public long SequenceNumber { get; init; }
}
