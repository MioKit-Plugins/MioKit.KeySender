using System.Reflection;
using Avalonia.Controls;
using KeySender;
using KeySender.Models;
using KeySender.Services;
using KeySender.Validation;
using MioKit.Sdk;
using Xunit;

namespace MioKit.KeySender.Tests;

[CollectionDefinition("Configuration persistence", DisableParallelization = true)]
public sealed class ConfigurationPersistenceCollection;

[Collection("Configuration persistence")]
public sealed class ConfigurationServicePersistenceTests
{
    [Fact]
    public async Task Update_persists_all_business_properties_and_detail_reads_them_back()
    {
        using var designMode = DesignModeScope.Enter();
        var (service, node, provider) = CreateHarness();
        var write = EditedWrite();

        var saved = await service.UpdateAsync(node.Id, write);

        Assert.True(saved.Ok);
        Assert.Equal(1, provider.SaveCount);
        Assert.Equal("original", provider.NameWhenSaveStarted);
        Assert.Equal(5, provider.SavedProperties.Count);
        Assert.Contains(MioFeatureExtensions.NameProperty, provider.SavedProperties);
        Assert.Contains(MioFeatureExtensions.DescriptionProperty, provider.SavedProperties);
        Assert.Contains(KeySenderExtension.BeforeDelayMsProperty, provider.SavedProperties);
        Assert.Contains(KeySenderExtension.AfterDelayMsProperty, provider.SavedProperties);
        Assert.Contains(KeySenderExtension.SequenceProperty, provider.SavedProperties);

        // A storage boundary serializes the sequence instead of retaining the request object.
        write.Sequence.Events[0].Key = "KeyZ";
        write.BeforeDelayMs = 999;
        var persisted = provider.ReadPersistedWrite(node.Id);
        Assert.Equal("edited", persisted.Name);
        Assert.Equal("saved description", persisted.Description);
        Assert.Equal(123, persisted.BeforeDelayMs);
        Assert.Equal(456, persisted.AfterDelayMs);
        var persistedEvent = Assert.Single(persisted.Sequence.Events);
        Assert.Equal("edited-event", persistedEvent.EventId);
        Assert.Equal("KeyB", persistedEvent.Key);
        Assert.Equal(KeySenderConst.EventTypeKeyDown, persistedEvent.Type);
        Assert.Equal(789, persistedEvent.DelayBeforeMs);

        // Reading the same live instance must observe the successful commit. The provider does
        // not refresh node.Store: that is the application's responsibility after persistence.
        var loaded = await service.GetDetailAsync(node.Id);

        Assert.True(loaded.Ok);
        var detail = Assert.IsType<NodeDetail>(loaded.Detail);
        Assert.Equal("edited", detail.Name);
        Assert.Equal("saved description", detail.Description);
        Assert.Equal(123, detail.BeforeDelayMs);
        Assert.Equal(456, detail.AfterDelayMs);
        Assert.Equal(KeySenderConst.SequenceVersion, detail.Sequence.Version);
        var ev = Assert.Single(detail.Sequence.Events);
        Assert.Equal("edited-event", ev.EventId);
        Assert.Equal("KeyB", ev.Key);
        Assert.Equal(KeySenderConst.EventTypeKeyDown, ev.Type);
        Assert.Equal(789, ev.DelayBeforeMs);
    }

    [Fact]
    public async Task Failed_update_does_not_publish_draft_values_to_the_live_node()
    {
        using var designMode = DesignModeScope.Enter();
        var (service, node, provider) = CreateHarness();
        provider.FailSave = true;

        var failed = await service.UpdateAsync(node.Id, EditedWrite());

        Assert.False(failed.Ok);
        Assert.Equal(ValidationCodes.PersistenceFailed, failed.Code);
        Assert.Equal("original", provider.NameWhenSaveStarted);
        Assert.Equal("original", node.GetName());
        Assert.Equal("original description", node.GetDescription());
        var persisted = provider.ReadPersistedWrite(node.Id);
        Assert.Equal("original", persisted.Name);
        Assert.Equal("original description", persisted.Description);
        Assert.Equal(10, persisted.BeforeDelayMs);
        Assert.Equal(20, persisted.AfterDelayMs);
        Assert.Equal("original-event", Assert.Single(persisted.Sequence.Events).EventId);
        var loaded = await service.GetDetailAsync(node.Id);
        Assert.True(loaded.Ok);
        Assert.Equal(10, loaded.Detail!.BeforeDelayMs);
        Assert.Equal(20, loaded.Detail.AfterDelayMs);
        var ev = Assert.Single(loaded.Detail.Sequence.Events);
        Assert.Equal("original-event", ev.EventId);
        Assert.Equal("KeyA", ev.Key);
        Assert.Equal(KeySenderConst.EventTypeKeyUp, ev.Type);
        Assert.Equal(30, ev.DelayBeforeMs);
    }

    [Fact]
    public async Task Recreated_node_hydrated_from_persisted_values_reads_the_latest_configuration()
    {
        using var designMode = DesignModeScope.Enter();
        var (service, node, provider) = CreateHarness();
        Assert.True((await service.UpdateAsync(node.Id, EditedWrite())).Ok);

        var parent = node.Parent;
        node.SetParent(null);
        var recreated = provider.HydrateNode(node.Id);
        recreated.SetParent(parent);
        var loaded = await service.GetDetailAsync(node.Id);

        Assert.True(loaded.Ok);
        Assert.Equal("edited", loaded.Detail!.Name);
        Assert.Equal("saved description", loaded.Detail.Description);
        Assert.Equal(123, loaded.Detail.BeforeDelayMs);
        Assert.Equal(456, loaded.Detail.AfterDelayMs);
        Assert.Equal("KeyB", Assert.Single(loaded.Detail.Sequence.Events).Key);
        Assert.Equal(789, loaded.Detail.Sequence.Events[0].DelayBeforeMs);
    }

    private static (ConfigurationService Service, KeySenderNode Node, PersistenceProvider Provider) CreateHarness()
    {
        var plugin = new KeySenderPlugin();
        var group = new KeySenderSearchGroup(KeySenderConst.SearchGroupId);
        group.SetParent(plugin);
        var node = new KeySenderNode("persistence-test-node");
        node.SetName("original");
        node.SetDescription("original description");
        node.SetBeforeDelayMs(10);
        node.SetAfterDelayMs(20);
        node.SetSequence(new KeyboardSequence
        {
            Events = [new KeyboardEvent
            {
                EventId = "original-event", Key = "KeyA", Type = KeySenderConst.EventTypeKeyUp,
                DelayBeforeMs = 30,
            }],
        });
        node.SetParent(group);
        var data = DispatchProxy.Create<IMioDataProvider, PersistenceProvider>();
        var provider = (PersistenceProvider)(object)data;
        provider.SeedPersistedValues(node);
        return (new ConfigurationService(data, plugin), node, provider);
    }

    private static NodeWrite EditedWrite() => new()
    {
        Name = "edited", Description = "saved description", BeforeDelayMs = 123, AfterDelayMs = 456,
        Sequence = new KeyboardSequence
        {
            Events = [new KeyboardEvent
            {
                EventId = "edited-event", Key = "KeyB", Type = KeySenderConst.EventTypeKeyDown,
                DelayBeforeMs = 789,
            }],
        },
    };

    /// <summary>
    /// Stores explicit SaveObjectsAsync values separately from the SDK DesignMioStore cache.
    /// Successful persistence deliberately leaves live node.Store untouched, as a database write
    /// does. This exercises the application's cache publication path, not the host database engine.
    /// </summary>
    public class PersistenceProvider : DispatchProxy
    {
        public int SaveCount { get; private set; }
        public bool FailSave { get; set; }
        public string? NameWhenSaveStarted { get; private set; }
        public List<IAttachEavProperty> SavedProperties { get; } = [];
        private readonly Dictionary<string, Dictionary<IAttachEavProperty, object>> _persisted = [];

        public void SeedPersistedValues(KeySenderNode node)
        {
            _persisted[node.Id] = new()
            {
                [MioFeatureExtensions.NameProperty] = node.GetName()!,
                [MioFeatureExtensions.DescriptionProperty] = node.GetDescription()!,
                [KeySenderExtension.BeforeDelayMsProperty] = node.GetBeforeDelayMsAsync().GetAwaiter().GetResult()!.Value,
                [KeySenderExtension.AfterDelayMsProperty] = node.GetAfterDelayMsAsync().GetAwaiter().GetResult()!.Value,
                [KeySenderExtension.SequenceProperty] = KeyboardSequenceConvert.Instance.ConvertToStoreValue(
                    node.GetSequenceAsync().GetAwaiter().GetResult(), MioStoreType.LongText)!,
            };
        }

        public NodeWrite ReadPersistedWrite(string nodeId)
        {
            var values = _persisted[nodeId];
            return new NodeWrite
            {
                Name = (string)values[MioFeatureExtensions.NameProperty],
                Description = (string)values[MioFeatureExtensions.DescriptionProperty],
                BeforeDelayMs = (int)values[KeySenderExtension.BeforeDelayMsProperty],
                AfterDelayMs = (int)values[KeySenderExtension.AfterDelayMsProperty],
                Sequence = (KeyboardSequence)KeyboardSequenceConvert.Instance.ConvertToMemoryValue(
                    values[KeySenderExtension.SequenceProperty], MioStoreType.LongText)!,
            };
        }

        public KeySenderNode HydrateNode(string nodeId)
        {
            var saved = ReadPersistedWrite(nodeId);
            var node = new KeySenderNode(nodeId);
            node.SetName(saved.Name);
            node.SetDescription(saved.Description);
            node.SetBeforeDelayMs(saved.BeforeDelayMs);
            node.SetAfterDelayMs(saved.AfterDelayMs);
            node.SetSequence(saved.Sequence);
            return node;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IMioDataProvider.SaveObjectsAsync))
                throw new NotSupportedException(targetMethod?.Name);

            SaveCount++;
            var objectValues = Assert.IsType<Dictionary<MioObject, Dictionary<IAttachEavProperty, object>>>(args![1]);
            var (node, values) = Assert.Single(objectValues);
            NameWhenSaveStarted = node.GetName();
            SavedProperties.AddRange(values.Keys);
            if (FailSave) return Task.FromException(new IOException("Simulated transaction failure."));

            var persisted = new Dictionary<IAttachEavProperty, object>();
            foreach (var (property, value) in values)
            {
                var storedValue = value;
                if (ReferenceEquals(property, KeySenderExtension.SequenceProperty))
                {
                    var converter = KeyboardSequenceConvert.Instance;
                    storedValue = converter.ConvertToStoreValue(value, MioStoreType.LongText)!;
                }
                persisted[property] = storedValue;
            }
            _persisted[node.Id] = persisted;
            return Task.CompletedTask;
        }
    }

    private sealed class DesignModeScope : IDisposable
    {
        private readonly MethodInfo _setter;
        private readonly bool _previous;

        private DesignModeScope(MethodInfo setter, bool previous)
        {
            _setter = setter;
            _previous = previous;
        }

        public static DesignModeScope Enter()
        {
            var property = typeof(Design).GetProperty(nameof(Design.IsDesignMode))!;
            var setter = property.GetSetMethod(nonPublic: true)
                ?? throw new InvalidOperationException("Design mode setter is unavailable.");
            var scope = new DesignModeScope(setter, (bool)property.GetValue(null)!);
            setter.Invoke(null, [true]);
            return scope;
        }

        public void Dispose() => _setter.Invoke(null, [_previous]);
    }
}
