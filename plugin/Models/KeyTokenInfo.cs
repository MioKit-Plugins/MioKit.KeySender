namespace KeySender.Models;

/// <summary>
/// Closed-catalog mapping for a stable key token.
/// </summary>
public sealed class KeyTokenInfo
{
    public KeyTokenInfo(string token, ushort virtualKey, ushort scanCode, bool extended)
    {
        Token = token;
        VirtualKey = virtualKey;
        ScanCode = scanCode;
        Extended = extended;
    }

    public string Token { get; }

    public ushort VirtualKey { get; }

    public ushort ScanCode { get; }

    public bool Extended { get; }
}
