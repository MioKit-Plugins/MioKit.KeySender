namespace KeySender.Models;

/// <summary>
/// Decoded WH_KEYBOARD_LL payload (design 03 §2).
/// </summary>
public sealed class RawKeyboardEvent
{
    public ushort VkCode { get; init; }

    public ushort ScanCode { get; init; }

    public bool Extended { get; init; }

    public bool IsKeyUp { get; init; }

    public bool IsSystemKey { get; init; }

    public bool Injected { get; init; }

    public bool LowerIntegrityInjected { get; init; }

    public long Timestamp { get; init; }

    public nuint ExtraInfo { get; init; }
}
