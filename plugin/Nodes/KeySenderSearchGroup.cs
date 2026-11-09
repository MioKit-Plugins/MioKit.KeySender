namespace KeySender;

/// <summary>
/// Search group hosting the manager entry and KeySender nodes.
/// </summary>
[EavType(KeySenderConst.SearchGroupTypeId)]
public class KeySenderSearchGroup : MioObject, ISearchableFeature
{
    public KeySenderSearchGroup(string id) : base(id)
    {
        Name = "模拟按键";
        Description = "搜索并执行按键序列节点";
        PreloadPropertySource =
        [
            ..PreloadPropertySource,
            AliasNameExtension.AliasNameProperty,
            PinnedExtension.IsPinnedProperty,
            PinnedExtension.PinnedOrderProperty,
            HotkeyExtension.HotkeyProperty,
        ];
    }

    public override Guid MioType => KeySenderConst.SearchGroupTypeGuid;

    public ValueTask SearchAsync(
        SearchRequest request,
        IWriteOnlyResultList<SearchResult> results,
        CancellationToken ct)
    {
        try
        {
            if (request.IsEmptyText)
            {
                return ValueTask.CompletedTask;
            }

            var text = request.SearchText!;
            foreach (var node in this.GetDescendants<KeySenderNode>())
            {
                ct.ThrowIfCancellationRequested();
                
                var matched = node.TryMatch(text, out var match, out var title);
                if(!matched) continue;
                results.Add(new SearchResult(request, node)
                {
                    Title = !string.IsNullOrEmpty(title)
                        ? title
                        : (node.GetName() ?? string.Empty),
                    Description = node.GetDescription(),
                    MatchResult = matched && match is not null ? match.Value : default,
                    PinMode = node.GetIsPinned() == true ? SortPriority.Top : SortPriority.Normal,
                    Source = "模拟按键",
                });
            }

            return ValueTask.CompletedTask;
        }
        catch (Exception exception)
        {
            return ValueTask.FromException(exception);
        }
    }
    
}
