using KeySender.BuiltIn;
using KeySender.Services;
using Lucide.Avalonia;

namespace KeySender;

/// <summary>
/// Search-result Alt menu for KeySender nodes: view/edit open the manager,
/// export/copy/delete call host services.
/// </summary>
internal static class KeySenderNodeActions
{
    public static ValueTask<IEnumerable<ISearchResultAction>> BuildAsync(KeySenderNode node)
    {
        var isPinned = node.GetIsPinned() == true;
        var actions = new List<ISearchResultAction>();

        if (BuiltInKeyCatalog.IsBuiltIn(node.Id))
        {
            actions.Add(new OpenNodeAction("查看", LucideIconKind.Eye));
        }
        else
        {
            actions.Add(new OpenNodeAction("编辑", LucideIconKind.Pencil));
        }

        actions.Add(isPinned ? CancelPinAction.Instance : PinAction.Instance);
        actions.Add(AliasNameAction.Instance);
        actions.Add(HotKeyAction.Instance);

        return ValueTask.FromResult<IEnumerable<ISearchResultAction>>(actions);
    }

    private sealed class OpenNodeAction : SearchResultActionBase
    {
        public OpenNodeAction(string text, LucideIconKind icon)
        {
            Text = text;
            Group = "模拟按键";
            Icon = icon;
        }

        public override Task ExecuteAsync(
            SearchResult result,
            InvokeContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MioIoc.Resolve<ISearchBoxWindow>().ResetOverlay();
            var ui = KeySenderRegister.Instance.ComponentContext.Resolve<KeySenderUiService>();
            ui.RequestOpenNode(result.OwnerObject.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class ExportNodeAction : SearchResultActionBase
    {
        public ExportNodeAction()
        {
            Text = "导出";
            Group = "模拟按键";
            Icon = LucideIconKind.Download;
        }

        public override Task ExecuteAsync(
            SearchResult result,
            InvokeContext context,
            CancellationToken cancellationToken)
        {
            var ui = KeySenderRegister.Instance.ComponentContext.Resolve<KeySenderUiService>();
            return ui.ExportFromHostAsync([result.OwnerObject.Id], cancellationToken);
        }
    }

    private sealed class CopyNodeAction : SearchResultActionBase
    {
        public CopyNodeAction(string text)
        {
            Text = text;
            Group = "模拟按键";
            Icon = LucideIconKind.ClipboardCopy;
        }

        public override async Task ExecuteAsync(
            SearchResult result,
            InvokeContext context,
            CancellationToken cancellationToken)
        {
            var config = KeySenderRegister.Instance.ComponentContext.Resolve<ConfigurationService>();
            var copy = await config.CopyAsync(result.OwnerObject.Id, cancellationToken)
                .ConfigureAwait(false);
            if (!copy.Ok)
            {
                throw new InvalidOperationException(copy.Message ?? copy.Code ?? "Copy failed.");
            }
        }
    }

    private sealed class DeleteNodeAction : SearchResultActionBase
    {
        public DeleteNodeAction()
        {
            Text = "删除";
            Group = "模拟按键";
            Icon = LucideIconKind.Trash2;
        }

        public override async Task ExecuteAsync(
            SearchResult result,
            InvokeContext context,
            CancellationToken cancellationToken)
        {
            var config = KeySenderRegister.Instance.ComponentContext.Resolve<ConfigurationService>();
            var delete = await config.DeleteAsync([result.OwnerObject.Id], cancellationToken)
                .ConfigureAwait(false);
            if (!delete.Ok)
            {
                throw new InvalidOperationException(delete.Message ?? delete.Code ?? "Delete failed.");
            }
        }
    }
}
