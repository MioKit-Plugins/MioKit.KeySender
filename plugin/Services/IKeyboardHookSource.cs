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
    /// Stop delivery. On the callback thread request exit without waiting for itself;
    /// other callers wait for the native thread to release its resources.
    /// </summary>
    void Stop();
}
