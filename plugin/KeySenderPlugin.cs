using KeySender.Views;

namespace KeySender;

public class KeySenderPlugin : PluginBase, IPluginConfigWindowFeature
{
    public KeySenderPlugin() : base(KeySenderConst.PluginId)
    {
        Name = "模拟按键";
        Description = "录制并回放有序键盘事件序列";
    }

    public override Guid MioType => KeySenderConst.PluginType;

    public void OpenPluginConfigWindow()
        => WindowManager.ShowUniqueWindow<PluginWebView>(Id, Id);

    protected override async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        var container = Container ?? throw new InvalidOperationException("Plugin container is not initialized.");
        var data = container.Resolve<IMioDataProvider>();
        var config = container.Resolve<Services.ConfigurationService>();

        await EnsureSearchGroupAsync(data, cancellationToken).ConfigureAwait(false);
        await EnsureManagerNodeAsync(data, cancellationToken).ConfigureAwait(false);
        await config.EnsureBuiltInsAsync(cancellationToken).ConfigureAwait(false);
        await config.EnsureCategoriesAndMigrateAsync(cancellationToken).ConfigureAwait(false);

        Context.Logger.Information("模拟按键插件已启动");
    }

    protected override async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            var container = Container;
            if (container is not null
                && container.TryResolve<Services.IKeySenderExecutionService>(out var execution))
            {
                // Do not register a new IMioTaskService job here — stop drain may already be closed.
                await execution.ReleaseAllAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Context.Logger.Warning(ex, "模拟按键停止时释放保持键失败");
        }

        Context.Logger.Information("模拟按键插件已停止");
    }

    public override ValueTask<PluginDataCleanupPlan> CreateDataCleanupPlanAsync(
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new PluginDataCleanupPlan
        {
            AdditionalEavRootObjectIds = [],
        });
    }

    private async Task EnsureSearchGroupAsync(IMioDataProvider data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureTreeLoadedAsync<KeySenderSearchGroup>(KeySenderConst.SearchGroupId)
            .ConfigureAwait(false);

        var group = this.FindFirstDescendant<KeySenderSearchGroup>(
            g => g.Id == KeySenderConst.SearchGroupId);
        if (group is not null)
        {
            return;
        }

        group = new KeySenderSearchGroup(KeySenderConst.SearchGroupId);
        await data.SaveObjectsAsync(
                [group],
                new Dictionary<MioObject, Dictionary<IAttachEavProperty, object>>
                {
                    [group] = new()
                    {
                        [MioFeatureExtensions.NameProperty] = group.Name ?? "模拟按键",
                        [MioFeatureExtensions.DescriptionProperty] = group.Description ?? string.Empty,
                    },
                })
            .ConfigureAwait(false);
        await data.UpdateParentsAsync(new Dictionary<string, string?> { [group.Id] = Id })
            .ConfigureAwait(false);
        group.SetParent(this);
    }

    private async Task EnsureManagerNodeAsync(IMioDataProvider data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var group = this.FindFirstDescendant<KeySenderSearchGroup>(
            g => g.Id == KeySenderConst.SearchGroupId)
            ?? throw new InvalidOperationException("Search group missing before manager ensure.");

        if (group.FindFirstDescendant<OpenKeySenderManagerNode>(
                n => n.Id == KeySenderConst.ManagerNodeId) is not null)
        {
            return;
        }

        var existing = await data.QueryObjectAsync<OpenKeySenderManagerNode>(
                KeySenderConst.ManagerNodeId,
                isPreload: false,
                isLoadChild: false)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            await data.UpdateParentsAsync(
                    new Dictionary<string, string?> { [existing.Id] = group.Id })
                .ConfigureAwait(false);
            if (existing.Parent != group)
            {
                existing.SetParent(group);
            }

            return;
        }

        var manager = new OpenKeySenderManagerNode(KeySenderConst.ManagerNodeId);
        await data.SaveObjectsAsync(
                [manager],
                new Dictionary<MioObject, Dictionary<IAttachEavProperty, object>>
                {
                    [manager] = new()
                    {
                        [MioFeatureExtensions.NameProperty] = manager.Name ?? "模拟按键管理",
                        [MioFeatureExtensions.DescriptionProperty] = manager.Description ?? string.Empty,
                    },
                })
            .ConfigureAwait(false);
        await data.UpdateParentsAsync(
                new Dictionary<string, string?> { [manager.Id] = group.Id })
            .ConfigureAwait(false);
        manager.SetParent(group);
    }
}
