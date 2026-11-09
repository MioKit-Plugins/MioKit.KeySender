namespace KeySender;

/// <summary>
/// User-managed category folder under the search group. Holds user KeySenderNode children.
/// </summary>
[EavType(KeySenderConst.CategoryTypeId)]
public class KeySenderCategory : MioObject
{
    public KeySenderCategory(string id) : base(id)
    {
        Name = "分类";
        Description = string.Empty;
    }

    public override Guid MioType => KeySenderConst.CategoryTypeGuid;

    public bool IsDefault
        => string.Equals(Id, KeySenderConst.DefaultCategoryId, StringComparison.Ordinal);
}
