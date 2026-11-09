namespace KeySender.Models;

/// <summary>
/// One ordered KeyDown/KeyUp event with a relative wait before it.
/// </summary>
public sealed class KeyboardEvent
{
    public string EventId { get; set; } = "";

    /// <summary>Wire value: <c>keyDown</c> or <c>keyUp</c>.</summary>
    public string Type { get; set; } = "";

    /// <summary>Stable catalog token, e.g. <c>LeftControl</c>, <c>KeyC</c>.</summary>
    public string Key { get; set; } = "";

    public int DelayBeforeMs { get; set; }
}
