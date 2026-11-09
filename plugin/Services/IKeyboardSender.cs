using KeySender.Models;

namespace KeySender.Services;

/// <summary>Single-event keyboard submission boundary (design 03 §9).</summary>
public interface IKeyboardSender
{
    Task<KeyboardSendResult> SendAsync(KeyTokenInfo key, bool keyDown, CancellationToken cancellationToken = default);
}
