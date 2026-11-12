using KeySender.Services;

namespace MioKit.KeySender.Tests.Fakes;

/// <summary>Reports nonzero window handles as valid and can simulate focus denial.</summary>
public sealed class FakeTargetWindowOps : ITargetWindowOps
{
    public nint Foreground { get; set; } = 0x1000;

    public bool AllowFocusChange { get; set; } = true;

    public bool IsWindow(nint hwnd) => hwnd != 0;

    public nint GetForegroundWindow() => Foreground;

    public bool SetForegroundWindow(nint hwnd)
    {
        if (!AllowFocusChange || hwnd == 0)
            return false;

        Foreground = hwnd;
        return true;
    }

    public uint? GetWindowProcessId(nint hwnd) => hwnd == 0 ? null : 4242u;

    public string? GetClassName(nint hwnd) => hwnd == 0 ? null : "FakeWindowClass";
}
