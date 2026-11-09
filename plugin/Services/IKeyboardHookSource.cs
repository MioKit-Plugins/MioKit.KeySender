using KeySender.Models;

namespace KeySender.Services;

public enum KeyboardHookAction : byte
{
    PassThrough = 0,
    Consume = 1,
}

/// <summary>
/// Abstract keyboard event source. Production uses WH_KEYBOARD_LL; tests inject a fake.
/// </summary>
public interface IKeyboardHookSource : IDisposable
{
    bool IsRunning { get; }

    /// <summary>
    /// Install / begin delivering events. Handler must stay light and return Consume to suppress.
    /// </summary>
    void Start(Func<RawKeyboardEvent, KeyboardHookAction> handler);

    /// <summary>
    /// Uninstall / stop delivery. Must not be called from inside the hook callback on the same thread
    /// that would deadlock waiting for itself (NativeKeyboardHook posts stop to its message loop).
    /// </summary>
    void Stop();
}
