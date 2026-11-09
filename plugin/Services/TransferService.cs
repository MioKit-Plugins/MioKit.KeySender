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
            ReleasePreviewUnlocked();
            return TransferOperationResult.Fail(parsed.Code ?? ValidationCodes.InvalidFormat, parsed.Message);
        }

        var items = new List<ImportPreviewItem>(parsed.Nodes.Count);
        for (var i = 0; i < parsed.Nodes.Count; i++)
        {
            var write = parsed.Nodes[i];
            var analysis = parsed.Analyses[i];
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
                Nodes = parsed.Nodes.ToList(),
                Analyses = parsed.Analyses.ToList(),
                Consumed = false,
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
        string? acknowledgedWarningFingerprint,
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

            if (entry.Consumed)
            {
                return TransferOperationResult.Fail(
                    ValidationCodes.ImportPreviewExpired,
                    "Import preview was already consumed.");
            }

            if (!entry.Preview.CanImport)
            {
                return TransferOperationResult.Fail(
                    ValidationCodes.ImportInvalid,
                    "Preview has hard errors and cannot be imported.");
            }

            if (entry.Preview.WarningFingerprint is not null
                && !string.Equals(
                    acknowledgedWarningFingerprint,
                    entry.Preview.WarningFingerprint,
                    StringComparison.Ordinal))
            {
                return TransferOperationResult.Fail(
                    ValidationCodes.SequenceConfirmationRequired,
                    "Package warning fingerprint must be acknowledged before commit.");
            }
        }

        if (revalidateBytes is not null)
        {
            if (revalidateBytes.Length > KeySenderConst.TransferMaxBytes)
            {
                return TransferOperationResult.Fail(ValidationCodes.FileTooLarge, "Revalidate payload too large.");
            }

            var hash = SHA256.HashData(revalidateBytes);
            if (!hash.AsSpan().SequenceEqual(entry.Preview.ContentSha256))
            {
                return TransferOperationResult.Fail(
                    ValidationCodes.ImportChanged,
                    "Source package content changed since preview.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(entry.Preview.SourcePath))
        {
            try
            {
                await using var fs = new FileStream(
                    entry.Preview.SourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    options: FileOptions.Asynchronous);

                if (fs.Length > KeySenderConst.TransferMaxBytes)
                {
                    return TransferOperationResult.Fail(ValidationCodes.FileTooLarge);
                }

                using var ms = new MemoryStream((int)fs.Length);
                await fs.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
                var hash = SHA256.HashData(ms.ToArray());
                if (!hash.AsSpan().SequenceEqual(entry.Preview.ContentSha256))
                {
                    return TransferOperationResult.Fail(
                        ValidationCodes.ImportChanged,
                        "Source file changed since preview.");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return TransferOperationResult.Fail(ValidationCodes.FileAccessFailed, ex.Message);
            }
        }

        var createdIds = new List<string>(entry.Nodes.Count);
        var createdItems = new List<NodeDetail>(entry.Nodes.Count);

        for (var i = 0; i < entry.Nodes.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var write = entry.Nodes[i];
            var analysis = entry.Analyses[i];
            // Per-node ack after package-level confirmation (design 05 §5).
            var nodeAck = analysis.HasWarnings ? analysis.WarningFingerprint : null;

            var result = await _configuration
                .CreateAsync(write, nodeAck, cancellationToken)
                .ConfigureAwait(false);

            if (!result.Ok)
            {
                // No partial import: if nothing persisted, keep preview; if some did, mark consumed.
                if (createdIds.Count == 0 && result.Persisted != true)
                {
                    return TransferOperationResult.Fail(
                        result.Code ?? ValidationCodes.PersistenceFailed,
                        result.Message);
                }

                lock (_sync)
                {
                    if (_preview?.Preview.PreviewId == previewId)
                    {
                        _preview.Consumed = true;
                    }
                }

                if (result.Persisted && result.CreatedIds is { Count: > 0 })
                {
                    createdIds.AddRange(result.CreatedIds);
                }
                else if (!string.IsNullOrEmpty(result.NodeId))
                {
                    createdIds.Add(result.NodeId);
                }

                return TransferOperationResult.Fail(
                    result.Code ?? ValidationCodes.PersistenceFailed,
                    result.Message,
                    persisted: createdIds.Count > 0,
                    createdIds: createdIds);
            }

            if (!string.IsNullOrEmpty(result.NodeId))
            {
                createdIds.Add(result.NodeId);
            }

            if (result.Detail is not null)
            {
                createdItems.Add(result.Detail);
            }
        }

        lock (_sync)
        {
            if (_preview?.Preview.PreviewId == previewId)
            {
                _preview.Consumed = true;
                _preview = null;
            }
        }

        return TransferOperationResult.SuccessCommit(createdIds, createdItems);
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

        if (nodeIds.Count == 0)
        {
            return TransferOperationResult.Fail(ValidationCodes.InvalidFormat, "No nodes selected for export.");
        }

        var writes = new List<NodeWrite>(nodeIds.Count);
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
        }

        try
        {
            TransferPackageParser.WritePackage(output, writes);
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
        TransferPackageParser.WritePackage(output, nodes);
        return TransferOperationResult.SuccessExport(nodes.Count);
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

        if (entry.Preview.ExpiresAt <= DateTimeOffset.UtcNow || entry.Consumed)
        {
            _preview = null;
            entry = null;
            return false;
        }

        return true;
    }

    private void ReleasePreviewUnlocked()
    {
        _preview = null;
    }

    private sealed class PreviewEntry
    {
        public required ImportPreview Preview { get; init; }
        public required List<NodeWrite> Nodes { get; init; }
        public required List<SequenceAnalysisResult> Analyses { get; init; }
        public bool Consumed { get; set; }
    }
}
