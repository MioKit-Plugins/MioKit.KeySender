using KeySender.Models;

namespace KeySender.Services;

/// <summary>
/// In-process hook source for unit tests. Push synthetic <see cref="RawKeyboardEvent"/> values.
/// </summary>
public sealed class FakeKeyboardHookSource : IKeyboardHookSource
{
    private readonly object _sync = new();
    private Func<RawKeyboardEvent, KeyboardHookAction>? _handler;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _handler is not null;
            }
        }
    }

    public void Start(Func<RawKeyboardEvent, KeyboardHookAction> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_sync)
        {
            if (_handler is not null)
            {
                throw new InvalidOperationException("Fake hook is already running.");
            }

            _handler = handler;
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _handler = null;
        }
    }

    /// <summary>
    /// Deliver one synthetic event on the calling thread. Returns PassThrough when not running.
    /// </summary>
    public KeyboardHookAction Push(RawKeyboardEvent ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        Func<RawKeyboardEvent, KeyboardHookAction>? handler;
        lock (_sync)
        {
            handler = _handler;
        }

        return handler?.Invoke(ev) ?? KeyboardHookAction.PassThrough;
    }

    public void Dispose() => Stop();
}
