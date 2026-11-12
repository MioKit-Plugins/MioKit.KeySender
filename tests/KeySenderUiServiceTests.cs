using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using KeySender;
using KeySender.Models;
using KeySender.Models.Ui;
using KeySender.Services;
using KeySender.Validation;
using MioKit.KeySender.Tests.Fakes;
using MioKit.Sdk;
using Xunit;

namespace MioKit.KeySender.Tests;

[Collection(ConfigurationPersistenceCollection.Name)]
public sealed class KeySenderUiServiceTests
{
    private const string SessionId = "manager-session";
    private const string CategoryId = "test-category";
    private const string NodeId = "editable-node";

    [Fact]
    public async Task Initialization_binds_the_window_and_a_closed_or_reopened_window_rejects_stale_requests()
    {
        using var designMode = DesignModeScope.Enter();
        using var harness = new Harness();
        var ui = harness.Ui;

        Assert.Equal(ValidationCodes.WindowSessionClosed, (await ui.InitializeAsync(Initialize())).Error?.Code);
        ui.OnWindowOpened();
        Assert.Equal(ValidationCodes.WindowSessionClosed, (await ui.GetListAsync(List())).Error?.Code);
        Assert.Equal(ValidationCodes.WindowSessionClosed,
            (await ui.InitializeAsync(new InitializeRequest { WindowSessionId = " " })).Error?.Code);

        var initialized = RequireData(await ui.InitializeAsync(Initialize()));
        Assert.Equal("模拟按键", initialized.DisplayName);
        Assert.Contains(initialized.KeyCatalog, key => key.Key == "LeftShift");
        Assert.Equal(3, initialized.Nodes.Total);
        Assert.Equal(1, Assert.Single(initialized.Categories).NodeCount);
        Assert.Empty(initialized.Runs.Active);
        Assert.True((await ui.GetListAsync(List())).Ok);

        ui.OnWindowClosed();
        Assert.Equal(ValidationCodes.WindowSessionClosed, (await ui.GetListAsync(List())).Error?.Code);
        ui.OnWindowOpened();
        RequireData(await ui.InitializeAsync(Initialize("reopened")));
        Assert.Equal(ValidationCodes.WindowSessionClosed, (await ui.GetListAsync(List())).Error?.Code);
        Assert.True((await ui.GetListAsync(new ListRequest { WindowSessionId = "reopened" })).Ok);
    }

    [Fact]
    public async Task List_filters_before_paging_and_exposes_builtin_permissions()
    {
        using var designMode = DesignModeScope.Enter();
        using var harness = new Harness();
        await harness.OpenAsync();

        var all = RequireData(await harness.Ui.GetListAsync(new ListRequest
        {
            WindowSessionId = SessionId, Page = 99, PageSize = 2,
        }));
        Assert.Equal(3, all.Total);
        Assert.Equal(2, all.Page);
        Assert.Equal(KeySenderConst.BuiltInCopyNodeId, Assert.Single(all.Items).NodeId);

        var byDescription = RequireData(await harness.Ui.GetListAsync(new ListRequest
        {
            WindowSessionId = SessionId, Scope = "CATEGORY", CategoryId = CategoryId.ToUpperInvariant(),
            Query = "  CLIPBOARD  ", Page = -10, PageSize = int.MaxValue,
        }));
        Assert.Equal(1, byDescription.Total);
        Assert.Equal(1, byDescription.Page);
        Assert.Equal(100, byDescription.PageSize);
        var editable = Assert.Single(byDescription.Items);
        Assert.Equal(NodeId, editable.NodeId);
        Assert.Equal(CategoryId, editable.CategoryId);
        Assert.True(editable.CanEdit);
        Assert.True(editable.CanDelete);

        var builtIn = Assert.Single(RequireData(await harness.Ui.GetListAsync(new ListRequest
        {
            WindowSessionId = SessionId, Scope = "builtin",
        })).Items);
        Assert.True(builtIn.IsReadOnly);
        Assert.False(builtIn.CanEdit);
        Assert.False(builtIn.CanDelete);
    }

    [Fact]
    public async Task Validation_keeps_field_errors_and_failed_saves_do_not_publish_changes()
    {
        using var designMode = DesignModeScope.Enter();
        using var harness = new Harness();
        await harness.OpenAsync();
        var ui = harness.Ui;
        var changes = new List<NodesChangedDto>();
        ui.NodesChanged += (_, change) => changes.Add(change);
        var invalid = Write("Updated");
        invalid.Sequence.Events[0].Key = "UnrecognizedKey";

        var validation = RequireData(await ui.ValidateDraftAsync(new ValidateDraftRequest
        {
            WindowSessionId = SessionId, Detail = invalid,
        }));
        Assert.False(validation.Valid);
        var issue = Assert.Single(validation.Errors);
        Assert.Equal(ValidationCodes.UnsupportedKey, issue.Code);
        Assert.Equal("sequence.events[0].key", issue.FieldPath);
        Assert.Equal(0, issue.EventIndex);
        var rejected = await ui.UpdateNodeAsync(new UpdateNodeRequest
        {
            WindowSessionId = SessionId, NodeId = NodeId, Detail = invalid,
        });
        Assert.False(rejected.Ok);
        Assert.Equal(ValidationCodes.ValidationFailed, rejected.Error?.Code);
        using var errorsJson = JsonDocument.Parse(JsonSerializer.Serialize(
            rejected.Error?.Details, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(ValidationCodes.UnsupportedKey,
            errorsJson.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Empty(changes);
        Assert.Equal(0, harness.Data.SaveObjectsCount);

        var before = RequireData(await ui.GetListAsync(List()));
        var updated = RequireData(await ui.UpdateNodeAsync(new UpdateNodeRequest
        {
            WindowSessionId = SessionId, NodeId = NodeId, Detail = Write("Updated"),
        }));
        Assert.Equal("Updated", updated.Name);
        var change = Assert.Single(changes);
        Assert.Equal("updated", change.Kind);
        Assert.Equal([NodeId], change.NodeIds);
        Assert.True(change.SequenceNumber > before.SequenceNumber);
        Assert.Equal(change.SequenceNumber, RequireData(await ui.GetListAsync(List())).SequenceNumber);
        var detail = RequireData(await ui.GetDetailAsync(new NodeIdRequest
        {
            WindowSessionId = SessionId, NodeId = NodeId,
        }));
        Assert.Equal("KeyB", Assert.Single(detail.Sequence.Events).Key);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Active_or_queued_playback_blocks_deleting_its_node_or_category(bool deleteCategory)
    {
        using var designMode = DesignModeScope.Enter();
        using var harness = new Harness();
        harness.Execution.Active = [Run(NodeId, RunState.Queued)];
        await harness.OpenAsync();

        var rejected = deleteCategory
            ? await harness.Ui.DeleteCategoryAsync(new CategoryIdRequest
            {
                WindowSessionId = SessionId, CategoryId = CategoryId,
            })
            : await harness.Ui.DeleteNodesAsync(new DeleteNodesRequest
            {
                WindowSessionId = SessionId, NodeIds = [NodeId],
            });
        Assert.False(rejected.Ok);
        Assert.Equal(ValidationCodes.NodeInUse, rejected.Error?.Code);
        Assert.Contains(RequireData(await harness.Ui.GetListAsync(List())).Items, item => item.NodeId == NodeId);
        Assert.Equal(1, Assert.Single(RequireData(await harness.Ui.GetCategoriesAsync(Session())).Items).NodeCount);
    }

    [Fact]
    public async Task Run_snapshot_maps_status_and_cancel_forwards_only_valid_run_ids()
    {
        using var designMode = DesignModeScope.Enter();
        using var harness = new Harness();
        var running = Run(NodeId, RunState.EventDelay);
        var done = Run("completed-node", RunState.Succeeded);
        harness.Execution.Active = [running];
        harness.Execution.Recent = [done];
        await harness.OpenAsync();
        var ui = harness.Ui;
        var changes = new ConcurrentQueue<RunPageDto>();
        ui.RunsChanged += (_, page) => changes.Enqueue(page);
        var before = RequireData(await ui.GetRunsAsync(Session()));
        var active = Assert.Single(before.Active);
        Assert.Equal(running.RunId.ToString("D"), active.RunId);
        Assert.Equal("eventDelay", active.State);
        Assert.Equal("succeeded", Assert.Single(before.Recent).State);

        var invalid = await ui.CancelRunAsync(new CancelRunRequest
        {
            WindowSessionId = SessionId, RunId = "bad-id",
        });
        Assert.Equal(ValidationCodes.NodeNotFound, invalid.Error?.Code);
        Assert.Empty(harness.Execution.Cancelled);

        var cancelled = RequireData(await ui.CancelRunAsync(new CancelRunRequest
        {
            WindowSessionId = SessionId, RunId = active.RunId,
        }));
        Assert.True(cancelled.CancellationRequested);
        Assert.Equal([running.RunId], harness.Execution.Cancelled);
        Assert.Contains(changes, page => page.SequenceNumber > before.SequenceNumber);
    }

    [Fact]
    public async Task Recording_bridge_returns_review_fragment_then_consumes_it()
    {
        using var designMode = DesignModeScope.Enter();
        using var harness = new Harness();
        await harness.OpenAsync();
        var ui = harness.Ui;
        var notifications = new ConcurrentQueue<RecordingSnapshotDto>();
        ui.RecordingChanged += (_, snapshot) => notifications.Enqueue(snapshot);
        var started = RequireData(await ui.BeginRecordingAsync(Session()));
        Assert.Equal(SessionId, started.WindowSessionId);
        Assert.Equal("recording", started.State);
        harness.Hook.Push(new RawKeyboardEvent { VkCode = 0x41, ScanCode = 0x1E });

        var request = new RecordingIdRequest { WindowSessionId = SessionId, RecordingId = started.RecordingId };
        var stopped = RequireData(await ui.StopRecordingAsync(request));
        Assert.Equal("review", stopped.State);
        Assert.Equal(1, stopped.EventCount);
        Assert.True(stopped.SequenceNumber > started.SequenceNumber);
        var review = RequireData(await ui.GetRecordingFragmentAsync(request));
        var applied = RequireData(await ui.ApplyRecordingAsync(request));
        var evt = Assert.Single(applied.Events);
        Assert.Equal(Assert.Single(review.Events).EventId, evt.EventId);
        Assert.Equal("KeyA", evt.Key);
        Assert.Equal("keyDown", evt.Type);
        Assert.Contains(notifications, item => item.RecordingId == started.RecordingId && item.State == "idle");
        Assert.Equal(ValidationCodes.RecordingNotReady, (await ui.ApplyRecordingAsync(request)).Error?.Code);
    }

    [Fact]
    public async Task Import_file_accepts_the_byte_limit_with_a_bom_and_rejects_one_extra_byte()
    {
        using var designMode = DesignModeScope.Enter();
        using var harness = new Harness();
        await harness.OpenAsync();
        var path = Path.GetTempFileName();
        try
        {
            var package = TransferPackageParser.WritePackageString([Write("Imported")]);
            var bytes = new byte[KeySenderConst.TransferMaxBytes];
            bytes.AsSpan().Fill((byte)' ');
            Encoding.UTF8.Preamble.CopyTo(bytes);
            Encoding.UTF8.GetBytes(package, bytes.AsSpan(Encoding.UTF8.Preamble.Length));
            await File.WriteAllBytesAsync(path, bytes);

            var preview = RequireData(await harness.Ui.PreviewImportFileAsync(Session(), path));
            var committed = RequireData(await harness.Ui.CommitImportAsync(new CommitImportRequest
            {
                WindowSessionId = SessionId,
                PreviewId = preview.PreviewId,
            }));
            Assert.Equal("Imported", Assert.Single(committed.CreatedItems).Name);

            await using (var stream = File.Open(path, FileMode.Append))
            {
                stream.WriteByte((byte)' ');
            }

            var rejected = await harness.Ui.PreviewImportFileAsync(Session(), path);
            Assert.False(rejected.Ok);
            Assert.Equal(ValidationCodes.FileTooLarge, rejected.Error?.Code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Export_rejects_too_many_selected_nodes_before_opening_the_file_picker()
    {
        using var designMode = DesignModeScope.Enter();
        using var harness = new Harness();
        await harness.OpenAsync();

        var rejected = await harness.Ui.ExportNodesAsync(new ExportNodesRequest
        {
            WindowSessionId = SessionId,
            NodeIds = Enumerable.Range(0, KeySenderConst.TransferMaxNodes + 1)
                .Select(index => $"node-{index}").ToList(),
        });

        Assert.False(rejected.Ok);
        Assert.Equal(ValidationCodes.ExportLimitExceeded, rejected.Error?.Code);
    }

    [Fact]
    public async Task Import_commit_returns_and_notifies_the_restored_category()
    {
        using var designMode = DesignModeScope.Enter();
        using var harness = new Harness();
        await harness.OpenAsync();
        var categoryChanges = new List<CategoriesChangedDto>();
        harness.Ui.CategoriesChanged += (_, change) => categoryChanges.Add(change);

        var committed = RequireData(await harness.Ui.CommitImportAsync(CategorizedImportRequest(harness)));

        var node = Assert.Single(committed.CreatedItems);
        Assert.Equal("Imported category", node.CategoryName);
        Assert.NotNull(node.CategoryId);
        Assert.Equal([node.CategoryId], Assert.Single(categoryChanges).CategoryIds);
        var categories = RequireData(await harness.Ui.GetCategoriesAsync(Session())).Items;
        var category = Assert.Single(categories, item => item.CategoryId == node.CategoryId);
        Assert.Equal("Category description", category.Description);
        Assert.Equal(1, category.NodeCount);
    }

    [Fact]
    public async Task Import_commit_reports_a_persisted_category_when_parenting_fails()
    {
        using var designMode = DesignModeScope.Enter();
        using var harness = new Harness();
        await harness.OpenAsync();
        harness.Data.FailParentUpdate = true;
        var categoryChanges = new List<CategoriesChangedDto>();
        var nodeChanges = new List<NodesChangedDto>();
        harness.Ui.CategoriesChanged += (_, change) => categoryChanges.Add(change);
        harness.Ui.NodesChanged += (_, change) => nodeChanges.Add(change);

        var result = await harness.Ui.CommitImportAsync(CategorizedImportRequest(harness));

        Assert.False(result.Ok);
        Assert.Equal(ValidationCodes.PersistenceFailed, result.Error?.Code);
        var persistedId = Assert.Single(harness.Data.SavedValues).Key;
        var details = JsonSerializer.SerializeToElement(result.Error?.Details);
        Assert.True(details.GetProperty("persisted").GetBoolean());
        Assert.Equal(0, details.GetProperty("createdIds").GetArrayLength());
        Assert.Equal(persistedId, Assert.Single(details.GetProperty("createdCategoryIds").EnumerateArray()).GetString());
        Assert.Equal([persistedId], Assert.Single(categoryChanges).CategoryIds);
        Assert.Empty(nodeChanges);
    }

    [Fact]
    public async Task Closing_manager_cancels_its_recording_and_invalidates_the_import_preview_without_cancelling_runs()
    {
        using var designMode = DesignModeScope.Enter();
        using var harness = new Harness();
        await harness.OpenAsync();
        var ui = harness.Ui;
        var started = RequireData(await ui.BeginRecordingAsync(Session()));
        var preview = harness.Transfer.PreviewFromString(SessionId,
            TransferPackageParser.WritePackageString([Write("Imported")]));
        Assert.True(preview.Ok, preview.Message);
        Assert.NotNull(preview.Preview);
        Assert.True(harness.Hook.IsRunning);

        ui.OnWindowClosed();

        Assert.False(harness.Hook.IsRunning);
        Assert.Equal(RecordingState.Cancelled, harness.Recording.GetSnapshot(started.RecordingId)?.State);
        var commit = await harness.Transfer.CommitAsync(SessionId, preview.Preview.PreviewId);
        Assert.Equal(ValidationCodes.ImportPreviewExpired, commit.Code);

        var active = Run(NodeId, RunState.AfterDelay);
        harness.Execution.Active = [active];
        await harness.OpenAsync();
        Assert.Equal(active.RunId.ToString("D"), Assert.Single(RequireData(await ui.GetRunsAsync(Session())).Active).RunId);
        ui.OnWindowClosed();
        Assert.Empty(harness.Execution.Cancelled);
    }

    private static T RequireData<T>(ApiResult<T> result) where T : class
    {
        Assert.True(result.Ok, $"{result.Error?.Code}: {result.Error?.Message}");
        return Assert.IsType<T>(result.Data);
    }

    private static UiSessionRequest Session() => new() { WindowSessionId = SessionId };
    private static InitializeRequest Initialize(string id = SessionId) => new() { WindowSessionId = id };
    private static ListRequest List() => new() { WindowSessionId = SessionId };

    private static CommitImportRequest CategorizedImportRequest(Harness harness)
    {
        var json = TransferPackageParser.WritePackageString(
            [Write("Imported")], [new TransferCategory("Imported category", "Category description")], [0]);
        var preview = harness.Transfer.PreviewFromString(SessionId, json);
        Assert.True(preview.Ok, preview.Message);
        return new CommitImportRequest
        {
            WindowSessionId = SessionId,
            PreviewId = Assert.IsType<ImportPreview>(preview.Preview).PreviewId,
        };
    }

    private static NodeWrite Write(string name) => new()
    {
        Name = name,
        Sequence = new KeyboardSequence
        {
            Events = [new KeyboardEvent { EventId = "edit", Key = "KeyB", Type = "keyDown" }],
        },
    };

    private static RunSnapshot Run(string nodeId, RunState state) => new()
    {
        RunId = Guid.NewGuid(), NodeId = nodeId, State = state, SubmittedAt = DateTimeOffset.UnixEpoch,
    };

    private sealed class Harness : IDisposable
    {
        public RecordingMioDataProvider Data { get; } = new();
        public FakeKeyboardHookSource Hook { get; } = new();
        public FakeExecution Execution { get; } = new();
        public RecordingService Recording { get; }
        public TransferService Transfer { get; }
        public KeySenderUiService Ui { get; }

        public Harness()
        {
            var plugin = new KeySenderPlugin();
            var group = new KeySenderSearchGroup(KeySenderConst.SearchGroupId);
            group.SetParent(plugin);
            var category = new KeySenderCategory(CategoryId);
            category.SetName("Work");
            category.SetParent(group);
            AddNode(NodeId, "Alpha", "Open clipboard", category);
            AddNode("other-node", "Bravo", "Another action", group);
            AddNode(KeySenderConst.BuiltInCopyNodeId, "Copy", "Read only", group);

            var configuration = new ConfigurationService(Data, plugin);
            Recording = new RecordingService(new FakeInputActivityGate(), Hook,
                new FakeTargetWindowOps(), new FakeMonotonicClock());
            Transfer = new TransferService(configuration);
            Ui = new KeySenderUiService(configuration, Recording, Execution, Transfer, plugin);
        }

        public async Task OpenAsync()
        {
            Ui.OnWindowOpened();
            RequireData(await Ui.InitializeAsync(Initialize()));
        }

        public void Dispose()
        {
            Ui.OnWindowClosed();
            Ui.Dispose();
            Recording.Dispose();
        }

        private static void AddNode(string id, string name, string description, MioObject parent)
        {
            var node = new KeySenderNode(id);
            node.SetName(name);
            node.SetDescription(description);
            node.SetParent(parent);
        }
    }

    private sealed class FakeExecution : IKeySenderExecutionService
    {
        public IReadOnlyList<RunSnapshot> Active { get; set; } = [];
        public IReadOnlyList<RunSnapshot> Recent { get; set; } = [];
        public List<Guid> Cancelled { get; } = [];

        public Task ExecuteAsync(KeySenderNode node, InvokeContext context) =>
            throw new NotSupportedException();

        public Task EnqueueAsync(string nodeId, string? nodeName, TargetIdentity target,
            InvokeSource source, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public bool TryCancel(Guid runId)
        {
            Cancelled.Add(runId);
            return Active.Any(run => run.RunId == runId);
        }

        public IReadOnlyList<RunSnapshot> GetActiveAndQueuedRuns() => Active;
        public IReadOnlyList<RunSnapshot> GetRecentRuns() => Recent;
    }
}
