using KeySender.BuiltIn;
using KeySender.Services;

namespace KeySender;

/// <summary>
/// Result-menu placeholders. UI-backed edit/view/export defer to later packages;
/// copy/delete call ConfigurationService when available.
/// </summary>
internal static class KeySenderNodeActions
{
    public static ValueTask<IEnumerable<ISearchResultAction>> BuildAsync(KeySenderNode node)
    {
        var isPinned = node.GetIsPinned() == true;
        var actions = new List<ISearchResultAction>();

        if (BuiltInKeyCatalog.IsBuiltIn(node.Id))
        {
            actions.Add(new DeferredUiAction("查看", "查看内置节点详情（UI 阶段待实现）"));
            actions.Add(new CopyNodeAction("复制为自定义"));
            actions.Add(new DeferredUiAction("导出", "导出（Transfer 阶段待实现）"));
        }
        else
        {
            actions.Add(new DeferredUiAction("编辑", "编辑节点（UI 阶段待实现）"));
            actions.Add(new CopyNodeAction("复制"));
            actions.Add(new DeleteNodeAction());
        }

        actions.Add(isPinned ? CancelPinAction.Instance : PinAction.Instance);
        actions.Add(AliasNameAction.Instance);
        actions.Add(HotKeyAction.Instance);

        return ValueTask.FromResult<IEnumerable<ISearchResultAction>>(actions);
    }

    private sealed class DeferredUiAction : SearchResultActionBase
    {
        private readonly string _message;

        public DeferredUiAction(string text, string message)
        {
            Text = text;
            Group = "模拟按键";
            _message = message;
        }

        public override Task ExecuteAsync(
            SearchResult result,
            InvokeContext context,
            CancellationToken cancellationToken)
            => throw new NotSupportedException(_message);
    }

    private sealed class CopyNodeAction : SearchResultActionBase
    {
        public CopyNodeAction(string text)
        {
            Text = text;
            Group = "模拟按键";
        }

        public override async Task ExecuteAsync(
            SearchResult result,
            InvokeContext context,
            CancellationToken cancellationToken)
        {
            var config = KeySenderRegister.Instance.ComponentContext.Resolve<ConfigurationService>();
            var copy = await config.CopyAsync(result.OwnerObject.Id, acknowledgedWarningFingerprint: null, cancellationToken)
                .ConfigureAwait(false);
            if (!copy.Ok && copy.Code == Validation.ValidationCodes.SequenceConfirmationRequired)
            {
                // Search-result path cannot show the confirm dialog yet; re-submit with fingerprint.
                copy = await config.CopyAsync(
                        result.OwnerObject.Id,
                        copy.Analysis!.WarningFingerprint,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

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
