namespace KeySender.Models;

/// <summary>
/// Closed first-release key token catalog (design 03 §1).
/// Tokens are case-sensitive stable ids; values are Windows VK / scan / extended.
/// </summary>
public static class KeyTokenCatalog
{
    private static readonly Dictionary<string, KeyTokenInfo> ByToken;
    private static readonly Dictionary<(ushort Vk, bool Extended), KeyTokenInfo> ByVkExtended;
    private static readonly Dictionary<ushort, KeyTokenInfo> NumpadByScan;

    static KeyTokenCatalog()
    {
        var list = BuildEntries();
        ByToken = new Dictionary<string, KeyTokenInfo>(list.Count, StringComparer.Ordinal);
        ByVkExtended = new Dictionary<(ushort, bool), KeyTokenInfo>(list.Count);
        NumpadByScan = new Dictionary<ushort, KeyTokenInfo>(16);

        foreach (var info in list)
        {
            ByToken.Add(info.Token, info);

            // Prefer first unique (vk, extended); NumpadEnter vs Enter already differ by extended.
            ByVkExtended.TryAdd((info.VirtualKey, info.Extended), info);

            if (info.Token.StartsWith("Numpad", StringComparison.Ordinal) && info.ScanCode != 0)
            {
                NumpadByScan.TryAdd(info.ScanCode, info);
            }
        }
    }

    public static IReadOnlyCollection<KeyTokenInfo> All => ByToken.Values;

    public static bool TryGet(string token, out KeyTokenInfo info)
    {
        if (token is null)
        {
            info = null!;
            return false;
        }

        return ByToken.TryGetValue(token, out info!);
    }

    public static bool Contains(string token) => token is not null && ByToken.ContainsKey(token);

    /// <summary>
    /// Resolve a raw Hook event to a catalog token (design 03 §1).
    /// Left/right modifiers use scan/extended; NumLock-off numpad uses non-extended scans.
    /// </summary>
    public static bool TryResolve(ushort vkCode, ushort scanCode, bool extended, out KeyTokenInfo info)
    {
        // Generic Shift / Control / Menu → left/right
        if (vkCode == 0x10) // VK_SHIFT
        {
            return TryGet(scanCode == 0x36 ? "RightShift" : "LeftShift", out info);
        }

        if (vkCode == 0x11) // VK_CONTROL
        {
            return TryGet(extended ? "RightControl" : "LeftControl", out info);
        }

        if (vkCode == 0x12) // VK_MENU
        {
            return TryGet(extended ? "RightAlt" : "LeftAlt", out info);
        }

        // NumLock off: navigation VKs arrive with non-extended numpad scans.
        if (!extended && NumpadByScan.TryGetValue(scanCode, out info!))
        {
            return true;
        }

        if (ByVkExtended.TryGetValue((vkCode, extended), out info!))
        {
            return true;
        }

        // Fallback: ignore extended mismatch for VK-only media/browser keys.
        foreach (var candidate in ByToken.Values)
        {
            if (candidate.VirtualKey == vkCode && candidate.ScanCode == 0)
            {
                info = candidate;
                return true;
            }
        }

        info = null!;
        return false;
    }

    private static List<KeyTokenInfo> BuildEntries()
    {
        var list = new List<KeyTokenInfo>(160);

        // KeyA–KeyZ: VK 0x41–0x5A
        for (var i = 0; i < 26; i++)
        {
            var ch = (char)('A' + i);
            list.Add(Entry($"Key{ch}", (ushort)(0x41 + i), ScanForLetter(ch), extended: false));
        }

        // Digit0–Digit9: 0x30–0x39
        for (var i = 0; i <= 9; i++)
        {
            list.Add(Entry($"Digit{i}", (ushort)(0x30 + i), ScanForDigit(i), extended: false));
        }

        // F1–F24: VK 0x70–0x87; F1–F12 use standard set-1 scans.
        ushort[] fScans =
        [
            0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x41, 0x42, 0x43, 0x44, 0x57, 0x58,
            0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x76,
        ];
        for (var i = 1; i <= 24; i++)
        {
            list.Add(Entry($"F{i}", (ushort)(0x6F + i), fScans[i - 1], extended: false));
        }

        // Modifiers
        list.Add(Entry("LeftShift", 0xA0, 0x2A, false));
        list.Add(Entry("RightShift", 0xA1, 0x36, false));
        list.Add(Entry("LeftControl", 0xA2, 0x1D, false));
        list.Add(Entry("RightControl", 0xA3, 0x1D, true));
        list.Add(Entry("LeftAlt", 0xA4, 0x38, false));
        list.Add(Entry("RightAlt", 0xA5, 0x38, true));
        list.Add(Entry("LeftWin", 0x5B, 0x5B, true));
        list.Add(Entry("RightWin", 0x5C, 0x5C, true));

        // Editing / lock / space
        list.Add(Entry("Backspace", 0x08, 0x0E, false));
        list.Add(Entry("Tab", 0x09, 0x0F, false));
        list.Add(Entry("Enter", 0x0D, 0x1C, false));
        list.Add(Entry("Escape", 0x1B, 0x01, false));
        list.Add(Entry("Space", 0x20, 0x39, false));
        list.Add(Entry("CapsLock", 0x14, 0x3A, false));

        // Navigation (extended)
        list.Add(Entry("PageUp", 0x21, 0x49, true));
        list.Add(Entry("PageDown", 0x22, 0x51, true));
        list.Add(Entry("End", 0x23, 0x4F, true));
        list.Add(Entry("Home", 0x24, 0x47, true));
        list.Add(Entry("ArrowLeft", 0x25, 0x4B, true));
        list.Add(Entry("ArrowUp", 0x26, 0x48, true));
        list.Add(Entry("ArrowRight", 0x27, 0x4D, true));
        list.Add(Entry("ArrowDown", 0x28, 0x50, true));
        list.Add(Entry("Insert", 0x2D, 0x52, true));
        list.Add(Entry("Delete", 0x2E, 0x53, true));

        list.Add(Entry("PrintScreen", 0x2C, 0x37, true));
        list.Add(Entry("ScrollLock", 0x91, 0x46, false));
        list.Add(Entry("Pause", 0x13, 0x45, false));
        list.Add(Entry("NumLock", 0x90, 0x45, true));
        list.Add(Entry("ContextMenu", 0x5D, 0x5D, true));

        // Numpad
        for (var i = 0; i <= 9; i++)
        {
            list.Add(Entry($"Numpad{i}", (ushort)(0x60 + i), NumpadDigitScan(i), extended: false));
        }

        list.Add(Entry("NumpadMultiply", 0x6A, 0x37, false));
        list.Add(Entry("NumpadAdd", 0x6B, 0x4E, false));
        list.Add(Entry("NumpadSeparator", 0x6C, 0x00, false));
        list.Add(Entry("NumpadSubtract", 0x6D, 0x4A, false));
        list.Add(Entry("NumpadDecimal", 0x6E, 0x53, false));
        list.Add(Entry("NumpadDivide", 0x6F, 0x35, true));
        list.Add(Entry("NumpadEnter", 0x0D, 0x1C, true));

        // OEM / punctuation
        list.Add(Entry("Semicolon", 0xBA, 0x27, false));
        list.Add(Entry("Equal", 0xBB, 0x0D, false));
        list.Add(Entry("Comma", 0xBC, 0x33, false));
        list.Add(Entry("Minus", 0xBD, 0x0C, false));
        list.Add(Entry("Period", 0xBE, 0x34, false));
        list.Add(Entry("Slash", 0xBF, 0x35, false));
        list.Add(Entry("Backquote", 0xC0, 0x29, false));
        list.Add(Entry("BracketLeft", 0xDB, 0x1A, false));
        list.Add(Entry("Backslash", 0xDC, 0x2B, false));
        list.Add(Entry("BracketRight", 0xDD, 0x1B, false));
        list.Add(Entry("Quote", 0xDE, 0x28, false));
        list.Add(Entry("IntlBackslash", 0xE2, 0x56, false));

        // Browser 0xA6–0xAC (extended, VK-only scan allowed 0)
        list.Add(Entry("BrowserBack", 0xA6, 0x00, true));
        list.Add(Entry("BrowserForward", 0xA7, 0x00, true));
        list.Add(Entry("BrowserRefresh", 0xA8, 0x00, true));
        list.Add(Entry("BrowserStop", 0xA9, 0x00, true));
        list.Add(Entry("BrowserSearch", 0xAA, 0x00, true));
        list.Add(Entry("BrowserFavorites", 0xAB, 0x00, true));
        list.Add(Entry("BrowserHome", 0xAC, 0x00, true));

        // Volume / media
        list.Add(Entry("VolumeMute", 0xAD, 0x00, true));
        list.Add(Entry("VolumeDown", 0xAE, 0x00, true));
        list.Add(Entry("VolumeUp", 0xAF, 0x00, true));
        list.Add(Entry("MediaNextTrack", 0xB0, 0x00, true));
        list.Add(Entry("MediaPreviousTrack", 0xB1, 0x00, true));
        list.Add(Entry("MediaStop", 0xB2, 0x00, true));
        list.Add(Entry("MediaPlayPause", 0xB3, 0x00, true));

        // Launch
        list.Add(Entry("LaunchMail", 0xB4, 0x00, true));
        list.Add(Entry("LaunchMediaPlayer", 0xB5, 0x00, true));
        list.Add(Entry("LaunchApp1", 0xB6, 0x00, true));
        list.Add(Entry("LaunchApp2", 0xB7, 0x00, true));

        return list;
    }

    private static KeyTokenInfo Entry(string token, ushort vk, ushort scan, bool extended)
        => new(token, vk, scan, extended);

    private static ushort ScanForLetter(char ch) => ch switch
    {
        'A' => 0x1E,
        'B' => 0x30,
        'C' => 0x2E,
        'D' => 0x20,
        'E' => 0x12,
        'F' => 0x21,
        'G' => 0x22,
        'H' => 0x23,
        'I' => 0x17,
        'J' => 0x24,
        'K' => 0x25,
        'L' => 0x26,
        'M' => 0x32,
        'N' => 0x31,
        'O' => 0x18,
        'P' => 0x19,
        'Q' => 0x10,
        'R' => 0x13,
        'S' => 0x1F,
        'T' => 0x14,
        'U' => 0x16,
        'V' => 0x2F,
        'W' => 0x11,
        'X' => 0x2D,
        'Y' => 0x15,
        'Z' => 0x2C,
        _ => 0x00,
    };

    private static ushort ScanForDigit(int digit) => digit switch
    {
        0 => 0x0B,
        1 => 0x02,
        2 => 0x03,
        3 => 0x04,
        4 => 0x05,
        5 => 0x06,
        6 => 0x07,
        7 => 0x08,
        8 => 0x09,
        9 => 0x0A,
        _ => 0x00,
    };

    private static ushort NumpadDigitScan(int digit) => digit switch
    {
        0 => 0x52,
        1 => 0x4F,
        2 => 0x50,
        3 => 0x51,
        4 => 0x4B,
        5 => 0x4C,
        6 => 0x4D,
        7 => 0x47,
        8 => 0x48,
        9 => 0x49,
        _ => 0x00,
    };
}
