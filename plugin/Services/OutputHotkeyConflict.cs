using KeySender.Models;
using KeySender.Validation;

namespace KeySender.Services;

/// <summary>
/// Optional host hotkey conflict preview. Gracefully no-ops when
/// <see cref="IGlobalHotKeyService"/> is unavailable (unit tests).
/// </summary>
public static class OutputHotkeyConflict
{
    public static string? FindConflict(
        IGlobalHotKeyService? hotkeys,
        IEnumerable<string> pressedTokens)
    {
        if (hotkeys is null)
        {
            return null;
        }

        IReadOnlyCollection<HotKeyRegistration> registrations;
        try
        {
            registrations = hotkeys.Registrations;
        }
        catch
        {
            return null;
        }

        if (registrations is null || registrations.Count == 0)
        {
            return null;
        }

        var codes = MapNormalized(pressedTokens);
        if (codes.Count == 0 || codes.Count > HotKeyInfo.MaxKeys)
        {
            return null;
        }

        var candidate = new HotKeyInfo(HotKeyInfo.TypeExactMatch, codes.ToArray()).Normalize();
        foreach (var reg in registrations)
        {
            if (reg.Status != HotKeyStatus.Registered)
            {
                continue;
            }

            if (reg.HotKeyInfo.Normalize().Equals(candidate))
            {
                return ValidationCodes.OutputHotkeyConflict;
            }
        }

        return null;
    }

    private static List<KeyCode> MapNormalized(IEnumerable<string> tokens)
    {
        var set = new HashSet<KeyCode>();
        foreach (var token in tokens)
        {
            if (TryMap(token, out var code))
            {
                set.Add(code);
            }
        }

        return set.OrderBy(c => (byte)c).ToList();
    }

    private static bool TryMap(string token, out KeyCode code)
    {
        code = KeyCode.None;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        // Normalize L/R modifiers to host common keys (design 03 §6).
        switch (token)
        {
            case "LeftControl":
            case "RightControl":
                code = KeyCode.Control;
                return true;
            case "LeftAlt":
            case "RightAlt":
                code = KeyCode.Menu;
                return true;
            case "LeftShift":
            case "RightShift":
                code = KeyCode.Shift;
                return true;
            case "LeftWin":
            case "RightWin":
                code = KeyCode.LWin;
                return true;
            case "Enter":
            case "NumpadEnter":
                code = KeyCode.Return;
                return true;
            case "Escape":
                code = KeyCode.Escape;
                return true;
            case "Tab":
                code = KeyCode.Tab;
                return true;
            case "Space":
                code = KeyCode.Space;
                return true;
            case "Backspace":
                code = KeyCode.Back;
                return true;
            case "Delete":
                code = KeyCode.Delete;
                return true;
            case "Insert":
                code = KeyCode.Insert;
                return true;
            case "Home":
                code = KeyCode.Home;
                return true;
            case "End":
                code = KeyCode.End;
                return true;
            case "PageUp":
                code = KeyCode.Prior;
                return true;
            case "PageDown":
                code = KeyCode.Next;
                return true;
            case "ArrowLeft":
                code = KeyCode.Left;
                return true;
            case "ArrowRight":
                code = KeyCode.Right;
                return true;
            case "ArrowUp":
                code = KeyCode.Up;
                return true;
            case "ArrowDown":
                code = KeyCode.Down;
                return true;
            case "ContextMenu":
                code = KeyCode.Apps;
                return true;
        }

        if (token.Length == 4 && token.StartsWith("Key", StringComparison.Ordinal) && token[3] is >= 'A' and <= 'Z')
        {
            code = (KeyCode)(byte)token[3];
            return true;
        }

        if (token.Length == 6 && token.StartsWith("Digit", StringComparison.Ordinal) && token[5] is >= '0' and <= '9')
        {
            code = (KeyCode)(byte)token[5];
            return true;
        }

        if (token.Length >= 2 && token[0] == 'F' && int.TryParse(token.AsSpan(1), out var f) && f is >= 1 and <= 24)
        {
            code = (KeyCode)(byte)(0x6F + f);
            return true;
        }

        return false;
    }
}
