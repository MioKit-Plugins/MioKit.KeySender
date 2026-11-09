namespace KeySender;

/// <summary>
/// Fixed manager entry under the search group. Opens the plugin config window.
/// </summary>
[EavType(KeySenderConst.ManagerNodeTypeId)]
public class OpenKeySenderManagerNode : MioObject, IInvokeFeature, IInvocationPresentationFeature,
    IAliasNameFeature, IHotkeyFeature, IPinnedFeature
{
    public OpenKeySenderManagerNode(string id) : base(id)
    {
        Name = "模拟按键管理";
        Description = "打开模拟按键配置窗口";
        PreloadPropertySource =
        [
            ..PreloadPropertySource,
            AliasNameExtension.AliasNameProperty,
            PinnedExtension.IsPinnedProperty,
            PinnedExtension.PinnedOrderProperty,
            HotkeyExtension.HotkeyProperty,
        ];
    }

    public override Guid MioType => KeySenderConst.ManagerNodeTypeGuid;

    public InvocationPresentation GetPresentation(InvocationPresentationRequest request) => new()
    {
        Interaction = InvocationInteraction.CompleteBeforeExecution,
    };

    public Task InvokeAsync(InvokeContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        var plugin = this.FindAncestor<KeySenderPlugin>()
                     ?? KeySenderRegister.Instance.ComponentContext.Resolve<KeySenderPlugin>();
        plugin.OpenPluginConfigWindow();
        return Task.CompletedTask;
    }
}
