using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using KeySender.BuiltIn;
using KeySender.Models;
using KeySender.Models.Ui;
using KeySender.Validation;
using KeySender.Views;
using MioKit.Webview2.Core;

namespace KeySender.Services;

/// <summary>
/// WebView2 bridge facade for the Vue manager (docs/04).
/// Session lifetime is owned by <see cref="PluginWebView"/>; the session id is adopted from
/// the first <see cref="InitializeAsync"/> call (Vue generates the UUID).
/// </summary>
[JsService("KeySenderUiService")]
public sealed class KeySenderUiService : IDisposable
{
    private readonly ConfigurationService _configuration;
    private readonly RecordingService _recording;
    private readonly IKeySenderExecutionService _execution;
    private readonly TransferService _transfer;
    private readonly KeySenderPlugin _plugin;

    private readonly object _sessionGate = new();
    private readonly object _nodesSeqGate = new();
    private readonly object _runsSeqGate = new();
    private readonly object _categoriesSeqGate = new();

    private bool _windowAlive;
    private bool _sessionBound;
    private string? _activeSessionId;
    private long _nodesSequence = 1;
    private long _runsSequence = 1;
    private long _categoriesSequence = 1;
    private string? _lastRunsFingerprint;
    private long _lastHeldSequence = -1;
    private CancellationTokenSource? _pollCts;
    private bool _subscribedRecording;
    private bool _disposed;

    public KeySenderUiService(
        ConfigurationService configuration,
        RecordingService recording,
        IKeySenderExecutionService execution,
        TransferService transfer,
        KeySenderPlugin plugin)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _recording = recording ?? throw new ArgumentNullException(nameof(recording));
        _execution = execution ?? throw new ArgumentNullException(nameof(execution));
        _transfer = transfer ?? throw new ArgumentNullException(nameof(transfer));
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
    }

    public event EventHandler<RecordingSnapshotDto>? RecordingChanged;

    public event EventHandler<RunPageDto>? RunsChanged;

    public event EventHandler<HeldKeysSnapshotDto>? HeldKeysChanged;

    public event EventHandler<NodesChangedDto>? NodesChanged;

    public event EventHandler<CategoriesChangedDto>? CategoriesChanged;

    /// <summary>Host-only: window opened; session id binds on InitializeAsync.</summary>
    internal void OnWindowOpened()
    {
        lock (_sessionGate)
        {
            _windowAlive = true;
            _sessionBound = false;
            _activeSessionId = null;
        }

        EnsureRecordingSubscription();
        StartPolling();
    }

    /// <summary>
    /// Host-only: optional pre-assigned session id (also adopted from Initialize when unset).
    /// </summary>
    internal void OpenSession(string windowSessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowSessionId);
        lock (_sessionGate)
        {
            _windowAlive = true;
            _activeSessionId = windowSessionId.Trim();
            _sessionBound = true;
        }

        EnsureRecordingSubscription();
        StartPolling();
    }

    /// <summary>Host-only: window closed — drop session, recording, and import preview. Does not ReleaseAll.</summary>
    internal void OnWindowClosed()
    {
        string? sessionId;
        lock (_sessionGate)
        {
            sessionId = _activeSessionId;
            _windowAlive = false;
            _sessionBound = false;
            _activeSessionId = null;
        }

        StopPolling();

        if (!string.IsNullOrEmpty(sessionId))
        {
            try
            {
                var snap = _recording.GetSnapshot();
                if (snap is not null
                    && string.Equals(snap.WindowSessionId, sessionId, StringComparison.Ordinal)
                    && snap.State is not RecordingState.Cancelled
                        and not RecordingState.Failed
                        and not RecordingState.Idle)
                {
                    _recording.Cancel(snap.RecordingId);
                }
            }
            catch
            {
                // best-effort
            }

            try
            {
                _transfer.ReleasePreview();
            }
            catch
            {
                // best-effort
            }
        }
    }

    public Task<ApiResult<InitializeDataDto>> InitializeAsync(InitializeRequest request)
    {
        if (TrySessionError(request, allowBind: true, out var error))
        {
            return Task.FromResult(Fail<InitializeDataDto>(error!));
        }

        var nodes = BuildNodePage(
            query: null,
            scope: KeySenderConst.ListScopeAll,
            categoryId: null,
            page: 1,
            pageSize: 20);
        var data = new InitializeDataDto
        {
            DisplayName = _plugin.Name ?? "模拟按键",
            Limits = new UiLimitsDto(),
            KeyCatalog = BuildKeyCatalog(),
            Categories = BuildCategoryList().Items,
            Nodes = nodes,
            Runs = BuildRunPage(),
            HeldKeys = MapHeldKeys(_execution.GetHeldKeys()),
        };
        return Task.FromResult(ApiResult<InitializeDataDto>.Success(data));
    }

    public Task<ApiResult<NodePageDto>> GetListAsync(ListRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Task.FromResult(Fail<NodePageDto>(error!));
        }

        return Task.FromResult(ApiResult<NodePageDto>.Success(
            BuildNodePage(
                request.Query,
                request.Scope,
                request.CategoryId,
                request.Page,
                request.PageSize)));
    }

    public Task<ApiResult<CategoryListDto>> GetCategoriesAsync(UiSessionRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Task.FromResult(Fail<CategoryListDto>(error!));
        }

        return Task.FromResult(ApiResult<CategoryListDto>.Success(BuildCategoryList()));
    }

    public async Task<ApiResult<CategoryListItemDto>> CreateCategoryAsync(CreateCategoryRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Fail<CategoryListItemDto>(error!);
        }

        var result = await _configuration
            .CreateCategoryAsync(request.Name ?? string.Empty, request.Description)
            .ConfigureAwait(false);
        if (!result.Ok || result.NodeId is null)
        {
            return MapConfigFail<CategoryListItemDto>(result);
        }

        RaiseCategoriesChanged("created", [result.NodeId]);
        var item = BuildCategoryList().Items
            .FirstOrDefault(c => string.Equals(c.CategoryId, result.NodeId, StringComparison.Ordinal));
        return item is null
            ? ApiResult<CategoryListItemDto>.Fail(ValidationCodes.CategoryNotFound, "分类已创建但未能读取")
            : ApiResult<CategoryListItemDto>.Success(item);
    }

    public async Task<ApiResult<CategoryListItemDto>> RenameCategoryAsync(RenameCategoryRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Fail<CategoryListItemDto>(error!);
        }

        if (string.IsNullOrWhiteSpace(request.CategoryId))
        {
            return ApiResult<CategoryListItemDto>.Fail(ValidationCodes.CategoryNotFound, "缺少 categoryId");
        }

        var result = await _configuration
            .RenameCategoryAsync(request.CategoryId.Trim(), request.Name ?? string.Empty, request.Description)
            .ConfigureAwait(false);
        if (!result.Ok || result.NodeId is null)
        {
            return MapConfigFail<CategoryListItemDto>(result);
        }

        RaiseCategoriesChanged("updated", [result.NodeId]);
        var item = BuildCategoryList().Items
            .FirstOrDefault(c => string.Equals(c.CategoryId, result.NodeId, StringComparison.Ordinal));
        return item is null
            ? ApiResult<CategoryListItemDto>.Fail(ValidationCodes.CategoryNotFound, "分类已更新但未能读取")
            : ApiResult<CategoryListItemDto>.Success(item);
    }

    public async Task<ApiResult<DeleteNodesResultDto>> DeleteCategoryAsync(CategoryIdRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Fail<DeleteNodesResultDto>(error!);
        }

        if (string.IsNullOrWhiteSpace(request.CategoryId))
        {
            return ApiResult<DeleteNodesResultDto>.Fail(ValidationCodes.CategoryNotFound, "缺少 categoryId");
        }

        var categoryId = request.CategoryId.Trim();
        var group = _plugin.FindFirstDescendant<KeySenderSearchGroup>(
            g => g.Id == KeySenderConst.SearchGroupId);
        var category = group?.FindFirstDescendant<KeySenderCategory>(c =>
            string.Equals(c.Id, categoryId, StringComparison.OrdinalIgnoreCase));
        if (category is null)
        {
            return ApiResult<DeleteNodesResultDto>.Fail(
                ValidationCodes.CategoryNotFound,
                "分类不存在",
                nodeId: categoryId);
        }

        var childIds = category.GetDescendants<KeySenderNode>()
            .Where(static n => !BuiltInKeyCatalog.IsBuiltIn(n.Id))
            .Select(static n => n.Id)
            .ToList();

        if (childIds.Count > 0)
        {
            var active = _execution.GetActiveAndQueuedRuns();
            if (active.Any(r => childIds.Contains(r.NodeId, StringComparer.Ordinal)))
            {
                return ApiResult<DeleteNodesResultDto>.Fail(
                    ValidationCodes.NodeInUse,
                    "分类内存在未结束的运行任务，不能删除。");
            }

            var held = _execution.GetHeldKeys();
            if (held.Keys.Any(k => childIds.Contains(k.SourceNodeId, StringComparer.Ordinal)))
            {
                var releaseSnap = await _execution.ReleaseAllAsync().ConfigureAwait(false);
                RaiseHeldKeys(MapHeldKeys(releaseSnap));
                RaiseRuns(BuildRunPage());
                if (!releaseSnap.SenderAvailable && releaseSnap.Keys.Count > 0)
                {
                    return ApiResult<DeleteNodesResultDto>.Fail(
                        ValidationCodes.InputCleanupFailed,
                        "释放保持键失败，未删除分类。",
                        details: MapHeldKeys(releaseSnap));
                }
            }
        }

        var result = await _configuration.DeleteCategoryAsync(categoryId).ConfigureAwait(false);
        if (!result.Ok)
        {
            return MapConfigFail<DeleteNodesResultDto>(result);
        }

        var deleted = result.DeletedIds?.ToList() ?? [];
        if (deleted.Count > 0)
        {
            RaiseNodesChanged("deleted", deleted);
        }

        RaiseCategoriesChanged("deleted", [categoryId]);
        return ApiResult<DeleteNodesResultDto>.Success(new DeleteNodesResultDto { DeletedIds = deleted });
    }

    public async Task<ApiResult<bool>> MoveNodesAsync(MoveNodesRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Fail<bool>(error!);
        }

        if (string.IsNullOrWhiteSpace(request.TargetCategoryId))
        {
            return ApiResult<bool>.Fail(ValidationCodes.CategoryNotFound, "缺少 targetCategoryId");
        }

        var ids = (request.NodeIds ?? [])
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (ids.Count == 0)
        {
            return ApiResult<bool>.Success(true);
        }

        var result = await _configuration
            .MoveNodesAsync(ids, request.TargetCategoryId.Trim())
            .ConfigureAwait(false);
        if (!result.Ok)
        {
            return MapConfigFail<bool>(result);
        }

        RaiseNodesChanged("moved", ids);
        RaiseCategoriesChanged("updated", [request.TargetCategoryId.Trim()]);
        return ApiResult<bool>.Success(true);
    }

    public async Task<ApiResult<NodeDetail>> GetDetailAsync(NodeIdRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Fail<NodeDetail>(error!);
        }

        if (string.IsNullOrWhiteSpace(request.NodeId))
        {
            return ApiResult<NodeDetail>.Fail(ValidationCodes.NodeNotFound, "缺少 nodeId");
        }

        var result = await _configuration.GetDetailAsync(request.NodeId.Trim()).ConfigureAwait(false);
        if (!result.Ok || result.Detail is null)
        {
            return MapConfigFail<NodeDetail>(result);
        }

        return ApiResult<NodeDetail>.Success(result.Detail);
    }

    public Task<ApiResult<ValidationResultDto>> ValidateDraftAsync(ValidateDraftRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Task.FromResult(Fail<ValidationResultDto>(error!));
        }

        if (request.Detail is null)
        {
            return Task.FromResult(ApiResult<ValidationResultDto>.Fail(
                ValidationCodes.ValidationFailed, "缺少 detail"));
        }

        var analysis = SequenceAnalyzer.AnalyzeSequence(request.Detail);
        return Task.FromResult(ApiResult<ValidationResultDto>.Success(MapValidation(analysis)));
    }

    public async Task<ApiResult<NodeListItemDto>> CreateNodeAsync(CreateNodeRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Fail<NodeListItemDto>(error!);
        }

        if (request.Detail is null)
        {
            return ApiResult<NodeListItemDto>.Fail(ValidationCodes.ValidationFailed, "缺少 detail");
        }

        var result = await _configuration
            .CreateInCategoryAsync(
                request.Detail,
                request.AcknowledgedWarningFingerprint,
                request.CategoryId)
            .ConfigureAwait(false);
        if (!result.Ok || result.NodeId is null)
        {
            return MapConfigFail<NodeListItemDto>(result);
        }

        var item = FindListItem(result.NodeId) ?? ListItemFromDetail(result.Detail!);
        RaiseNodesChanged("created", [result.NodeId]);
        RaiseCategoriesChanged("updated", item.CategoryId is null ? [] : [item.CategoryId]);
        return ApiResult<NodeListItemDto>.Success(item);
    }

    public async Task<ApiResult<NodeListItemDto>> UpdateNodeAsync(UpdateNodeRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Fail<NodeListItemDto>(error!);
        }

        if (string.IsNullOrWhiteSpace(request.NodeId) || request.Detail is null)
        {
            return ApiResult<NodeListItemDto>.Fail(ValidationCodes.ValidationFailed, "缺少 nodeId 或 detail");
        }

        var result = await _configuration
            .UpdateAsync(request.NodeId.Trim(), request.Detail, request.AcknowledgedWarningFingerprint)
            .ConfigureAwait(false);
        if (!result.Ok || result.NodeId is null)
        {
            return MapConfigFail<NodeListItemDto>(result);
        }

        var item = FindListItem(result.NodeId) ?? ListItemFromDetail(result.Detail!);
        RaiseNodesChanged("updated", [result.NodeId]);
        return ApiResult<NodeListItemDto>.Success(item);
    }

    public async Task<ApiResult<NodeListItemDto>> CopyNodeAsync(CopyNodeRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Fail<NodeListItemDto>(error!);
        }

        if (string.IsNullOrWhiteSpace(request.NodeId))
        {
            return ApiResult<NodeListItemDto>.Fail(ValidationCodes.NodeNotFound, "缺少 nodeId");
        }

        var result = await _configuration
            .CopyToCategoryAsync(
                request.NodeId.Trim(),
                request.AcknowledgedWarningFingerprint,
                request.CategoryId)
            .ConfigureAwait(false);
        if (!result.Ok || result.NodeId is null)
        {
            return MapConfigFail<NodeListItemDto>(result);
        }

        var item = FindListItem(result.NodeId) ?? ListItemFromDetail(result.Detail!);
        RaiseNodesChanged("created", [result.NodeId]);
        RaiseCategoriesChanged("updated", item.CategoryId is null ? [] : [item.CategoryId]);
        return ApiResult<NodeListItemDto>.Success(item);
    }

    public async Task<ApiResult<DeleteNodesResultDto>> DeleteNodesAsync(DeleteNodesRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Fail<DeleteNodesResultDto>(error!);
        }

        var ids = (request.NodeIds ?? [])
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (ids.Count == 0)
        {
            return ApiResult<DeleteNodesResultDto>.Success(new DeleteNodesResultDto { DeletedIds = [] });
        }

        var builtin = ConfigHelpers.RejectIfAnyBuiltIn(ids);
        if (builtin is not null)
        {
            return MapConfigFail<DeleteNodesResultDto>(builtin);
        }

        var active = _execution.GetActiveAndQueuedRuns();
        if (active.Any(r => ids.Contains(r.NodeId, StringComparer.Ordinal)))
        {
            return ApiResult<DeleteNodesResultDto>.Fail(
                ValidationCodes.NodeInUse,
                "存在未结束的运行任务，不能删除。");
        }

        var held = _execution.GetHeldKeys();
        var heldFromTargets = held.Keys
            .Where(k => ids.Contains(k.SourceNodeId, StringComparer.Ordinal))
            .Select(k => k.SourceNodeId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (heldFromTargets.Count > 0 && !request.ReleaseHeldKeys)
        {
            return ApiResult<DeleteNodesResultDto>.Fail(
                ValidationCodes.HeldKeysConfirmationRequired,
                "所选节点仍有保持键，需先确认释放",
                details: new { nodeIds = heldFromTargets });
        }

        if (request.ReleaseHeldKeys)
        {
            var releaseSnap = await _execution.ReleaseAllAsync().ConfigureAwait(false);
            RaiseHeldKeys(MapHeldKeys(releaseSnap));
            RaiseRuns(BuildRunPage());
            if (!releaseSnap.SenderAvailable && releaseSnap.Keys.Count > 0)
            {
                return ApiResult<DeleteNodesResultDto>.Fail(
                    ValidationCodes.InputCleanupFailed,
                    "释放保持键失败，未删除节点。",
                    details: MapHeldKeys(releaseSnap));
            }
        }

        var result = await _configuration.DeleteAsync(ids).ConfigureAwait(false);
        if (!result.Ok)
        {
            return MapConfigFail<DeleteNodesResultDto>(result);
        }

        var deleted = result.DeletedIds ?? ids;
        RaiseNodesChanged("deleted", deleted);
        return ApiResult<DeleteNodesResultDto>.Success(new DeleteNodesResultDto { DeletedIds = deleted });
    }

    public Task<ApiResult<RecordingSnapshotDto>> BeginRecordingAsync(UiSessionRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Task.FromResult(Fail<RecordingSnapshotDto>(error!));
        }

        var result = _recording.Start(_activeSessionId!);
        if (!result.Ok || result.Snapshot is null)
        {
            return Task.FromResult(MapRecordingFail(result));
        }

        var dto = MapRecording(result.Snapshot);
        RaiseRecording(dto);
        return Task.FromResult(ApiResult<RecordingSnapshotDto>.Success(dto));
    }

    public Task<ApiResult<RecordingSnapshotDto>> StopRecordingAsync(RecordingIdRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Task.FromResult(Fail<RecordingSnapshotDto>(error!));
        }

        if (string.IsNullOrWhiteSpace(request.RecordingId))
        {
            return Task.FromResult(ApiResult<RecordingSnapshotDto>.Fail(
                ValidationCodes.RecordingNotReady, "缺少 recordingId"));
        }

        var result = _recording.Stop(request.RecordingId.Trim());
        if (!result.Ok || result.Snapshot is null)
        {
            return Task.FromResult(MapRecordingFail(result));
        }

        var dto = MapRecording(result.Snapshot);
        RaiseRecording(dto);
        return Task.FromResult(ApiResult<RecordingSnapshotDto>.Success(dto));
    }

    public Task<ApiResult<RecordingSnapshotDto>> GetRecordingAsync(RecordingIdRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Task.FromResult(Fail<RecordingSnapshotDto>(error!));
        }

        var snap = _recording.GetSnapshot(request.RecordingId);
        if (snap is null)
        {
            return Task.FromResult(ApiResult<RecordingSnapshotDto>.Fail(
                ValidationCodes.RecordingNotReady, "录制会话不存在"));
        }

        return Task.FromResult(ApiResult<RecordingSnapshotDto>.Success(MapRecording(snap)));
    }

    public Task<ApiResult<RecordingFragmentDto>> GetRecordingFragmentAsync(RecordingIdRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Task.FromResult(Fail<RecordingFragmentDto>(error!));
        }

        if (string.IsNullOrWhiteSpace(request.RecordingId))
        {
            return Task.FromResult(ApiResult<RecordingFragmentDto>.Fail(
                ValidationCodes.RecordingNotReady, "缺少 recordingId"));
        }

        var result = _recording.GetFragment(request.RecordingId.Trim());
        if (!result.Ok || result.Fragment is null)
        {
            return Task.FromResult(ApiResult<RecordingFragmentDto>.Fail(
                result.Code ?? ValidationCodes.RecordingNotReady,
                result.Message));
        }

        return Task.FromResult(ApiResult<RecordingFragmentDto>.Success(MapFragment(result.Fragment)));
    }

    public Task<ApiResult<RecordingFragmentDto>> ApplyRecordingAsync(RecordingIdRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Task.FromResult(Fail<RecordingFragmentDto>(error!));
        }

        if (string.IsNullOrWhiteSpace(request.RecordingId))
        {
            return Task.FromResult(ApiResult<RecordingFragmentDto>.Fail(
                ValidationCodes.RecordingNotReady, "缺少 recordingId"));
        }

        var result = _recording.Apply(request.RecordingId.Trim());
        if (!result.Ok || result.Fragment is null)
        {
            return Task.FromResult(ApiResult<RecordingFragmentDto>.Fail(
                result.Code ?? ValidationCodes.RecordingNotReady,
                result.Message));
        }

        RaiseRecording(new RecordingSnapshotDto
        {
            RecordingId = request.RecordingId.Trim(),
            WindowSessionId = _activeSessionId ?? "",
            State = "idle",
            SequenceNumber = result.Snapshot?.SequenceNumber + 1 ?? 0,
        });

        return Task.FromResult(ApiResult<RecordingFragmentDto>.Success(MapFragment(result.Fragment)));
    }

    public Task<ApiResult<RecordingSnapshotDto>> CancelRecordingAsync(RecordingIdRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Task.FromResult(Fail<RecordingSnapshotDto>(error!));
        }

        var recordingId = request.RecordingId?.Trim() ?? "";
        var result = _recording.Cancel(recordingId);
        var dto = result.Snapshot is not null
            ? MapRecording(result.Snapshot)
            : new RecordingSnapshotDto
            {
                RecordingId = recordingId,
                WindowSessionId = _activeSessionId ?? "",
                State = "cancelled",
                SequenceNumber = 0,
            };
        RaiseRecording(dto);
        return Task.FromResult(ApiResult<RecordingSnapshotDto>.Success(dto));
    }

    public Task<ApiResult<RunPageDto>> GetRunsAsync(UiSessionRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Task.FromResult(Fail<RunPageDto>(error!));
        }

        return Task.FromResult(ApiResult<RunPageDto>.Success(BuildRunPage()));
    }

    public Task<ApiResult<CancelRunResultDto>> CancelRunAsync(CancelRunRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Task.FromResult(Fail<CancelRunResultDto>(error!));
        }

        if (!Guid.TryParse(request.RunId, out var runId))
        {
            return Task.FromResult(ApiResult<CancelRunResultDto>.Fail(
                ValidationCodes.NodeNotFound, "任务不存在", runId: request.RunId));
        }

        var requested = _execution.TryCancel(runId);
        RaiseRuns(BuildRunPage());
        return Task.FromResult(ApiResult<CancelRunResultDto>.Success(
            new CancelRunResultDto { CancellationRequested = requested }));
    }

    public Task<ApiResult<HeldKeysSnapshotDto>> GetHeldKeysAsync(UiSessionRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Task.FromResult(Fail<HeldKeysSnapshotDto>(error!));
        }

        return Task.FromResult(ApiResult<HeldKeysSnapshotDto>.Success(
            MapHeldKeys(_execution.GetHeldKeys())));
    }

    public async Task<ApiResult<HeldKeysSnapshotDto>> ReleaseHeldKeysAsync(UiSessionRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Fail<HeldKeysSnapshotDto>(error!);
        }

        var snap = await _execution.ReleaseAllAsync().ConfigureAwait(false);
        var dto = MapHeldKeys(snap);
        RaiseHeldKeys(dto);
        RaiseRuns(BuildRunPage());

        if (!snap.SenderAvailable && snap.Keys.Count > 0)
        {
            return ApiResult<HeldKeysSnapshotDto>.Fail(
                ValidationCodes.InputCleanupFailed,
                "释放保持键未完全成功",
                details: dto);
        }

        return ApiResult<HeldKeysSnapshotDto>.Success(dto);
    }

    public async Task<ApiResult<ImportPreviewDto>> PreviewImportAsync(UiSessionRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Fail<ImportPreviewDto>(error!);
        }

        if (IsRecordingBlocking())
        {
            return ApiResult<ImportPreviewDto>.Fail(
                ValidationCodes.RecordingBusy, "录制进行中不能导入");
        }

        var picked = await PickOpenJsonAsync().ConfigureAwait(false);
        if (!picked.Ok)
        {
            return ApiResult<ImportPreviewDto>.Fail(
                picked.Code ?? ValidationCodes.Cancelled,
                picked.Message ?? "已取消");
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(picked.Path!).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ApiResult<ImportPreviewDto>.Fail(ValidationCodes.FileAccessFailed, ex.Message);
        }

        var result = _transfer.PreviewFromBytes(_activeSessionId!, bytes, picked.Path);
        if (!result.Ok || result.Preview is null)
        {
            return ApiResult<ImportPreviewDto>.Fail(
                result.Code ?? ValidationCodes.InvalidFormat,
                result.Message);
        }

        return ApiResult<ImportPreviewDto>.Success(MapImportPreview(result.Preview));
    }

    public async Task<ApiResult<CommitImportResultDto>> CommitImportAsync(CommitImportRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Fail<CommitImportResultDto>(error!);
        }

        if (string.IsNullOrWhiteSpace(request.PreviewId))
        {
            return ApiResult<CommitImportResultDto>.Fail(
                ValidationCodes.ImportPreviewExpired, "缺少 previewId");
        }

        var result = await _transfer
            .CommitAsync(
                _activeSessionId!,
                request.PreviewId.Trim(),
                request.AcknowledgedWarningFingerprint)
            .ConfigureAwait(false);

        if (!result.Ok)
        {
            object? details = null;
            if (result.Persisted)
            {
                details = new { persisted = true, createdIds = result.CreatedIds };
            }

            return ApiResult<CommitImportResultDto>.Fail(
                result.Code ?? ValidationCodes.PersistenceFailed,
                result.Message,
                details: details);
        }

        var items = (result.CreatedItems ?? [])
            .Select(ListItemFromDetail)
            .ToList();
        var ids = items.Select(i => i.NodeId).ToList();
        RaiseNodesChanged("imported", ids);
        RaiseCategoriesChanged("updated", []);
        return ApiResult<CommitImportResultDto>.Success(new CommitImportResultDto { CreatedItems = items });
    }

    public Task<ApiResult<ReleaseImportPreviewResultDto>> ReleaseImportPreviewAsync(
        ImportPreviewIdRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Task.FromResult(Fail<ReleaseImportPreviewResultDto>(error!));
        }

        _transfer.ReleasePreview(request.PreviewId);
        return Task.FromResult(ApiResult<ReleaseImportPreviewResultDto>.Success(
            new ReleaseImportPreviewResultDto { Released = true }));
    }

    public async Task<ApiResult<ExportNodesResultDto>> ExportNodesAsync(ExportNodesRequest request)
    {
        if (TrySessionError(request, allowBind: false, out var error))
        {
            return Fail<ExportNodesResultDto>(error!);
        }

        var ids = (request.NodeIds ?? [])
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .ToList();

        if (ids.Count == 0)
        {
            return ApiResult<ExportNodesResultDto>.Fail(
                ValidationCodes.ValidationFailed, "未选择导出节点");
        }

        if (IsRecordingBlocking())
        {
            return ApiResult<ExportNodesResultDto>.Fail(
                ValidationCodes.RecordingBusy, "录制进行中不能导出");
        }

        var picked = await PickSaveJsonAsync().ConfigureAwait(false);
        if (!picked.Ok)
        {
            return ApiResult<ExportNodesResultDto>.Fail(
                picked.Code ?? ValidationCodes.Cancelled,
                picked.Message ?? "已取消");
        }

        try
        {
            await using var stream = new FileStream(
                picked.Path!,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.Asynchronous);

            var result = await _transfer.ExportAsync(ids, stream).ConfigureAwait(false);
            if (!result.Ok)
            {
                return ApiResult<ExportNodesResultDto>.Fail(
                    result.Code ?? ValidationCodes.FileAccessFailed,
                    result.Message);
            }

            return ApiResult<ExportNodesResultDto>.Success(new ExportNodesResultDto
            {
                ExportedCount = result.ExportedCount,
                FileName = Path.GetFileName(picked.Path),
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ApiResult<ExportNodesResultDto>.Fail(ValidationCodes.FileAccessFailed, ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopPolling();
        if (_subscribedRecording)
        {
            _recording.RecordingChanged -= OnRecordingChanged;
            _subscribedRecording = false;
        }
    }

    private bool TrySessionError(UiSessionRequest? request, bool allowBind, out ApiError? error)
    {
        error = null;
        if (request is null || string.IsNullOrWhiteSpace(request.WindowSessionId))
        {
            error = new ApiError
            {
                Code = ValidationCodes.WindowSessionClosed,
                Message = "缺少 windowSessionId",
            };
            return true;
        }

        var incoming = request.WindowSessionId.Trim();
        lock (_sessionGate)
        {
            if (!_windowAlive)
            {
                error = new ApiError
                {
                    Code = ValidationCodes.WindowSessionClosed,
                    Message = "管理窗口已关闭",
                };
                return true;
            }

            // InitializeAsync adopts the client windowSessionId (Vue generates it).
            // Host OpenSession pre-assigns an id for lifetime tracking; Initialize may replace it.
            if (allowBind)
            {
                _activeSessionId = incoming;
                _sessionBound = true;
                return false;
            }

            if (!_sessionBound
                || !string.Equals(_activeSessionId, incoming, StringComparison.Ordinal))
            {
                error = new ApiError
                {
                    Code = ValidationCodes.WindowSessionClosed,
                    Message = "窗口会话已失效",
                };
                return true;
            }
        }

        return false;
    }

    private static ApiResult<T> Fail<T>(ApiError error) => new()
    {
        Ok = false,
        Data = default,
        Error = error,
    };

    private NodePageDto BuildNodePage(
        string? query,
        string? scope,
        string? categoryId,
        int page,
        int pageSize)
    {
        var normalizedScope = NormalizeScope(scope);
        var all = EnumerateListItems()
            .Where(item => MatchesScope(item, normalizedScope, categoryId))
            .Where(item => MatchesQuery(item, query))
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.NodeId, StringComparer.Ordinal)
            .ToList();

        var size = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100);
        var total = all.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)size));
        var p = Math.Clamp(page <= 0 ? 1 : page, 1, totalPages);
        var items = all.Skip((p - 1) * size).Take(size).ToList();

        long seq;
        lock (_nodesSeqGate)
        {
            seq = _nodesSequence;
        }

        return new NodePageDto
        {
            Items = items,
            Total = total,
            Page = p,
            PageSize = size,
            SequenceNumber = seq,
        };
    }

    private static string NormalizeScope(string? scope)
    {
        if (string.Equals(scope, KeySenderConst.ListScopeBuiltin, StringComparison.OrdinalIgnoreCase))
        {
            return KeySenderConst.ListScopeBuiltin;
        }

        if (string.Equals(scope, KeySenderConst.ListScopeCategory, StringComparison.OrdinalIgnoreCase))
        {
            return KeySenderConst.ListScopeCategory;
        }

        return KeySenderConst.ListScopeAll;
    }

    private static bool MatchesScope(NodeListItemDto item, string scope, string? categoryId)
    {
        if (scope == KeySenderConst.ListScopeBuiltin)
        {
            return item.IsBuiltIn;
        }

        if (scope == KeySenderConst.ListScopeCategory)
        {
            return !item.IsBuiltIn
                   && !string.IsNullOrWhiteSpace(categoryId)
                   && string.Equals(item.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private IEnumerable<NodeListItemDto> EnumerateListItems()
    {
        var group = _plugin.FindFirstDescendant<KeySenderSearchGroup>(
            g => g.Id == KeySenderConst.SearchGroupId);
        if (group is null)
        {
            yield break;
        }

        foreach (var node in group.GetDescendants<KeySenderNode>())
        {
            yield return ToListItem(node);
        }
    }

    private CategoryListDto BuildCategoryList()
    {
        var group = _plugin.FindFirstDescendant<KeySenderSearchGroup>(
            g => g.Id == KeySenderConst.SearchGroupId);
        var items = new List<CategoryListItemDto>();
        if (group is not null)
        {
            foreach (var category in group.GetDescendants<KeySenderCategory>()
                         .OrderBy(static c => c.GetName() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(static c => c.Id, StringComparer.Ordinal))
            {
                var count = category.GetDescendants<KeySenderNode>()
                    .Count(static n => !BuiltInKeyCatalog.IsBuiltIn(n.Id));
                items.Add(new CategoryListItemDto
                {
                    CategoryId = category.Id,
                    Name = category.GetName() ?? string.Empty,
                    Description = category.GetDescription() ?? string.Empty,
                    NodeCount = count,
                    IsDefault = category.IsDefault,
                    CanRename = true,
                    CanDelete = true,
                });
            }
        }

        long seq;
        lock (_categoriesSeqGate)
        {
            seq = _categoriesSequence;
        }

        return new CategoryListDto
        {
            Items = items,
            SequenceNumber = seq,
        };
    }

    private NodeListItemDto? FindListItem(string nodeId)
        => EnumerateListItems()
            .FirstOrDefault(item => string.Equals(item.NodeId, nodeId, StringComparison.Ordinal));

    private static NodeListItemDto ToListItem(KeySenderNode node)
    {
        var isBuiltIn = BuiltInKeyCatalog.IsBuiltIn(node.Id);
        var name = node.GetName() ?? string.Empty;
        var aliases = node.GetAliasName()?.Where(static a => !string.IsNullOrWhiteSpace(a)).ToList()
                      ?? [];
        string? categoryId = null;
        string? categoryName = null;
        if (node.Parent is KeySenderCategory category)
        {
            categoryId = category.Id;
            categoryName = category.GetName() ?? string.Empty;
        }

        return new NodeListItemDto
        {
            NodeId = node.Id,
            Name = name,
            Description = node.GetDescription() ?? string.Empty,
            AliasNames = aliases,
            HotkeyDisplay = FormatHotkey(node),
            IsPinned = node.GetIsPinned() == true,
            DragText = name,
            IsBuiltIn = isBuiltIn,
            IsReadOnly = isBuiltIn,
            CanEdit = !isBuiltIn,
            CanDelete = !isBuiltIn,
            CategoryId = categoryId,
            CategoryName = categoryName,
        };
    }

    private static NodeListItemDto ListItemFromDetail(NodeDetail detail)
        => new()
        {
            NodeId = detail.NodeId,
            Name = detail.Name,
            Description = detail.Description,
            AliasNames = [],
            HotkeyDisplay = "",
            IsPinned = false,
            DragText = detail.Name,
            IsBuiltIn = detail.IsBuiltIn,
            IsReadOnly = detail.IsReadOnly,
            CanEdit = detail.CanEdit,
            CanDelete = detail.CanDelete,
            CategoryId = null,
            CategoryName = null,
        };

    private static string FormatHotkey(KeySenderNode node)
    {
        try
        {
            var hotkey = node.GetHotkey();
            if (hotkey is null)
            {
                return string.Empty;
            }

            var text = Convert.ToString(hotkey, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(text) || text == "0" ? string.Empty : text;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool MatchesQuery(NodeListItemDto item, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var q = query.Trim();
        if (item.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || item.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return item.AliasNames.Any(a => a.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    private RunPageDto BuildRunPage()
    {
        var active = _execution.GetActiveAndQueuedRuns().Select(MapRun).ToList();
        var recent = _execution.GetRecentRuns().Select(MapRun).ToList();
        long seq;
        lock (_runsSeqGate)
        {
            seq = _runsSequence;
        }

        return new RunPageDto
        {
            SequenceNumber = seq,
            Active = active,
            Recent = recent,
        };
    }

    private void RaiseNodesChanged(string kind, IReadOnlyList<string> nodeIds)
    {
        long seq;
        lock (_nodesSeqGate)
        {
            _nodesSequence++;
            seq = _nodesSequence;
        }

        NodesChanged?.Invoke(this, new NodesChangedDto
        {
            Kind = kind,
            NodeIds = nodeIds,
            SequenceNumber = seq,
        });
    }

    private void RaiseCategoriesChanged(string kind, IReadOnlyList<string> categoryIds)
    {
        long seq;
        lock (_categoriesSeqGate)
        {
            _categoriesSequence++;
            seq = _categoriesSequence;
        }

        CategoriesChanged?.Invoke(this, new CategoriesChangedDto
        {
            Kind = kind,
            CategoryIds = categoryIds,
            SequenceNumber = seq,
        });
    }

    private void RaiseRuns(RunPageDto page)
    {
        lock (_runsSeqGate)
        {
            _runsSequence++;
            page = new RunPageDto
            {
                SequenceNumber = _runsSequence,
                Active = page.Active,
                Recent = page.Recent,
            };
        }

        _lastRunsFingerprint = FingerprintRuns(page);
        RunsChanged?.Invoke(this, page);
    }

    private void RaiseHeldKeys(HeldKeysSnapshotDto dto)
    {
        _lastHeldSequence = dto.SequenceNumber;
        HeldKeysChanged?.Invoke(this, dto);
    }

    private void RaiseRecording(RecordingSnapshotDto dto)
        => RecordingChanged?.Invoke(this, dto);

    private void EnsureRecordingSubscription()
    {
        if (_subscribedRecording)
        {
            return;
        }

        _recording.RecordingChanged += OnRecordingChanged;
        _subscribedRecording = true;
    }

    private void OnRecordingChanged(object? sender, RecordingSnapshot snapshot)
    {
        string? session;
        lock (_sessionGate)
        {
            session = _activeSessionId;
            if (!_windowAlive || session is null)
            {
                return;
            }
        }

        if (!string.Equals(snapshot.WindowSessionId, session, StringComparison.Ordinal)
            && snapshot.State is not RecordingState.Idle
                and not RecordingState.Cancelled
                and not RecordingState.Failed)
        {
            return;
        }

        RaiseRecording(MapRecording(snapshot));
    }

    private void StartPolling()
    {
        StopPolling();
        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(400, token).ConfigureAwait(false);
                    PollState();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // keep polling
                }
            }
        }, token);
    }

    private void StopPolling()
    {
        var cts = _pollCts;
        _pollCts = null;
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch
        {
            // ignore
        }

        cts.Dispose();
    }

    private void PollState()
    {
        lock (_sessionGate)
        {
            if (!_windowAlive || !_sessionBound)
            {
                return;
            }
        }

        var held = MapHeldKeys(_execution.GetHeldKeys());
        if (held.SequenceNumber != _lastHeldSequence)
        {
            RaiseHeldKeys(held);
        }

        var runs = BuildRunPage();
        var fingerprint = FingerprintRuns(runs);
        if (!string.Equals(fingerprint, _lastRunsFingerprint, StringComparison.Ordinal))
        {
            RaiseRuns(runs);
        }
    }

    private static string FingerprintRuns(RunPageDto page)
    {
        var sb = new StringBuilder();
        sb.Append(page.Active.Count).Append('|').Append(page.Recent.Count);
        foreach (var run in page.Active)
        {
            sb.Append(';').Append(run.RunId).Append(':').Append(run.State)
                .Append(':').Append(run.EventIndex).Append(':').Append(run.IsCancellationRequested);
        }

        foreach (var run in page.Recent.Take(5))
        {
            sb.Append(';').Append(run.RunId).Append(':').Append(run.State);
        }

        return sb.ToString();
    }

    private bool IsRecordingBlocking()
    {
        var snap = _recording.GetSnapshot();
        return snap is not null
               && snap.State is not RecordingState.Idle
                   and not RecordingState.Cancelled
                   and not RecordingState.Failed
                   and not RecordingState.Review;
    }

    private static ValidationResultDto MapValidation(SequenceAnalysisResult analysis) => new()
    {
        Valid = analysis.IsValid,
        Errors = analysis.HardErrors,
        Warnings = analysis.Warnings,
        WarningFingerprint = analysis.HasWarnings ? analysis.WarningFingerprint : null,
    };

    private static ApiResult<T> MapConfigFail<T>(ConfigurationResult result)
    {
        object? details = null;
        if (result.Analysis is not null)
        {
            details = new
            {
                warningFingerprint = result.Analysis.WarningFingerprint,
                warnings = result.Analysis.Warnings,
                errors = result.Analysis.HardErrors,
            };
        }
        else if (string.Equals(result.Code, ValidationCodes.DeleteIncomplete, StringComparison.Ordinal))
        {
            details = new { deletedIds = result.DeletedIds, remainingIds = result.RemainingIds };
        }
        else if (result.Persisted)
        {
            details = new { persisted = true, createdIds = result.CreatedIds };
        }

        return ApiResult<T>.Fail(
            result.Code ?? ValidationCodes.PersistenceFailed,
            result.Message,
            nodeId: result.NodeId,
            details: details);
    }

    private static ApiResult<RecordingSnapshotDto> MapRecordingFail(RecordingOperationResult result)
        => ApiResult<RecordingSnapshotDto>.Fail(
            result.Code ?? ValidationCodes.RecordingUnavailable,
            result.Message,
            details: result.Snapshot is null ? null : MapRecording(result.Snapshot));

    private static RecordingSnapshotDto MapRecording(RecordingSnapshot snap) => new()
    {
        RecordingId = snap.RecordingId,
        WindowSessionId = snap.WindowSessionId,
        State = ToCamel(snap.State.ToString()),
        EventCount = snap.EventCount,
        ElapsedMs = snap.ElapsedMs,
        SequenceNumber = snap.SequenceNumber,
        Notices = snap.Notices,
        Error = snap.Error,
    };

    private static RecordingFragmentDto MapFragment(RecordingFragment fragment) => new()
    {
        RecordingId = fragment.RecordingId,
        Events = fragment.Events,
        Notices = fragment.Notices,
    };

    private static RunSnapshotDto MapRun(RunSnapshot snap) => new()
    {
        RunId = snap.RunId.ToString("D"),
        NodeId = snap.NodeId,
        NodeName = snap.NodeName,
        State = ToCamel(snap.State.ToString()),
        EventIndex = snap.EventIndex,
        EventCount = snap.EventCount,
        QueuePosition = snap.QueuePosition,
        SubmittedAt = snap.SubmittedAt,
        StartedAt = snap.StartedAt,
        FinishedAt = snap.FinishedAt,
        IsCancellationRequested = snap.IsCancellationRequested,
        Error = snap.Error,
    };

    private static HeldKeysSnapshotDto MapHeldKeys(HeldKeysSnapshot snap) => new()
    {
        SequenceNumber = snap.SequenceNumber,
        Keys = snap.Keys.Select(static k => new HeldKeyItemDto
        {
            Key = k.Key,
            Display = FormatKeyDisplay(k.Key),
            SourceRunId = k.SourceRunId.ToString("D"),
            SourceNodeId = k.SourceNodeId,
            HeldSince = k.HeldSince,
            Uncertain = k.Uncertain,
        }).ToList(),
        IsReleasing = snap.IsReleasing,
        SenderAvailable = snap.SenderAvailable,
    };

    private static ImportPreviewDto MapImportPreview(ImportPreview preview) => new()
    {
        PreviewId = preview.PreviewId,
        ExpiresAt = preview.ExpiresAt,
        ItemCount = preview.ItemCount,
        CanImport = preview.CanImport,
        WarningFingerprint = preview.WarningFingerprint,
        Items = preview.Items,
    };

    private static string ToCamel(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToLowerInvariant(value[0]) + value[1..];
    }

    private static string FormatKeyDisplay(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return "";
        }

        return key switch
        {
            "LeftControl" => "左 Ctrl",
            "RightControl" => "右 Ctrl",
            "LeftShift" => "左 Shift",
            "RightShift" => "右 Shift",
            "LeftAlt" => "左 Alt",
            "RightAlt" => "右 Alt",
            "LeftWin" => "左 Win",
            "RightWin" => "右 Win",
            "Space" => "空格",
            "Escape" => "Esc",
            "Backspace" => "退格",
            _ when key.StartsWith("Key", StringComparison.Ordinal) && key.Length == 4
                => key[3].ToString(),
            _ when key.StartsWith("Digit", StringComparison.Ordinal)
                => key[5..],
            _ when key.StartsWith("Numpad", StringComparison.Ordinal)
                => "小键盘 " + key[6..],
            _ => key,
        };
    }

    private static IReadOnlyList<KeyCatalogEntryDto> BuildKeyCatalog()
    {
        var list = new List<KeyCatalogEntryDto>(KeyTokenCatalog.All.Count);
        foreach (var info in KeyTokenCatalog.All.OrderBy(static i => i.Token, StringComparer.Ordinal))
        {
            list.Add(new KeyCatalogEntryDto
            {
                Key = info.Token,
                Display = FormatKeyDisplay(info.Token),
                Group = InferGroup(info.Token),
            });
        }

        return list;
    }

    private static string InferGroup(string token)
    {
        if (token is "LeftControl" or "RightControl" or "LeftShift" or "RightShift"
            or "LeftAlt" or "RightAlt" or "LeftWin" or "RightWin")
        {
            return "修饰键";
        }

        if (token.StartsWith("Key", StringComparison.Ordinal))
        {
            return "字母";
        }

        if (token.StartsWith("Digit", StringComparison.Ordinal))
        {
            return "数字";
        }

        if (token.Length >= 2 && token[0] == 'F' && char.IsDigit(token[1]))
        {
            return "功能键";
        }

        if (token.StartsWith("Numpad", StringComparison.Ordinal))
        {
            return "小键盘";
        }

        return "其它";
    }

    private sealed class FilePickResult
    {
        public bool Ok { get; init; }

        public string? Path { get; init; }

        public string? Code { get; init; }

        public string? Message { get; init; }

        public static FilePickResult Success(string path) => new() { Ok = true, Path = path };

        public static FilePickResult Fail(string code, string? message = null) => new()
        {
            Ok = false,
            Code = code,
            Message = message,
        };
    }

    private static async Task<FilePickResult> PickOpenJsonAsync()
    {
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var host = ResolvePluginWindow();
                if (host?.StorageProvider is null)
                {
                    return FilePickResult.Fail(ValidationCodes.Cancelled, "无法打开文件对话框");
                }

                var files = await host.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "导入按键配置",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("KeySender JSON")
                        {
                            Patterns = ["*.json"],
                        },
                    ],
                }).ConfigureAwait(true);

                if (files.Count == 0)
                {
                    return FilePickResult.Fail(ValidationCodes.Cancelled, "已取消");
                }

                var path = files[0].TryGetLocalPath();
                return string.IsNullOrWhiteSpace(path)
                    ? FilePickResult.Fail(ValidationCodes.FileAccessFailed, "无法解析文件路径")
                    : FilePickResult.Success(path);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return FilePickResult.Fail(ValidationCodes.FileAccessFailed, ex.Message);
        }
    }

    private static async Task<FilePickResult> PickSaveJsonAsync()
    {
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var host = ResolvePluginWindow();
                if (host?.StorageProvider is null)
                {
                    return FilePickResult.Fail(ValidationCodes.Cancelled, "无法打开保存对话框");
                }

                var file = await host.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "导出按键配置",
                    SuggestedFileName = "keysender-export.json",
                    DefaultExtension = "json",
                    FileTypeChoices =
                    [
                        new FilePickerFileType("KeySender JSON")
                        {
                            Patterns = ["*.json"],
                        },
                    ],
                }).ConfigureAwait(true);

                if (file is null)
                {
                    return FilePickResult.Fail(ValidationCodes.Cancelled, "已取消");
                }

                var path = file.TryGetLocalPath();
                return string.IsNullOrWhiteSpace(path)
                    ? FilePickResult.Fail(ValidationCodes.FileAccessFailed, "无法解析文件路径")
                    : FilePickResult.Success(path);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return FilePickResult.Fail(ValidationCodes.FileAccessFailed, ex.Message);
        }
    }

    private static TopLevel? ResolvePluginWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
            {
                if (window is PluginWebView)
                {
                    return window;
                }
            }

            return desktop.MainWindow;
        }

        return null;
    }
}
