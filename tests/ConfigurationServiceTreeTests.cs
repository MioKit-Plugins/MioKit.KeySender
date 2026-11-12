using KeySender;
using KeySender.BuiltIn;
using KeySender.Models;
using KeySender.Services;
using KeySender.Validation;
using MioKit.KeySender.Tests.Fakes;
using MioKit.Sdk;
using Xunit;

namespace MioKit.KeySender.Tests;

[Collection(ConfigurationPersistenceCollection.Name)]
public sealed class ConfigurationServiceTreeTests
{
    [Fact]
    public async Task Create_and_rename_category_persist_trimmed_values_and_preserve_unspecified_description()
    {
        using var designMode = DesignModeScope.Enter();
        var (group, service, data) = CreateHarness();

        var created = await service.CreateCategoryAsync("  工作  ", "  工作使用的组合键  ");

        Assert.True(created.Ok);
        var category = Assert.Single(group.GetDescendants<KeySenderCategory>());
        Assert.Equal(created.NodeId, category.Id);
        Assert.Equal(group.Id, Assert.Single(data.ParentUpdates)[category.Id]);
        Assert.Equal("工作", category.GetName());

        var renamed = await service.RenameCategoryAsync(category.Id, "  办公  ");

        Assert.True(renamed.Ok);
        Assert.Equal("办公", category.GetName());
        Assert.Equal("工作使用的组合键", category.GetDescription());
        Assert.Equal("办公", data.SavedValues[category.Id][MioFeatureExtensions.NameProperty]);
        Assert.Equal("工作使用的组合键", data.SavedValues[category.Id][MioFeatureExtensions.DescriptionProperty]);
    }

    [Fact]
    public async Task Import_reuses_a_unique_category_and_node_detail_keeps_the_local_metadata()
    {
        using var designMode = DesignModeScope.Enter();
        var (group, service, _) = CreateHarness();
        var local = await service.CreateCategoryAsync("Work", "本机说明");

        var resolved = await service.ResolveImportCategoryAsync("  work  ", "文件说明", allowReuse: true);

        Assert.True(resolved.Ok);
        Assert.Equal(local.NodeId, resolved.NodeId);
        Assert.False(resolved.Persisted);
        Assert.Equal("本机说明", Assert.Single(group.GetDescendants<KeySenderCategory>()).GetDescription());

        var created = await service.CreateInCategoryAsync(SampleWrite(), resolved.NodeId);
        Assert.True(created.Ok);
        Assert.Equal(local.NodeId, created.Detail!.CategoryId);
        Assert.Equal("Work", created.Detail.CategoryName);

        var loaded = await service.GetDetailAsync(created.NodeId!);
        Assert.Equal(new CategorySnapshot(local.NodeId!, "Work", "本机说明"), loaded.Category);
        Assert.Equal(local.NodeId, loaded.Detail!.CategoryId);
        Assert.Equal("Work", loaded.Detail.CategoryName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Import_creates_a_category_when_reuse_is_disabled_or_locally_ambiguous(bool allowReuse)
    {
        using var designMode = DesignModeScope.Enter();
        var (group, service, _) = CreateHarness();
        var local = await service.CreateCategoryAsync("work", "本机说明");
        if (allowReuse)
        {
            await service.CreateCategoryAsync("WORK", "另一个本机说明");
        }

        var resolved = await service.ResolveImportCategoryAsync(" Work ", " 文件说明 ", allowReuse);

        Assert.True(resolved.Ok);
        Assert.True(resolved.Persisted);
        Assert.NotEqual(local.NodeId, resolved.NodeId);
        var categories = group.GetDescendants<KeySenderCategory>().ToArray();
        Assert.Equal(allowReuse ? 3 : 2, categories.Length);
        var created = Assert.Single(categories, category => category.Id == resolved.NodeId);
        Assert.Equal("Work", created.GetName());
        Assert.Equal("文件说明", created.GetDescription());
    }

    [Fact]
    public async Task Import_reports_a_persisted_category_when_parent_update_fails()
    {
        using var designMode = DesignModeScope.Enter();
        var (group, service, data) = CreateHarness();
        data.FailParentUpdate = true;

        var resolved = await service.ResolveImportCategoryAsync("Work", "文件说明", allowReuse: true);

        Assert.Equal(ValidationCodes.PersistenceFailed, resolved.Code);
        Assert.True(resolved.Persisted);
        var id = Assert.Single(resolved.CreatedIds!);
        Assert.Equal(id, resolved.NodeId);
        Assert.True(await data.ExistsAsync(id));
        Assert.Equal(group.Id, Assert.Single(data.ParentUpdates)[id]);
        Assert.Empty(group.GetDescendants<KeySenderCategory>());
    }

    [Fact]
    public async Task Create_reports_a_persisted_node_when_parent_update_fails()
    {
        using var designMode = DesignModeScope.Enter();
        var (group, service, data) = CreateHarness();
        data.FailParentUpdate = true;

        var result = await service.CreateAsync(SampleWrite());

        Assert.Equal(ValidationCodes.PersistenceFailed, result.Code);
        Assert.True(result.Persisted);
        var id = Assert.Single(result.CreatedIds!);
        Assert.Equal(id, result.NodeId);
        Assert.True(await data.ExistsAsync(id));
        Assert.Equal(group.Id, Assert.Single(data.ParentUpdates)[id]);
        Assert.Empty(group.GetDescendants<KeySenderNode>());

        data.FailParentUpdate = false;
        Assert.True((await service.CreateAsync(SampleWrite())).Ok);
        Assert.Single(group.GetDescendants<KeySenderNode>());
    }

    [Fact]
    public async Task Copy_builtin_into_category_creates_an_editable_user_node_with_fresh_event_ids()
    {
        using var designMode = DesignModeScope.Enter();
        var (group, service, data) = CreateHarness();
        var category = new KeySenderCategory("destination");
        category.SetParent(group);
        var source = Assert.IsType<NodeDetail>((await service.GetDetailAsync(KeySenderConst.BuiltInCopyNodeId)).Detail);

        var copied = await service.CopyToCategoryAsync(source.NodeId, category.Id);

        Assert.True(copied.Ok);
        var detail = Assert.IsType<NodeDetail>(copied.Detail);
        Assert.True(detail.CanEdit);
        Assert.False(BuiltInKeyCatalog.IsBuiltIn(detail.NodeId));
        Assert.Equal(source.Name + KeySenderConst.CopyNameSuffix, detail.Name);
        Assert.Equal(source.Sequence.Events.Select(EventValues), detail.Sequence.Events.Select(EventValues));
        var originalIds = source.Sequence.Events.Select(row => row.EventId).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(detail.Sequence.Events, row => originalIds.Contains(row.EventId));
        Assert.Equal(detail.NodeId, Assert.Single(category.GetDescendants<KeySenderNode>()).Id);
        Assert.Equal(category.Id, Assert.Single(data.ParentUpdates)[detail.NodeId]);

        static (string Type, string Key, int Delay) EventValues(KeyboardEvent row) =>
            (row.Type, row.Key, row.DelayBeforeMs);
    }

    [Fact]
    public async Task Move_validates_the_whole_batch_before_any_parent_update()
    {
        using var designMode = DesignModeScope.Enter();
        var (group, service, data) = CreateHarness();
        var category = new KeySenderCategory("destination");
        category.SetParent(group);
        var existing = new KeySenderNode("exists");
        existing.SetParent(group);

        var moved = await service.MoveNodesAsync([existing.Id, "missing"], category.Id);

        Assert.Equal(ValidationCodes.NodeNotFound, moved.Code);
        Assert.Same(group, existing.Parent);
        Assert.Empty(data.ParentUpdates);
    }

    [Fact]
    public async Task Move_deduplicates_node_ids_and_updates_live_parents_only_after_persistence_succeeds()
    {
        using var designMode = DesignModeScope.Enter();
        var (group, service, data) = CreateHarness();
        var category = new KeySenderCategory("destination");
        category.SetParent(group);
        var first = new KeySenderNode("first");
        first.SetParent(group);
        var second = new KeySenderNode("second");
        second.SetParent(group);
        var selection = new[] { first.Id, second.Id, $" {first.Id} " };
        data.FailParentUpdate = true;

        Assert.Equal(ValidationCodes.PersistenceFailed, (await service.MoveNodesAsync(selection, category.Id)).Code);
        Assert.Same(group, first.Parent);
        Assert.Same(group, second.Parent);

        data.FailParentUpdate = false;
        Assert.True((await service.MoveNodesAsync(selection, category.Id)).Ok);
        Assert.Same(category, first.Parent);
        Assert.Same(category, second.Parent);
        var parentMap = data.ParentUpdates.Last();
        Assert.Equal(2, parentMap.Count);
        Assert.Equal(category.Id, parentMap[first.Id]);
        Assert.Equal(category.Id, parentMap[second.Id]);
    }

    [Fact]
    public async Task Delete_category_removes_its_user_nodes_and_leaves_other_nodes_untouched()
    {
        using var designMode = DesignModeScope.Enter();
        var (group, service, data) = CreateHarness();
        var category = await service.CreateCategoryAsync("工作");
        var child = await service.CreateInCategoryAsync(SampleWrite(), category.NodeId);
        var survivor = await service.CreateAsync(SampleWrite());

        var removed = await service.DeleteCategoryAsync(category.NodeId!);

        Assert.True(removed.Ok);
        Assert.Equal(child.NodeId, Assert.Single(removed.DeletedIds!));
        Assert.Empty(group.GetDescendants<KeySenderCategory>());
        Assert.Equal(survivor.NodeId, Assert.Single(group.GetDescendants<KeySenderNode>()).Id);
        Assert.False(await data.ExistsAsync(category.NodeId!));
        Assert.False(await data.ExistsAsync(child.NodeId!));
        Assert.True(await data.ExistsAsync(survivor.NodeId!));
    }

    [Theory]
    [InlineData("category")]
    [InlineData("search-group")]
    [InlineData("outside-tree")]
    [InlineData("missing")]
    public async Task Delete_batch_rejects_non_owned_sequence_ids_before_any_write(string target)
    {
        using var designMode = DesignModeScope.Enter();
        var (group, service, data) = CreateHarness();
        var category = await service.CreateCategoryAsync("工作");
        var user = await service.CreateInCategoryAsync(SampleWrite(), category.NodeId);
        var outside = await service.CreateAsync(SampleWrite());
        group.FindFirstDescendant<KeySenderNode>(node => node.Id == outside.NodeId)!.SetParent(null);
        var rejectedId = target switch
        {
            "category" => category.NodeId!,
            "search-group" => group.Id,
            "outside-tree" => outside.NodeId!,
            _ => "missing-node",
        };

        var rejected = await service.DeleteAsync([user.NodeId!, rejectedId]);

        Assert.Equal(ValidationCodes.NodeNotFound, rejected.Code);
        Assert.Equal(rejectedId, rejected.NodeId);
        Assert.Empty(data.DeleteAttempts);
        Assert.True(await data.ExistsAsync(user.NodeId!));
        Assert.Equal(user.NodeId, Assert.Single(group.GetDescendants<KeySenderNode>()).Id);
    }

    [Fact]
    public async Task Delete_node_rejects_unexpected_descendants_before_host_can_cascade()
    {
        using var designMode = DesignModeScope.Enter();
        var (group, service, data) = CreateHarness();
        var id = (await service.CreateAsync(SampleWrite())).NodeId!;
        var node = Assert.Single(group.GetDescendants<KeySenderNode>());
        new ForeignNode("foreign-child").SetParent(node);

        var rejected = await service.DeleteAsync([id]);

        Assert.Equal(ValidationCodes.InvalidConfiguration, rejected.Code);
        Assert.Equal(id, rejected.NodeId);
        Assert.Empty(data.DeleteAttempts);
        Assert.True(await data.ExistsAsync(id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delete_category_rejects_unexpected_children_before_deleting_any_nodes(bool builtInChild)
    {
        using var designMode = DesignModeScope.Enter();
        var (group, service, data) = CreateHarness();
        var categoryId = (await service.CreateCategoryAsync("工作")).NodeId!;
        var childId = (await service.CreateInCategoryAsync(SampleWrite(), categoryId)).NodeId!;
        var category = Assert.Single(group.GetDescendants<KeySenderCategory>());
        MioObject unexpected = builtInChild
            ? new KeySenderNode(KeySenderConst.BuiltInCopyNodeId)
            : new ForeignNode("foreign-child");
        unexpected.SetParent(category);

        var rejected = await service.DeleteCategoryAsync(categoryId);

        Assert.Equal(ValidationCodes.InvalidCategory, rejected.Code);
        Assert.Empty(data.DeleteAttempts);
        Assert.True(await data.ExistsAsync(categoryId));
        Assert.True(await data.ExistsAsync(childId));
        Assert.Same(category, unexpected.Parent);
    }

    [Fact]
    public async Task Delete_valid_batch_removes_only_selected_nodes_and_deduplicates_ids()
    {
        using var designMode = DesignModeScope.Enter();
        var (group, service, data) = CreateHarness();
        var categoryId = (await service.CreateCategoryAsync("工作")).NodeId!;
        var categorizedId = (await service.CreateInCategoryAsync(SampleWrite(), categoryId)).NodeId!;
        var uncategorizedId = (await service.CreateAsync(SampleWrite())).NodeId!;
        var survivor = (await service.CreateAsync(SampleWrite())).NodeId!;

        var deleted = await service.DeleteAsync([categorizedId, uncategorizedId, categorizedId]);

        Assert.True(deleted.Ok);
        Assert.Equal(new[] { categorizedId, uncategorizedId }, deleted.DeletedIds);
        Assert.Equal(deleted.DeletedIds, data.DeleteAttempts);
        Assert.Equal(survivor, Assert.Single(group.GetDescendants<KeySenderNode>()).Id);
        Assert.True(await data.ExistsAsync(categoryId));
        Assert.True(await data.ExistsAsync(survivor));
        Assert.False(await data.ExistsAsync(categorizedId));
        Assert.False(await data.ExistsAsync(uncategorizedId));
    }

    [Fact]
    public async Task Partial_node_delete_detaches_only_verified_deleted_nodes_and_reports_remaining_ids()
    {
        using var designMode = DesignModeScope.Enter();
        var (group, service, data) = CreateHarness();
        var deleted = (await service.CreateAsync(SampleWrite())).NodeId!;
        var failed = (await service.CreateAsync(SampleWrite())).NodeId!;
        var unattempted = (await service.CreateAsync(SampleWrite())).NodeId!;
        data.FailedDeleteIds.Add(failed);

        var result = await service.DeleteAsync([deleted, failed, unattempted]);

        Assert.Equal(ValidationCodes.DeleteIncomplete, result.Code);
        Assert.Equal(deleted, Assert.Single(result.DeletedIds!));
        Assert.Equal(new[] { failed, unattempted }, result.RemainingIds);
        Assert.True(new HashSet<string> { failed, unattempted }.SetEquals(
            group.GetDescendants<KeySenderNode>().Select(node => node.Id)));
    }

    private static (KeySenderSearchGroup Group, ConfigurationService Service, RecordingMioDataProvider Data) CreateHarness()
    {
        var plugin = new KeySenderPlugin();
        var group = new KeySenderSearchGroup(KeySenderConst.SearchGroupId);
        group.SetParent(plugin);
        var data = new RecordingMioDataProvider();
        return (group, new ConfigurationService(data, plugin), data);
    }

    private static NodeWrite SampleWrite() => new()
    {
        Name = "Tap A",
        Sequence = new KeyboardSequence
        {
            Events =
            [
                new KeyboardEvent { EventId = "press-a", Key = "KeyA", Type = "keyDown" },
                new KeyboardEvent { EventId = "release-a", Key = "KeyA", Type = "keyUp" },
            ],
        },
    };

    private sealed class ForeignNode(string id) : MioObject(id)
    {
        public override Guid MioType { get; } = new("940b087a-c4ae-4a3b-9040-d63df7e3f7c4");
    }
}
