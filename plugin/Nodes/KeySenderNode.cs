using KeySender.BuiltIn;
using KeySender.Services;

namespace KeySender;

/// <summary>
/// User or built-in key-sequence node.
/// </summary>
[EavType(KeySenderConst.KeySenderNodeTypeId)]
public class KeySenderNode : MioObject,
    IKeySenderNodeFeature,
    IAliasNameFeature,
    IHotkeyFeature,
    IPinnedFeature,
    IResultActionProviderFeature,
    IInvocationPresentationFeature,
    IRequireTargetWindowFeature
{
    public KeySenderNode(string id) : base(id)
    {
        // Light fields only — Sequence / delays stay cold (EavCachePolicy.None, not preloaded).
        PreloadPropertySource =
        [
            ..PreloadPropertySource,
            AliasNameExtension.AliasNameProperty,
            PinnedExtension.IsPinnedProperty,
            PinnedExtension.PinnedOrderProperty,
            HotkeyExtension.HotkeyProperty,
            RecentExtension.UseCountProperty,
            RecentExtension.LastUseTimeProperty,
        ];
    }

    public override Guid MioType => KeySenderConst.KeySenderNodeTypeGuid;

    public bool IsBuiltIn => BuiltInKeyCatalog.IsBuiltIn(Id);

    public InvocationPresentation GetPresentation(InvocationPresentationRequest request) => new()
    {
        Interaction = InvocationInteraction.CompleteBeforeExecution,
    };

    public ValueTask<IEnumerable<ISearchResultAction>> GetActionAsync(SearchResult searchResult)
        => KeySenderNodeActions.BuildAsync(this);

    public async Task InvokeAsync(InvokeContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        var execution = KeySenderRegister.Instance.ComponentContext.Resolve<IKeySenderExecutionService>();
        await execution.ExecuteAsync(this, context).ConfigureAwait(false);
        await this.IncreaseAsync();
    }
}
