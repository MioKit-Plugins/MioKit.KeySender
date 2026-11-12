using System.Security.Cryptography;
using KeySender.Models;
using KeySender.Validation;

namespace KeySender.Services;

/// <summary>
/// Import preview/commit and export orchestration (design 05). File dialogs stay in UI.
/// </summary>
public sealed class TransferService
{
    private readonly IConfigurationService _configuration;
    private readonly object _sync = new();
    private PreviewEntry? _preview;

    public TransferService(IConfigurationService configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Parse bytes into a single-window preview (TTL 5 minutes). Replaces any prior preview.
    /// </summary>
    public TransferOperationResult PreviewFromBytes(
        string windowSessionId,
        ReadOnlySpan<byte> utf8Json,
        string? sourcePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowSessionId);

        var parsed = TransferPackageParser.Parse(utf8Json);
        if (!parsed.Ok)
        {
            ReleasePreview();
            return TransferOperationResult.Fail(parsed.Code ?? ValidationCodes.InvalidFormat, parsed.Message);
        }

        var items = new List<ImportPreviewItem>(parsed.Nodes.Count);
        for (var i = 0; i < parsed.Nodes.Count; i++)
        {
            var write = parsed.Nodes[i];
            var analysis = parsed.Analyses[i];
            var categoryIndex = parsed.CategoryIndices[i];
            long total = write.BeforeDelayMs + write.AfterDelayMs;
            foreach (var ev in write.Sequence.Events)
            {
                total += ev.DelayBeforeMs;
            }

            items.Add(new ImportPreviewItem
            {
                ItemIndex = i,
                Name = write.Name,
                Description = write.Description,
                CategoryName = categoryIndex is int index ? parsed.Categories[index].Name : null,
                EventCount = write.Sequence.Events.Count,
                TotalDelayMs = total,
                Errors = analysis.HardErrors,
                Warnings = analysis.Warnings,
            });
        }

        var preview = new ImportPreview
        {
            PreviewId = Guid.NewGuid().ToString("D"),
            WindowSessionId = windowSessionId,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(KeySenderConst.TransferPreviewTtlSeconds),
            ItemCount = items.Count,
            CanImport = parsed.CanImport,
            WarningFingerprint = parsed.WarningFingerprint,
            Items = items,
            SourcePath = sourcePath,
            ContentSha256 = parsed.ContentSha256 ?? [],
        };

        lock (_sync)
        {
            _preview = new PreviewEntry
            {
                Preview = preview,
                Nodes = parsed.Nodes,
                Categories = parsed.Categories,
                CategoryIndices = parsed.CategoryIndices,
            };
        }

        return TransferOperationResult.SuccessPreview(preview);
    }

    public TransferOperationResult PreviewFromString(string windowSessionId, string json, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        return PreviewFromBytes(windowSessionId, System.Text.Encoding.UTF8.GetBytes(json), sourcePath);
    }

    public TransferOperationResult GetPreview(string previewId)
    {
        lock (_sync)
        {
            if (!TryGetLivePreview(previewId, out var entry) || entry is null)
            {
                return TransferOperationResult.Fail(
                    ValidationCodes.ImportPreviewExpired,
                    "Import preview is missing or expired.");
            }

            return TransferOperationResult.SuccessPreview(entry.Preview);
        }
    }

    public void ReleasePreview(string? previewId = null)
    {
        lock (_sync)
        {
            if (previewId is null
                || (_preview is not null
                    && string.Equals(_preview.Preview.PreviewId, previewId, StringComparison.Ordinal)))
            {
                _preview = null;
            }
        }
    }

    /// <summary>
    /// Commit a live preview. When <paramref name="revalidateBytes"/> is provided (or source path
    /// bytes match), content hash is re-checked. Creates USER nodes only via ConfigurationService.
    /// </summary>
    public async Task<TransferOperationResult> CommitAsync(
        string windowSessionId,
        string previewId,
        byte[]? revalidateBytes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(previewId);

        PreviewEntry entry;
        lock (_sync)
        {
            if (!TryGetLivePreview(previewId, out entry!)
                || !string.Equals(entry.Preview.WindowSessionId, windowSessionId, StringComparison.Ordinal))
            {
                return TransferOperationResult.Fail(
                    ValidationCodes.ImportPreviewExpired,
                    "Import preview is missing, expired, or bound to another window.");
            }

            if (entry.Committing)
            {
                return TransferOperationResult.Fail(
                    ValidationCodes.ImportInProgress,
                    "此预览正在导入。");
            }

            if (!entry.Preview.CanImport)
            {
                return TransferOperationResult.Fail(
                    ValidationCodes.ImportInvalid,
                    "Preview has hard errors and cannot be imported.");
            }

            entry.Committing = true;
        }

        var createdIds = new List<string>(entry.Nodes.Count);
        var createdCategoryIds = new List<string>(entry.Categories.Count);
        var createdItems = new List<NodeDetail>(entry.Nodes.Count);
        var consumePreview = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var invalidSource = await RevalidateSourceAsync(entry.Preview, revalidateBytes, cancellationToken)
                .ConfigureAwait(false);
            if (invalidSource is not null)
            {
                return invalidSource;
            }

            lock (_sync)
            {
                if (!TryGetLivePreview(previewId, out var current) || !ReferenceEquals(current, entry))
                {
                    return TransferOperationResult.Fail(
                        ValidationCodes.ImportPreviewExpired,
                        "Import preview was released or expired before writing.");
                }
            }

            var uniqueCategoryNames = entry.Categories
                .GroupBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() == 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var localCategoryIds = new string?[entry.Categories.Count];

            for (var i = 0; i < entry.Nodes.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var write = entry.Nodes[i];
                string? categoryId = null;
                if (entry.CategoryIndices[i] is int categoryIndex)
                {
                    categoryId = localCategoryIds[categoryIndex];
                    if (categoryId is null)
                    {
                        var category = entry.Categories[categoryIndex];
                        var resolved = await _configuration.ResolveImportCategoryAsync(
                                category.Name,
                                category.Description,
                                uniqueCategoryNames.Contains(category.Name),
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (resolved.Persisted)
                        {
                            consumePreview = true;
                            if (resolved.NodeId is not null)
                            {
                                createdCategoryIds.Add(resolved.NodeId);
                            }
                        }

                        if (!resolved.Ok || resolved.NodeId is null)
                        {
                            return FailedCommit(resolved);
                        }

                        categoryId = localCategoryIds[categoryIndex] = resolved.NodeId;
                    }
                }

                var result = categoryId is null
                    ? await _configuration.CreateAsync(write, cancellationToken).ConfigureAwait(false)
                    : await _configuration.CreateInCategoryAsync(write, categoryId, cancellationToken)
                        .ConfigureAwait(false);
                if (!result.Ok)
                {
                    if (result.Persisted)
                    {
                        consumePreview = true;
                        if (result.CreatedIds is { Count: > 0 })
                        {
                            createdIds.AddRange(result.CreatedIds);
                        }
                        else if (!string.IsNullOrEmpty(result.NodeId))
                        {
                            createdIds.Add(result.NodeId);
                        }
                    }

                    return FailedCommit(result);
                }

                consumePreview = true;
                if (!string.IsNullOrEmpty(result.NodeId))
                {
                    createdIds.Add(result.NodeId);
                }

                if (result.Detail is not null)
                {
                    createdItems.Add(result.Detail);
                }
            }

            consumePreview = true;
            return TransferOperationResult.SuccessCommit(createdIds, createdItems, createdCategoryIds);
        }
        catch (OperationCanceledException) when (consumePreview)
        {
            return TransferOperationResult.Fail(
                ValidationCodes.Cancelled,
                $"导入已取消，已保存 {createdIds.Count} 个节点和 {createdCategoryIds.Count} 个分类。",
                persisted: true,
                createdIds: createdIds,
                createdCategoryIds: createdCategoryIds);
        }
        finally
        {
            lock (_sync)
            {
                entry.Committing = false;
                if (consumePreview && ReferenceEquals(_preview, entry))
                {
                    _preview = null;
                }
            }
        }

        TransferOperationResult FailedCommit(ConfigurationResult result) => TransferOperationResult.Fail(
            result.Code ?? ValidationCodes.PersistenceFailed,
            result.Message,
            persisted: consumePreview,
            createdIds: consumePreview ? createdIds : null,
            createdCategoryIds: consumePreview ? createdCategoryIds : null);
    }

    private static async Task<TransferOperationResult?> RevalidateSourceAsync(
        ImportPreview preview,
        byte[]? revalidateBytes,
        CancellationToken cancellationToken)
    {
        byte[] hash;
        if (revalidateBytes is not null)
        {
            if (revalidateBytes.Length > KeySenderConst.TransferMaxBytes)
            {
                return TransferOperationResult.Fail(ValidationCodes.FileTooLarge, "Revalidate payload too large.");
            }

            hash = SHA256.HashData(revalidateBytes);
        }
        else if (!string.IsNullOrWhiteSpace(preview.SourcePath))
        {
            try
            {
                await using var stream = new FileStream(
                    preview.SourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    options: FileOptions.Asynchronous);
                if (stream.Length > KeySenderConst.TransferMaxBytes)
                {
                    return TransferOperationResult.Fail(ValidationCodes.FileTooLarge);
                }

                hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return TransferOperationResult.Fail(ValidationCodes.FileAccessFailed, ex.Message);
            }
        }
        else
        {
            return null;
        }

        return hash.AsSpan().SequenceEqual(preview.ContentSha256)
            ? null
            : TransferOperationResult.Fail(ValidationCodes.ImportChanged, "Source content changed since preview.");
    }

    /// <summary>
    /// Export saved node configs (builtins strip identity). Does not include drafts/recordings.
    /// </summary>
    public async Task<TransferOperationResult> ExportAsync(
        IReadOnlyList<string> nodeIds,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);
        ArgumentNullException.ThrowIfNull(output);

        if (ValidateExportCount(nodeIds.Count) is { } countError)
        {
            return countError;
        }

        var writes = new List<NodeWrite>(nodeIds.Count);
        var categories = new List<TransferCategory>();
        var categoryIndices = new List<int?>(nodeIds.Count);
        var indicesById = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in nodeIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var detail = await _configuration.GetDetailAsync(id, cancellationToken).ConfigureAwait(false);
            if (!detail.Ok || detail.Detail is null)
            {
                return TransferOperationResult.Fail(
                    detail.Code ?? ValidationCodes.NodeNotFound,
                    detail.Message ?? $"Node not found: {id}");
            }

            // Builtins export as plain sequences without builtin id/marker (design 05 §3).
            writes.Add(detail.Detail.ToWrite());
            int? categoryIndex = null;
            if (detail.Category is { } category)
            {
                if (!indicesById.TryGetValue(category.Id, out var index))
                {
                    index = categories.Count;
                    indicesById.Add(category.Id, index);
                    categories.Add(new TransferCategory(category.Name, category.Description));
                }

                categoryIndex = index;
            }

            categoryIndices.Add(categoryIndex);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return WriteExportPackage(output, writes, categories, categoryIndices);
    }

    internal static TransferOperationResult? ValidateExportCount(int count)
    {
        if (count == 0)
        {
            return TransferOperationResult.Fail(ValidationCodes.InvalidFormat, "未选择导出节点。");
        }

        if (count > KeySenderConst.TransferMaxNodes)
        {
            return TransferOperationResult.Fail(
                ValidationCodes.ExportLimitExceeded,
                $"每个文件最多导出 {KeySenderConst.TransferMaxNodes} 个节点，请分批导出。");
        }

        return null;
    }

    private static TransferOperationResult WriteExportPackage(
        Stream output,
        IReadOnlyList<NodeWrite> writes,
        IReadOnlyList<TransferCategory>? categories = null,
        IReadOnlyList<int?>? categoryIndices = null)
    {
        try
        {
            if (!TransferPackageParser.TryWritePackage(output, writes, categories, categoryIndices))
            {
                return TransferOperationResult.Fail(
                    ValidationCodes.FileTooLarge,
                    $"导出文件不能超过 {KeySenderConst.TransferMaxBytes / (1024 * 1024)} MiB，请分批导出。");
            }
        }
        catch (ArgumentException ex) when (ex.ParamName is "categories" or "categoryIndices")
        {
            return TransferOperationResult.Fail(ValidationCodes.InvalidCategory, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return TransferOperationResult.Fail(ValidationCodes.FileAccessFailed, ex.Message);
        }

        return TransferOperationResult.SuccessExport(writes.Count);
    }

    /// <summary>
    /// Export already-loaded writes (unit-test / pure path).
    /// </summary>
    public static TransferOperationResult ExportWrites(IReadOnlyList<NodeWrite> nodes, Stream output)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(output);
        return ValidateExportCount(nodes.Count) ?? WriteExportPackage(output, nodes);
    }

    private bool TryGetLivePreview(string previewId, out PreviewEntry? entry)
    {
        entry = _preview;
        if (entry is null
            || !string.Equals(entry.Preview.PreviewId, previewId, StringComparison.Ordinal))
        {
            entry = null;
            return false;
        }

        if (entry.Preview.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _preview = null;
            entry = null;
            return false;
        }

        return true;
    }

    private sealed class PreviewEntry
    {
        public required ImportPreview Preview { get; init; }
        public required IReadOnlyList<NodeWrite> Nodes { get; init; }
        public required IReadOnlyList<TransferCategory> Categories { get; init; }
        public required IReadOnlyList<int?> CategoryIndices { get; init; }
        public bool Committing { get; set; }
    }
}
