using KeySender.Models;
using KeySender.Services;
using KeySender.Validation;

namespace MioKit.KeySender.Tests.Fakes;

public sealed class FakeConfigurationService : IConfigurationService
{
    public List<NodeWrite> Created { get; } = [];

    public Func<NodeWrite, Task<ConfigurationResult>>? CreateHandler { get; set; }

    public Func<NodeWrite, string, Task<ConfigurationResult>>? CreateInCategoryHandler { get; set; }

    public Func<string, string, bool, Task<ConfigurationResult>>? ResolveImportCategoryHandler { get; set; }

    public Dictionary<string, NodeDetail> Details { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, CategorySnapshot> Categories { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<ConfigurationResult> CreateAsync(
        NodeWrite write,
        CancellationToken cancellationToken = default) =>
        CreateInCategoryAsync(write, null, cancellationToken);

    public Task<ConfigurationResult> CreateInCategoryAsync(
        NodeWrite write,
        string? categoryId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (categoryId is not null && CreateInCategoryHandler is not null)
        {
            return CreateInCategoryHandler(write, categoryId);
        }

        if (CreateHandler is not null)
        {
            return CreateHandler(write);
        }

        var blocked = ConfigHelpers.EvaluateSaveGate(write);
        if (blocked is not null)
        {
            return Task.FromResult(blocked);
        }

        CategorySnapshot? category = null;
        if (categoryId is not null && !Categories.TryGetValue(categoryId, out category))
        {
            return Task.FromResult(ConfigurationResult.Fail(ValidationCodes.CategoryNotFound, nodeId: categoryId));
        }

        var id = Guid.NewGuid().ToString("D").ToUpperInvariant();
        Created.Add(write);
        var detail = ConfigHelpers.ToDetail(id, write, isBuiltIn: false, category);
        Details[id] = detail;
        return Task.FromResult(ConfigurationResult.Success(id, detail, category));
    }

    public Task<ConfigurationResult> ResolveImportCategoryAsync(
        string name,
        string description,
        bool allowReuse,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ResolveImportCategoryHandler is not null)
        {
            return ResolveImportCategoryHandler(name, description, allowReuse);
        }

        if (allowReuse)
        {
            var matches = Categories.Values.Where(category =>
                string.Equals(category.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 1)
            {
                return Task.FromResult(ConfigurationResult.Success(matches[0].Id));
            }
        }

        var id = Guid.NewGuid().ToString("D").ToUpperInvariant();
        Categories.Add(id, new CategorySnapshot(id, name.Trim(), description.Trim()));
        return Task.FromResult(ConfigurationResult.Success(id, persisted: true));
    }

    public Task<ConfigurationResult> GetDetailAsync(
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Details.TryGetValue(nodeId, out var detail))
        {
            var category = detail.CategoryId is null ? null : Categories.GetValueOrDefault(detail.CategoryId);
            return Task.FromResult(ConfigurationResult.Success(nodeId, detail, category));
        }

        return Task.FromResult(ConfigurationResult.Fail(ValidationCodes.NodeNotFound, nodeId: nodeId));
    }
}
