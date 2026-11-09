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

    public int QueryCategoryByTypeCount { get; private set; }

    public int SaveObjectsCount { get; private set; }

    public RecordingMioDataProvider(
        IEnumerable<string>? categoryIds = null,
        IEnumerable<string>? nodeIds = null)
    {
        _categoryIds = (categoryIds ?? []).ToList();
        _nodeIds = (nodeIds ?? []).ToList();
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
        => Task.CompletedTask;

    public Task BatchLoadValueAsync(
        IReadOnlyCollection<MioObject> mioObjects,
        HashSet<IAttachEavProperty> properties)
        => Task.CompletedTask;

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
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string objectId) => Task.CompletedTask;

    public Task DeleteAsync(IEnumerable<string> mioObjectIds) => Task.CompletedTask;

    public Task DeleteManyAsync(IReadOnlyCollection<string> objectIds) => Task.CompletedTask;

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
            || _nodeIds.Contains(objectId, StringComparer.Ordinal));

    public Task<int> CountByTypeAsync(Guid mioType) => Task.FromResult(0);

    public Task UpdateParentAsync(string objectId, string? parentId) => Task.CompletedTask;

    public Task UpdateParentsAsync(IReadOnlyDictionary<string, string?> parentMap)
        => Task.CompletedTask;

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
