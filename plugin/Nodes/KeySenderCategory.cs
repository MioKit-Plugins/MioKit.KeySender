namespace KeySender;

/// <summary>
/// User-managed category folder under the search group. Holds user KeySenderNode children.
/// Do not assign Name/Description in the ctor — Absolute EAV cache would block BatchLoad after restart.
/// </summary>
[EavType(KeySenderConst.CategoryTypeId)]
public class KeySenderCategory : MioObject
{
    public KeySenderCategory(string id) : base(id)
    {
    }

    public override Guid MioType => KeySenderConst.CategoryTypeGuid;
}
