using KeySender.Services;

namespace MioKit.KeySender.Tests.Fakes;

/// <summary>Always reports the requested HWND as valid and foreground.</summary>
public sealed class FakeTargetWindowOps : ITargetWindowOps
{
    public nint Foreground { get; set; } = 0x1000;

    public bool IsWindow(nint hwnd) => hwnd != 0;

    public nint GetForegroundWindow() => Foreground;

    public bool SetForegroundWindow(nint hwnd)
    {
        Foreground = hwnd;
        return hwnd != 0;
    }

    public uint? GetWindowProcessId(nint hwnd) => hwnd == 0 ? null : 4242u;

    public string? GetClassName(nint hwnd) => hwnd == 0 ? null : "FakeWindowClass";
}
