namespace KeySender.Models;

/// <summary>
/// Native key identity used for drain ownership (VK + scan + extended).
/// </summary>
public readonly record struct PhysicalKeyIdentity(ushort VkCode, ushort ScanCode, bool Extended)
{
    public static PhysicalKeyIdentity From(RawKeyboardEvent ev)
        => new(ev.VkCode, ev.ScanCode, ev.Extended);
}
