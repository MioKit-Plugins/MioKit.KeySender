using System.Runtime.InteropServices;
using System.Text;

namespace KeySender.Services;

public sealed class Win32TargetWindowOps : ITargetWindowOps
{
    private const int MaxClassName = 256;

    public bool IsWindow(nint hwnd) => hwnd != 0 && IsWindowNative(hwnd);

    public nint GetForegroundWindow() => GetForegroundWindowNative();

    public bool SetForegroundWindow(nint hwnd) => hwnd != 0 && SetForegroundWindowNative(hwnd);

    public uint? GetWindowProcessId(nint hwnd)
    {
        if (hwnd == 0)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(hwnd, out var pid);
        return pid == 0 ? null : pid;
    }

    public string? GetClassName(nint hwnd)
    {
        if (hwnd == 0)
        {
            return null;
        }

        var sb = new StringBuilder(MaxClassName);
        var len = GetClassNameNative(hwnd, sb, sb.Capacity);
        return len <= 0 ? null : sb.ToString();
    }

    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    private static extern bool IsWindowNative(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint GetForegroundWindowNative();

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    private static extern bool SetForegroundWindowNative(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameNative(nint hWnd, StringBuilder lpClassName, int nMaxCount);
}
