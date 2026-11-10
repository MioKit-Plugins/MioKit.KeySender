using KeySender.BuiltIn;
using KeySender.Models;
using KeySender.Validation;

namespace KeySender.Services;

/// <summary>
/// Config CRUD gate: validate structure, persist, parent, publish.
/// </summary>
public sealed class ConfigurationService : IConfigurationService
{
    private readonly IMioDataProvider _data;
    private readonly KeySenderPlugin? _plugin;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <param name="plugin">
    /// Required for create/parent/tree recovery. May be null in unit tests that only exercise
    /// early builtin/validation rejection paths.
    /// </param>
    public ConfigurationService(IMioDataProvider data, KeySenderPlugin? plugin)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _plugin = plugin;
    }

    public Task<ConfigurationResult> CreateAsync(
        NodeWrite write,
        CancellationToken cancellationToken = default)
        => CreateInCategoryAsync(write, categoryId: null, cancellationToken);

    public async Task<ConfigurationResult> CreateInCategoryAsync(
        NodeWrite write,
        string? categoryId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        NormalizeWrite(write);

        var blocked = ConfigHelpers.EvaluateSaveGate(write);
        if (blocked is not null)
        {
            return blocked;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check after entering the write gate.
            blocked = ConfigHelpers.EvaluateSaveGate(write);
            if (blocked is not null)
            {
                return blocked;
            }

            var parentResult = ResolveNodeParent(categoryId);
            if (!parentResult.Ok || parentResult.Parent is null)
            {
                return ConfigurationResult.Fail(
                    parentResult.Code ?? ValidationCodes.CategoryNotFound,
                    parentResult.Message);
            }

            var parent = parentResult.Parent;
            var id = Guid.NewGuid().ToString("D").ToUpperInvariant();
            var node = new KeySenderNode(id);
            // Name/Description only on the unpublished create object; cold fields go via property dict.
            node.SetName(write.Name);
            node.SetDescription(write.Description);

            try
            {
                await _data.SaveObjectsAsync(
                        [node],
                        new Dictionary<MioObject, Dictionary<IAttachEavProperty, object>>
                        {
                            [node] = BuildBusinessPropertyValues(write),
                        })
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return ConfigurationResult.Fail(
                    ValidationCodes.PersistenceFailed,
                    ex.Message);
            }

            try
            {
                await _data.UpdateParentsAsync(
                        new Dictionary<string, string?> { [id] = parent.Id })
                    .ConfigureAwait(false);
                PublishCommittedValues(node, write);
                node.SetParent(parent);
            }
            catch (Exception ex)
            {
                return new ConfigurationResult
                {
                    Ok = false,
                    Code = ValidationCodes.PersistenceFailed,
                    Message = ex.Message,
                    Persisted = true,
                    CreatedIds = [id],
                    NodeId = id,
                };
            }

            return ConfigurationResult.Success(id, ConfigHelpers.ToDetail(id, write, isBuiltIn: false));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ConfigurationResult> UpdateAsync(
        string nodeId,
        NodeWrite write,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(write);
        NormalizeWrite(write);

        var builtin = ConfigHelpers.RejectIfBuiltIn(nodeId);
        if (builtin is not null)
        {
            return builtin;
        }

        var blocked = ConfigHelpers.EvaluateSaveGate(write);
        if (blocked is not null)
        {
            return blocked;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            builtin = ConfigHelpers.RejectIfBuiltIn(nodeId);
            if (builtin is not null)
            {
                return builtin;
            }

            blocked = ConfigHelpers.EvaluateSaveGate(write);
            if (blocked is not null)
            {
                return blocked;
            }

            var live = await ResolveLiveUserNodeAsync(nodeId, cancellationToken).ConfigureAwait(false);
            if (live is null)
            {
                return ConfigurationResult.Fail(ValidationCodes.NodeNotFound, $"Node not found: {nodeId}", nodeId: nodeId);
            }

            // Keep live parent. Do not draft-Set cold EAV onto the live node before commit,
            // and do not overwrite alias/hotkey/pin.
            try
            {
                await _data.SaveObjectsAsync(
                        [live],
                        new Dictionary<MioObject, Dictionary<IAttachEavProperty, object>>
                        {
                            [live] = BuildBusinessPropertyValues(write),
                        })
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return ConfigurationResult.Fail(ValidationCodes.PersistenceFailed, ex.Message, nodeId: nodeId);
            }

            PublishCommittedValues(live, write);
            return ConfigurationResult.Success(live.Id, ConfigHelpers.ToDetail(live.Id, write, isBuiltIn: false));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ConfigurationResult> DeleteAsync(
        IReadOnlyList<string> nodeIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);
        if (nodeIds.Count == 0)
        {
            return ConfigurationResult.Success();
        }

        var builtin = ConfigHelpers.RejectIfAnyBuiltIn(nodeIds);
        if (builtin is not null)
        {
            return builtin;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            builtin = ConfigHelpers.RejectIfAnyBuiltIn(nodeIds);
            if (builtin is not null)
            {
                return builtin;
            }

            var ids = nodeIds.Distinct(StringComparer.Ordinal).ToArray();
            try
            {
                await _data.DeleteManyAsync(ids).ConfigureAwait(false);
            }
            catch (Exception)
            {
                var deleted = new List<string>();
                var remaining = new List<string>();
                foreach (var id in ids)
                {
                    var exists = await _data.ExistsAsync(id).ConfigureAwait(false);
                    if (exists)
                    {
                        remaining.Add(id);
                    }
                    else
                    {
                        deleted.Add(id);
                    }
                }

                DetachFromTree(deleted);
                return new ConfigurationResult
                {
                    Ok = false,
                    Code = ValidationCodes.DeleteIncomplete,
                    Message = "DeleteManyAsync failed; verified remaining ids.",
                    DeletedIds = deleted,
                    RemainingIds = remaining,
                };
            }

            DetachFromTree(ids);
            return new ConfigurationResult
            {
                Ok = true,
                DeletedIds = ids,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<ConfigurationResult> CopyAsync(
        string sourceNodeId,
        CancellationToken cancellationToken = default)
        => CopyToCategoryAsync(sourceNodeId, categoryId: null, cancellationToken);

    public async Task<ConfigurationResult> CopyToCategoryAsync(
        string sourceNodeId,
        string? categoryId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNodeId);

        var detailResult = await GetDetailAsync(sourceNodeId, cancellationToken).ConfigureAwait(false);
        if (!detailResult.Ok || detailResult.Detail is null)
        {
            return detailResult.Ok
                ? ConfigurationResult.Fail(ValidationCodes.NodeNotFound, $"Node not found: {sourceNodeId}", nodeId: sourceNodeId)
                : detailResult;
        }

        var source = detailResult.Detail.ToWrite();
        source.Name = ConfigHelpers.BuildCopyName(source.Name);
        source.Sequence = BuiltinSequenceFactory.CloneWithFreshEventIds(source.Sequence);

        return await CreateInCategoryAsync(
                source,
                categoryId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ConfigurationResult> GetDetailAsync(
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        cancellationToken.ThrowIfCancellationRequested();

        if (BuiltInKeyCatalog.TryGet(nodeId, out var recipe))
        {
            var write = BuiltinSequenceFactory.CreateWrite(recipe);
            return ConfigurationResult.Success(
                recipe.Id,
                ConfigHelpers.ToDetail(recipe.Id, write, isBuiltIn: true));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var node = await ResolveLiveUserNodeAsync(nodeId, cancellationToken).ConfigureAwait(false);
            if (node is null)
            {
                var queried = await _data.QueryObjectAsync<KeySenderNode>(nodeId, isPreload: false, isLoadChild: false)
                    .ConfigureAwait(false);
                node = queried;
            }

            if (node is null)
            {
                return ConfigurationResult.Fail(ValidationCodes.NodeNotFound, $"Node not found: {nodeId}", nodeId: nodeId);
            }

            var write = await ReadUserWriteAsync(node).ConfigureAwait(false);
            if (write is null)
            {
                return ConfigurationResult.Fail(
                    ValidationCodes.InvalidConfiguration,
                    "User node is missing sequence configuration.",
                    nodeId: node.Id);
            }

            return ConfigurationResult.Success(node.Id, ConfigHelpers.ToDetail(node.Id, write, isBuiltIn: false));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Idempotently ensure the three built-in instances exist under the search group.
    /// </summary>
    public async Task EnsureBuiltInsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var group = RequireSearchGroup();
            foreach (var recipe in BuiltInKeyCatalog.All)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await EnsureOneBuiltInAsync(group, recipe, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Load user categories under the search group and attach orphan user nodes as uncategorized
    /// (directly under the search group). Does not invent a default category.
    /// </summary>
    public async Task EnsureCategoriesAndMigrateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var group = RequireSearchGroup();

            // QueryObjectByTypeAsync always creates fresh instances — never SetParent those
            // when a live tree node with the same Id already exists under the search group.
            var categories = await _data.QueryObjectByTypeAsync<KeySenderCategory>(
                    KeySenderConst.CategoryTypeGuid,
                    isPreload: false)
                .ConfigureAwait(false);

            var light =
                new HashSet<IAttachEavProperty>
                {
                    MioFeatureExtensions.NameProperty,
                    MioFeatureExtensions.DescriptionProperty,
                };

            foreach (var queried in categories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var live = group.FindFirstDescendant<KeySenderCategory>(c =>
                               string.Equals(c.Id, queried.Id, StringComparison.Ordinal))
                           ?? queried;

                if (live.Parent?.Id != group.Id)
                {
                    await _data.UpdateParentsAsync(
                            new Dictionary<string, string?> { [live.Id] = group.Id })
                        .ConfigureAwait(false);
                }

                await _data.BatchLoadValueAsync(live, light).ConfigureAwait(false);
                if (live.Parent != group)
                {
                    live.SetParent(group);
                }
            }

            var nodes = await _data.QueryObjectByTypeAsync<KeySenderNode>(
                    KeySenderConst.KeySenderNodeTypeGuid,
                    isPreload: false)
                .ConfigureAwait(false);

            var nodeLight =
                new HashSet<IAttachEavProperty>
                {
                    MioFeatureExtensions.NameProperty,
                    MioFeatureExtensions.DescriptionProperty,
                    AliasNameExtension.AliasNameProperty,
                    PinnedExtension.IsPinnedProperty,
                    PinnedExtension.PinnedOrderProperty,
                    HotkeyExtension.HotkeyProperty,
                };

            foreach (var queried in nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (BuiltInKeyCatalog.IsBuiltIn(queried.Id))
                {
                    continue;
                }

                var live = group.FindFirstDescendant<KeySenderNode>(n =>
                               string.Equals(n.Id, queried.Id, StringComparison.Ordinal))
                           ?? queried;

                var parentCategory = live.Parent as KeySenderCategory;
                var belongsToKnownCategory = parentCategory is not null
                    && parentCategory.Parent == group;
                var underSearchGroup = ReferenceEquals(live.Parent, group);

                if (!belongsToKnownCategory && !underSearchGroup)
                {
                    // Uncategorized: park under search group; never invent a default category.
                    await _data.UpdateParentsAsync(
                            new Dictionary<string, string?> { [live.Id] = group.Id })
                        .ConfigureAwait(false);
                    live.SetParent(group);
                }

                await _data.BatchLoadValueAsync(live, nodeLight).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ConfigurationResult> CreateCategoryAsync(
        string name,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = (name ?? string.Empty).Trim();
        if (normalizedName.Length is < KeySenderConst.NameMinLength or > KeySenderConst.NameMaxLength)
        {
            return ConfigurationResult.Fail(ValidationCodes.InvalidName, "分类名称不合法");
        }

        var normalizedDescription = (description ?? string.Empty).Trim();
        if (normalizedDescription.Length > KeySenderConst.DescriptionMaxLength)
        {
            return ConfigurationResult.Fail(ValidationCodes.InvalidDescription, "分类说明过长");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var group = RequireSearchGroup();
            var id = Guid.NewGuid().ToString("D").ToUpperInvariant();
            var category = new KeySenderCategory(id);
            category.SetName(normalizedName);
            category.SetDescription(normalizedDescription);

            try
            {
                await _data.SaveObjectsAsync(
                        [category],
                        new Dictionary<MioObject, Dictionary<IAttachEavProperty, object>>
                        {
                            [category] = new()
                            {
                                [MioFeatureExtensions.NameProperty] = normalizedName,
                                [MioFeatureExtensions.DescriptionProperty] = normalizedDescription,
                            },
                        })
                    .ConfigureAwait(false);
                await _data.UpdateParentsAsync(
                        new Dictionary<string, string?> { [id] = group.Id })
                    .ConfigureAwait(false);
                category.SetParent(group);
            }
            catch (Exception ex)
            {
                return ConfigurationResult.Fail(ValidationCodes.PersistenceFailed, ex.Message);
            }

            return ConfigurationResult.Success(id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ConfigurationResult> RenameCategoryAsync(
        string categoryId,
        string name,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);
        var normalizedName = (name ?? string.Empty).Trim();
        if (normalizedName.Length is < KeySenderConst.NameMinLength or > KeySenderConst.NameMaxLength)
        {
            return ConfigurationResult.Fail(ValidationCodes.InvalidName, "分类名称不合法");
        }

        var normalizedDescription = description?.Trim();
        if (normalizedDescription is not null
            && normalizedDescription.Length > KeySenderConst.DescriptionMaxLength)
        {
            return ConfigurationResult.Fail(ValidationCodes.InvalidDescription, "分类说明过长");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var category = FindCategory(categoryId.Trim());
            if (category is null)
            {
                return ConfigurationResult.Fail(
                    ValidationCodes.CategoryNotFound,
                    $"Category not found: {categoryId}",
                    nodeId: categoryId);
            }

            category.SetName(normalizedName);
            if (normalizedDescription is not null)
            {
                category.SetDescription(normalizedDescription);
            }

            try
            {
                var props = new Dictionary<IAttachEavProperty, object>
                {
                    [MioFeatureExtensions.NameProperty] = normalizedName,
                };
                if (normalizedDescription is not null)
                {
                    props[MioFeatureExtensions.DescriptionProperty] = normalizedDescription;
                }

                await _data.SaveObjectsAsync(
                        [category],
                        new Dictionary<MioObject, Dictionary<IAttachEavProperty, object>>
                        {
                            [category] = props,
                        })
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return ConfigurationResult.Fail(
                    ValidationCodes.PersistenceFailed,
                    ex.Message,
                    nodeId: category.Id);
            }

            return ConfigurationResult.Success(category.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ConfigurationResult> DeleteCategoryAsync(
        string categoryId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var category = FindCategory(categoryId.Trim());
            if (category is null)
            {
                return ConfigurationResult.Fail(
                    ValidationCodes.CategoryNotFound,
                    $"Category not found: {categoryId}",
                    nodeId: categoryId);
            }

            var childIds = category.GetDescendants<KeySenderNode>()
                .Where(static n => !BuiltInKeyCatalog.IsBuiltIn(n.Id))
                .Select(static n => n.Id)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            try
            {
                if (childIds.Length > 0)
                {
                    await _data.DeleteManyAsync(childIds).ConfigureAwait(false);
                    DetachFromTree(childIds);
                }

                await _data.DeleteAsync(category.Id).ConfigureAwait(false);
                category.SetParent(null);
            }
            catch (Exception ex)
            {
                return new ConfigurationResult
                {
                    Ok = false,
                    Code = ValidationCodes.DeleteIncomplete,
                    Message = ex.Message,
                    DeletedIds = childIds,
                    NodeId = category.Id,
                };
            }

            return new ConfigurationResult
            {
                Ok = true,
                NodeId = categoryId,
                DeletedIds = childIds,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ConfigurationResult> MoveNodesAsync(
        IReadOnlyList<string> nodeIds,
        string targetCategoryId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetCategoryId);

        var builtin = ConfigHelpers.RejectIfAnyBuiltIn(nodeIds);
        if (builtin is not null)
        {
            return builtin;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            builtin = ConfigHelpers.RejectIfAnyBuiltIn(nodeIds);
            if (builtin is not null)
            {
                return builtin;
            }

            var category = FindCategory(targetCategoryId.Trim());
            if (category is null)
            {
                return ConfigurationResult.Fail(
                    ValidationCodes.CategoryNotFound,
                    $"Category not found: {targetCategoryId}",
                    nodeId: targetCategoryId);
            }

            var ids = nodeIds
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Select(static id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (ids.Length == 0)
            {
                return ConfigurationResult.Success();
            }

            var parentMap = new Dictionary<string, string?>(StringComparer.Ordinal);
            var liveNodes = new List<KeySenderNode>();
            foreach (var id in ids)
            {
                var node = await ResolveLiveUserNodeAsync(id, cancellationToken).ConfigureAwait(false);
                if (node is null || BuiltInKeyCatalog.IsBuiltIn(node.Id))
                {
                    return ConfigurationResult.Fail(
                        ValidationCodes.NodeNotFound,
                        $"Node not found: {id}",
                        nodeId: id);
                }

                parentMap[id] = category.Id;
                liveNodes.Add(node);
            }

            try
            {
                await _data.UpdateParentsAsync(parentMap).ConfigureAwait(false);
                foreach (var node in liveNodes)
                {
                    node.SetParent(category);
                }
            }
            catch (Exception ex)
            {
                return ConfigurationResult.Fail(ValidationCodes.PersistenceFailed, ex.Message);
            }

            return ConfigurationResult.Success();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureOneBuiltInAsync(
        KeySenderSearchGroup group,
        BuiltInKeyRecipe recipe,
        CancellationToken cancellationToken)
    {
        var existingInTree = group.FindFirstDescendant<KeySenderNode>(n =>
            string.Equals(n.Id, recipe.Id, StringComparison.Ordinal));

        if (existingInTree is not null)
        {
            if (existingInTree.MioType != KeySenderConst.KeySenderNodeTypeGuid)
            {
                throw new InvalidOperationException(
                    $"{ValidationCodes.BuiltinIdentityConflict}: id {recipe.Id} has unexpected type {existingInTree.MioType}.");
            }

            existingInTree.SetName(recipe.Name);
            existingInTree.SetDescription(recipe.Description);
            await _data.SaveObjectsAsync(
                    [existingInTree],
                    new Dictionary<MioObject, Dictionary<IAttachEavProperty, object>>
                    {
                        [existingInTree] = new()
                        {
                            [MioFeatureExtensions.NameProperty] = recipe.Name,
                            [MioFeatureExtensions.DescriptionProperty] = recipe.Description,
                        },
                    })
                .ConfigureAwait(false);
            return;
        }

        var existing = await _data.QueryObjectAsync<KeySenderNode>(recipe.Id, isPreload: false, isLoadChild: false)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            if (existing.MioType != KeySenderConst.KeySenderNodeTypeGuid)
            {
                throw new InvalidOperationException(
                    $"{ValidationCodes.BuiltinIdentityConflict}: id {recipe.Id} occupied by type {existing.MioType}.");
            }

            existing.SetName(recipe.Name);
            existing.SetDescription(recipe.Description);
            await _data.SaveObjectsAsync(
                    [existing],
                    new Dictionary<MioObject, Dictionary<IAttachEavProperty, object>>
                    {
                        [existing] = new()
                        {
                            [MioFeatureExtensions.NameProperty] = recipe.Name,
                            [MioFeatureExtensions.DescriptionProperty] = recipe.Description,
                        },
                    })
                .ConfigureAwait(false);

            await _data.UpdateParentsAsync(new Dictionary<string, string?> { [recipe.Id] = group.Id })
                .ConfigureAwait(false);

            await _data.BatchLoadValueAsync(
                    existing,
                    new HashSet<IAttachEavProperty>
                    {
                        MioFeatureExtensions.NameProperty,
                        MioFeatureExtensions.DescriptionProperty,
                        AliasNameExtension.AliasNameProperty,
                        PinnedExtension.IsPinnedProperty,
                        PinnedExtension.PinnedOrderProperty,
                        HotkeyExtension.HotkeyProperty,
                    })
                .ConfigureAwait(false);

            if (existing.Parent != group)
            {
                existing.SetParent(group);
            }

            return;
        }

        var created = new KeySenderNode(recipe.Id);
        created.SetName(recipe.Name);
        created.SetDescription(recipe.Description);
        await _data.SaveObjectsAsync(
                [created],
                new Dictionary<MioObject, Dictionary<IAttachEavProperty, object>>
                {
                    [created] = new()
                    {
                        [MioFeatureExtensions.NameProperty] = recipe.Name,
                        [MioFeatureExtensions.DescriptionProperty] = recipe.Description,
                    },
                })
            .ConfigureAwait(false);
        await _data.UpdateParentsAsync(new Dictionary<string, string?> { [recipe.Id] = group.Id })
            .ConfigureAwait(false);
        created.SetParent(group);
    }

    private KeySenderSearchGroup RequireSearchGroup()
    {
        if (_plugin is null)
        {
            throw new InvalidOperationException("KeySenderPlugin is required for tree operations.");
        }

        var group = _plugin.FindFirstDescendant<KeySenderSearchGroup>(
            g => g.Id == KeySenderConst.SearchGroupId);
        if (group is null)
        {
            throw new InvalidOperationException(
                "KeySender search group is not loaded. Call EnsureTreeLoadedAsync first.");
        }

        return group;
    }

    private sealed class NodeParentResult
    {
        public bool Ok { get; init; }

        public string? Code { get; init; }

        public string? Message { get; init; }

        /// <summary>Target parent: a user category, or the search group for uncategorized nodes.</summary>
        public MioObject? Parent { get; init; }

        public static NodeParentResult Success(MioObject parent) => new()
        {
            Ok = true,
            Parent = parent,
        };

        public static NodeParentResult Fail(string code, string message) => new()
        {
            Ok = false,
            Code = code,
            Message = message,
        };
    }

    /// <summary>
    /// Resolve create/copy parent. Explicit categoryId must exist; null/empty → search group (uncategorized).
    /// </summary>
    private NodeParentResult ResolveNodeParent(string? categoryId)
    {
        var group = RequireSearchGroup();
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            return NodeParentResult.Success(group);
        }

        var category = FindCategory(categoryId.Trim());
        if (category is null)
        {
            return NodeParentResult.Fail(
                ValidationCodes.CategoryNotFound,
                $"Category not found: {categoryId}");
        }

        return NodeParentResult.Success(category);
    }

    private KeySenderCategory? FindCategory(string categoryId)
    {
        var group = _plugin?.FindFirstDescendant<KeySenderSearchGroup>(
            g => g.Id == KeySenderConst.SearchGroupId);
        return group?.FindFirstDescendant<KeySenderCategory>(c =>
            string.Equals(c.Id, categoryId, StringComparison.OrdinalIgnoreCase));
    }

    private Task<KeySenderNode?> ResolveLiveUserNodeAsync(string nodeId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var group = _plugin?.FindFirstDescendant<KeySenderSearchGroup>(
            g => g.Id == KeySenderConst.SearchGroupId);
        var node = group?.FindFirstDescendant<KeySenderNode>(n =>
            string.Equals(n.Id, nodeId, StringComparison.Ordinal));
        return Task.FromResult(node);
    }

    private void DetachFromTree(IEnumerable<string> ids)
    {
        var group = _plugin?.FindFirstDescendant<KeySenderSearchGroup>(
            g => g.Id == KeySenderConst.SearchGroupId);
        if (group is null)
        {
            return;
        }

        foreach (var id in ids)
        {
            var node = group.FindFirstDescendant<KeySenderNode>(n =>
                string.Equals(n.Id, id, StringComparison.Ordinal));
            node?.SetParent(null);
        }
    }

    private static void NormalizeWrite(NodeWrite write)
    {
        write.Name = write.Name?.Trim() ?? string.Empty;
        write.Description ??= string.Empty;
        write.Sequence ??= new KeyboardSequence { Version = KeySenderConst.SequenceVersion };
        write.Sequence.Events ??= [];
        if (write.Sequence.Version == 0)
        {
            write.Sequence.Version = KeySenderConst.SequenceVersion;
        }
    }

    private static void PublishCommittedValues(KeySenderNode node, NodeWrite write)
    {
        // SaveObjectsAsync commits its explicit property dictionary to storage. The attached
        // node may still expose old Store entries, including fields with CachePolicy.None.
        // Publish only after persistence succeeds so a failed save never changes live values.
        node.SetName(write.Name);
        node.SetDescription(write.Description);
        node.SetBeforeDelayMs(write.BeforeDelayMs);
        node.SetAfterDelayMs(write.AfterDelayMs);
        // The live value represents committed data, not the caller's mutable request DTO.
        node.SetSequence(new KeyboardSequence
        {
            Version = write.Sequence.Version,
            Events = write.Sequence.Events.Select(ev => new KeyboardEvent
            {
                EventId = ev.EventId,
                Type = ev.Type,
                Key = ev.Key,
                DelayBeforeMs = ev.DelayBeforeMs,
            }).ToList(),
        });
    }

    private static Dictionary<IAttachEavProperty, object> BuildBusinessPropertyValues(NodeWrite write)
        => new()
        {
            [MioFeatureExtensions.NameProperty] = write.Name,
            [MioFeatureExtensions.DescriptionProperty] = write.Description,
            [KeySenderExtension.BeforeDelayMsProperty] = write.BeforeDelayMs,
            [KeySenderExtension.AfterDelayMsProperty] = write.AfterDelayMs,
            [KeySenderExtension.SequenceProperty] = write.Sequence,
        };

    private static async Task<NodeWrite?> ReadUserWriteAsync(KeySenderNode node)
    {
        var before = await node.GetBeforeDelayMsAsync().ConfigureAwait(false) ?? 0;
        var after = await node.GetAfterDelayMsAsync().ConfigureAwait(false) ?? 0;
        var sequence = await node.GetSequenceAsync().ConfigureAwait(false);
        if (sequence is null)
        {
            return null;
        }

        return new NodeWrite
        {
            Name = node.GetName() ?? string.Empty,
            Description = node.GetDescription() ?? string.Empty,
            BeforeDelayMs = before,
            AfterDelayMs = after,
            Sequence = sequence,
        };
    }
}
