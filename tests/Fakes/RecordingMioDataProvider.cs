using KeySender;
using MioKit.Sdk;

namespace MioKit.KeySender.Tests.Fakes;

/// <summary>
/// Minimal data provider that mirrors host QueryObjectByTypeAsync behavior:
/// each query returns fresh MioObject instances (not live tree nodes).
/// </summary>
internal sealed class RecordingMioDataProvider : IMioDataProvider
{
    private readonly List<string> _categoryIds;
    private readonly List<string> _nodeIds;
    private readonly Dictionary<string, string> _categoryNames;

    public int QueryCategoryByTypeCount { get; private set; }

    public int SaveObjectsCount { get; private set; }

    public Dictionary<string, Dictionary<IAttachEavProperty, object>> SavedValues { get; } = new(StringComparer.Ordinal);

    public List<IReadOnlyDictionary<string, string?>> ParentUpdates { get; } = [];

    public bool FailParentUpdate { get; set; }

    public HashSet<string> FailedDeleteIds { get; } = new(StringComparer.Ordinal);

    public List<string> DeleteAttempts { get; } = [];

    public RecordingMioDataProvider(
        IEnumerable<string>? categoryIds = null,
        IEnumerable<string>? nodeIds = null,
        IReadOnlyDictionary<string, string>? categoryNames = null)
    {
        _categoryIds = (categoryIds ?? []).ToList();
        _nodeIds = (nodeIds ?? []).ToList();
        _categoryNames = categoryNames is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(categoryNames, StringComparer.Ordinal);
    }

    public ValueTask<T?> GetValueAsync<T>(MioObject mioObject, IEavProperty<T> property)
        => ValueTask.FromResult(default(T));

    public ValueTask<Dictionary<IAttachEavProperty, object?>> GetValuesAsync(
        MioObject mioObject,
        IEnumerable<IAttachEavProperty> properties)
        => ValueTask.FromResult(new Dictionary<IAttachEavProperty, object?>());

    public ValueTask SetValueAsync<T>(MioObject mioObject, IEavProperty<T> property, T? value)
        => ValueTask.CompletedTask;

    public ValueTask SetValueAsync(
        MioObject mioObject,
        Dictionary<IAttachEavProperty, object> valueDictionary)
        => ValueTask.CompletedTask;

    public ValueTask SetValuesAsync(
        Dictionary<MioObject, Dictionary<IAttachEavProperty, object>> objectValues)
        => ValueTask.CompletedTask;

    public Task BatchLoadValueAsync(MioObject mioObject, HashSet<IAttachEavProperty> properties)
    {
        ApplyBatchLoad(mioObject, properties);
        return Task.CompletedTask;
    }

    public Task BatchLoadValueAsync(
        IReadOnlyCollection<MioObject> mioObjects,
        HashSet<IAttachEavProperty> properties)
    {
        foreach (var mioObject in mioObjects)
        {
            ApplyBatchLoad(mioObject, properties);
        }

        return Task.CompletedTask;
    }

    private void ApplyBatchLoad(MioObject mioObject, HashSet<IAttachEavProperty> properties)
    {
        if (mioObject is not KeySenderCategory category)
        {
            return;
        }

        if (!properties.Contains(MioFeatureExtensions.NameProperty))
        {
            return;
        }

        if (_categoryNames.TryGetValue(category.Id, out var name))
        {
            category.SetName(name);
        }
    }

    public Task<bool> StoreAsync<T>(T mioObject) where T : MioObject
        => Task.FromResult(true);

    public Task<int> StoreAsync<T>(IEnumerable<MioObject> mioObjects) where T : MioObject
        => Task.FromResult(0);

    public Task<int> StoreManyAsync(IReadOnlyCollection<MioObject> objects)
        => Task.FromResult(0);

    public Task SaveObjectsAsync(
        IReadOnlyCollection<MioObject> objects,
        Dictionary<MioObject, Dictionary<IAttachEavProperty, object>>? propertyValues = null)
    {
        SaveObjectsCount++;
        if (propertyValues is not null)
        {
            foreach (var (node, values) in propertyValues)
            {
                if (!SavedValues.TryGetValue(node.Id, out var saved))
                {
                    saved = [];
                    SavedValues.Add(node.Id, saved);
                }

                foreach (var (property, value) in values)
                {
                    saved[property] = value;
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string objectId)
    {
        DeleteAttempts.Add(objectId);
        if (FailedDeleteIds.Contains(objectId))
        {
            return Task.FromException(new IOException("Simulated delete failure."));
        }

        _categoryIds.Remove(objectId);
        _nodeIds.Remove(objectId);
        SavedValues.Remove(objectId);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(IEnumerable<string> mioObjectIds) => Task.CompletedTask;

    public async Task DeleteManyAsync(IReadOnlyCollection<string> objectIds)
    {
        foreach (var id in objectIds)
        {
            await DeleteAsync(id);
        }
    }

    public Task DeleteByTypeAsync(Guid mioType) => Task.CompletedTask;

    public Task<T> QueryObjectAsync<T>(string objectId, bool isPreload = false, bool isLoadChild = false)
        where T : MioObject
    {
        var created = (T)Activator.CreateInstance(typeof(T), objectId)!;
        return Task.FromResult(created);
    }

    public Task<IReadOnlyCollection<T>> QueryObjectByTypeAsync<T>(Guid mioType, bool isPreload = false)
        where T : MioObject
    {
        if (typeof(T) == typeof(KeySenderCategory))
        {
            QueryCategoryByTypeCount++;
            IReadOnlyCollection<T> cats = _categoryIds
                .Select(id => (T)(object)new KeySenderCategory(id))
                .ToList();
            return Task.FromResult(cats);
        }

        if (typeof(T) == typeof(KeySenderNode))
        {
            IReadOnlyCollection<T> nodes = _nodeIds
                .Select(id => (T)(object)new KeySenderNode(id))
                .ToList();
            return Task.FromResult(nodes);
        }

        return Task.FromResult<IReadOnlyCollection<T>>([]);
    }

    public Task<IReadOnlyCollection<T>> QueryObjectByTypeAsync<T>(
        Guid mioType,
        PageRequest page,
        bool isPreload = false)
        where T : MioObject
        => QueryObjectByTypeAsync<T>(mioType, isPreload);

    public Task<IReadOnlyCollection<T>> QueryChildrenAsync<T>(string parentId, bool isPreload = false)
        where T : MioObject
        => Task.FromResult<IReadOnlyCollection<T>>([]);

    public Task<IReadOnlyCollection<MioObject>> QueryDescendantsAsync(string objectId, bool isPreload = false)
        => Task.FromResult<IReadOnlyCollection<MioObject>>([]);

    public Task<bool> ExistsAsync(string objectId)
        => Task.FromResult(
            _categoryIds.Contains(objectId, StringComparer.Ordinal)
            || _nodeIds.Contains(objectId, StringComparer.Ordinal)
            || SavedValues.ContainsKey(objectId));

    public Task<int> CountByTypeAsync(Guid mioType) => Task.FromResult(0);

    public Task UpdateParentAsync(string objectId, string? parentId) => Task.CompletedTask;

    public Task UpdateParentsAsync(IReadOnlyDictionary<string, string?> parentMap)
    {
        ParentUpdates.Add(new Dictionary<string, string?>(parentMap, StringComparer.Ordinal));
        return FailParentUpdate
            ? Task.FromException(new IOException("Simulated parent relation failure."))
            : Task.CompletedTask;
    }

    public Task<PagedResult<T>> SearchAsync<T>(EavQuery query) where T : MioObject
        => Task.FromResult(PagedResult<T>.Empty());

    public Task<int> CountAsync(EavQuery query) => Task.FromResult(0);

    public Task<PagedResult<T>> SearchByPropertyAsync<T>(
        Guid mioType,
        IAttachEavProperty property,
        EavFilterOperator @operator,
        object? value,
        PageRequest? page = null)
        where T : MioObject
        => Task.FromResult(PagedResult<T>.Empty());

    public Task<IReadOnlyCollection<T>> QueryByRawSqlAsync<T>(string sql, object? parameters = null)
        where T : MioObject
        => Task.FromResult<IReadOnlyCollection<T>>([]);
}
