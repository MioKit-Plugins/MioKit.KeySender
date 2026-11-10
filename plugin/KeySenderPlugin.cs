using KeySender.Views;

namespace KeySender;

public class KeySenderPlugin : PluginBase, IPluginConfigWindowFeature,
    ISearchableFeature, IInvokeFeature, IInvocationPresentationFeature,IUseFeature
{
    public KeySenderPlugin() : base(KeySenderConst.PluginId)
    {
        Name = "模拟按键";
        Description = "录制并回放有序键盘事件序列";
    }

    public override Guid MioType => KeySenderConst.PluginType;

    public void OpenPluginConfigWindow()
        => WindowManager.ShowUniqueWindow<PluginWebView>(Id, Id);

    /// <summary>
    /// 加载固定 Id 搜索组，确保内置节点就绪，并装载用户分类（不自动创建默认分类）。
    /// 打开管理窗由插件自身 Search/Invoke 承担，不再挂独立管理入口节点。
    /// </summary>
    protected override async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        var container = Container ?? throw new InvalidOperationException("Plugin container is not initialized.");
        var data = container.Resolve<IMioDataProvider>();
        var config = container.Resolve<Services.ConfigurationService>();

        await EnsureSearchGroupAsync(data, cancellationToken).ConfigureAwait(false);
        await RemoveLegacyManagerNodeAsync(data).ConfigureAwait(false);
        await config.EnsureBuiltInsAsync(cancellationToken).ConfigureAwait(false);
        await config.EnsureCategoriesAndMigrateAsync(cancellationToken).ConfigureAwait(false);

        Context.Logger.Information("模拟按键插件已启动");
    }

    protected override Task StopCoreAsync(CancellationToken cancellationToken)
    {
        Context.Logger.Information("模拟按键插件已停止");
        return Task.CompletedTask;
    }

    public override ValueTask<PluginDataCleanupPlan> CreateDataCleanupPlanAsync(
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new PluginDataCleanupPlan
        {
            AdditionalEavRootObjectIds = [],
        });
    }

    public ValueTask SearchAsync(
        SearchRequest request,
        IWriteOnlyResultList<SearchResult> writeOnlyResultList,
        CancellationToken cancellationToken)
    {
        if (request.IsEmptySearchText)
        {
            return ValueTask.CompletedTask;
        }

        if (!this.TryMatch(request.SearchText!, out var result, out var title))
        {
            return ValueTask.CompletedTask;
        }

        writeOnlyResultList.Add(new SearchResult(request, this)
        {
            Title = title,
            Description = Description,
            MatchResult = result!.Value,
            PinMode = SortPriority.Normal,
            Source = "模拟按键",
        });

        return ValueTask.CompletedTask;
    }

    public async Task InvokeAsync(InvokeContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        OpenPluginConfigWindow();
        await this.IncreaseAsync().ConfigureAwait(false);
    }

    public InvocationPresentation GetPresentation(InvocationPresentationRequest request) => new()
    {
        Interaction = InvocationInteraction.CompleteBeforeExecution,
    };

    private async Task EnsureSearchGroupAsync(IMioDataProvider data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existed = await data.ExistsAsync(KeySenderConst.SearchGroupId).ConfigureAwait(false);
        await EnsureTreeLoadedAsync<KeySenderSearchGroup>(KeySenderConst.SearchGroupId)
            .ConfigureAwait(false);

        if (existed)
        {
            return;
        }

        var group = this.FindFirstDescendant<KeySenderSearchGroup>(
            g => g.Id == KeySenderConst.SearchGroupId)
            ?? throw new InvalidOperationException("Search group missing after EnsureTreeLoadedAsync.");

        group.Name = "模拟按键";
        group.Description = "搜索并执行按键序列节点";
        await data.SaveObjectsAsync(
                [group],
                new Dictionary<MioObject, Dictionary<IAttachEavProperty, object>>
                {
                    [group] = new()
                    {
                        [MioFeatureExtensions.NameProperty] = group.Name,
                        [MioFeatureExtensions.DescriptionProperty] = group.Description,
                    },
                })
            .ConfigureAwait(false);
    }

    private async Task RemoveLegacyManagerNodeAsync(IMioDataProvider data)
    {
        var group = this.FindFirstDescendant<KeySenderSearchGroup>(
            g => g.Id == KeySenderConst.SearchGroupId);
        var legacy = group?.Children.FirstOrDefault(x => x.Id == KeySenderConst.ManagerNodeId);
        if (legacy is not null)
        {
            legacy.SetParent(null);
        }

        try
        {
            await data.DeleteAsync(KeySenderConst.ManagerNodeId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Context.Logger.Debug(
                ex,
                "Failed to delete legacy manager node {ManagerNodeId}.",
                KeySenderConst.ManagerNodeId);
        }
    }
}
