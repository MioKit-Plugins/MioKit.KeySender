using System.Reflection;
using Avalonia.Controls;
using KeySender;
using KeySender.Services;
using MioKit.KeySender.Tests.Fakes;
using MioKit.Sdk;
using Xunit;

namespace MioKit.KeySender.Tests;

public sealed class ConfigurationServiceEnsureCategoriesTests
{
    [Fact]
    public async Task EnsureCategories_does_not_create_any_category_when_none_exist()
    {
        using var designScope = DesignModeScope.Enter();

        var plugin = new KeySenderPlugin();
        var group = new KeySenderSearchGroup(KeySenderConst.SearchGroupId);
        group.SetParent(plugin);

        var data = new RecordingMioDataProvider(categoryIds: []);
        var service = new ConfigurationService(data, plugin);

        await service.EnsureCategoriesAndMigrateAsync();

        Assert.Empty(group.GetDescendants<KeySenderCategory>());
        Assert.Equal(0, data.SaveObjectsCount);
    }

    [Fact]
    public async Task EnsureCategories_does_not_duplicate_user_category_when_query_returns_fresh_instance()
    {
        using var designScope = DesignModeScope.Enter();

        var plugin = new KeySenderPlugin();
        var group = new KeySenderSearchGroup(KeySenderConst.SearchGroupId);
        group.SetParent(plugin);

        var categoryId = Guid.NewGuid().ToString("D").ToUpperInvariant();
        var live = new KeySenderCategory(categoryId);
        live.SetName("办公");
        live.SetParent(group);

        Assert.Single(group.GetDescendants<KeySenderCategory>());

        var data = new RecordingMioDataProvider(categoryIds: [categoryId]);
        var service = new ConfigurationService(data, plugin);

        await service.EnsureCategoriesAndMigrateAsync();

        var categories = group.GetDescendants<KeySenderCategory>().ToList();
        Assert.Equal(1, data.QueryCategoryByTypeCount);
        Assert.Single(categories);
        Assert.Same(live, categories[0]);
    }

    [Fact]
    public void KeySenderCategory_ctor_does_not_seed_placeholder_name()
    {
        using var designScope = DesignModeScope.Enter();

        var category = new KeySenderCategory(Guid.NewGuid().ToString("D").ToUpperInvariant());

        Assert.True(string.IsNullOrEmpty(category.GetName()));
        Assert.NotEqual("分类", category.GetName());
    }

    [Fact]
    public async Task EnsureCategories_batch_loads_persisted_name_onto_live_category()
    {
        using var designScope = DesignModeScope.Enter();

        var plugin = new KeySenderPlugin();
        var group = new KeySenderSearchGroup(KeySenderConst.SearchGroupId);
        group.SetParent(plugin);

        var categoryId = Guid.NewGuid().ToString("D").ToUpperInvariant();
        var live = new KeySenderCategory(categoryId);
        live.SetParent(group);

        Assert.True(string.IsNullOrEmpty(live.GetName()));

        var data = new RecordingMioDataProvider(
            categoryIds: [categoryId],
            categoryNames: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [categoryId] = "办公",
            });
        var service = new ConfigurationService(data, plugin);

        await service.EnsureCategoriesAndMigrateAsync();

        Assert.Same(live, Assert.Single(group.GetDescendants<KeySenderCategory>()));
        Assert.Equal("办公", live.GetName());
    }

    /// <summary>
    /// Forces Avalonia design-mode so MioObject uses DesignMioStore and SetParent skips EventBus.
    /// </summary>
    private sealed class DesignModeScope : IDisposable
    {
        private readonly object? _previous;
        private readonly MethodInfo? _setter;
        private readonly FieldInfo? _field;

        private DesignModeScope(MethodInfo? setter, FieldInfo? field, object? previous)
        {
            _setter = setter;
            _field = field;
            _previous = previous;
        }

        public static DesignModeScope Enter()
        {
            var property = typeof(Design).GetProperty(
                               nameof(Design.IsDesignMode),
                               BindingFlags.Public | BindingFlags.Static)
                           ?? throw new InvalidOperationException("Design.IsDesignMode not found.");

            var previous = property.GetValue(null);
            var setter = property.GetSetMethod(nonPublic: true);
            FieldInfo? field = null;
            if (setter is null)
            {
                field = typeof(Design)
                    .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
                    .FirstOrDefault(f =>
                        f.FieldType == typeof(bool)
                        && f.Name.Contains("DesignMode", StringComparison.OrdinalIgnoreCase));
                if (field is null)
                {
                    throw new InvalidOperationException(
                        "Cannot force Design.IsDesignMode for category tree unit test.");
                }
            }

            var scope = new DesignModeScope(setter, field, previous);
            scope.Set(true);
            return scope;
        }

        public void Dispose() => Set(_previous is bool b && b);

        private void Set(bool value)
        {
            if (_setter is not null)
            {
                _setter.Invoke(null, [value]);
                return;
            }

            _field!.SetValue(null, value);
        }
    }
}
