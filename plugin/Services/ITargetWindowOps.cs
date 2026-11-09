namespace KeySender.Services;

/// <summary>Win32 seam for target window verify/restore. Injected fake skips real HWND ops in tests.</summary>
public interface ITargetWindowOps
{
    bool IsWindow(nint hwnd);

    nint GetForegroundWindow();

    bool SetForegroundWindow(nint hwnd);

    uint? GetWindowProcessId(nint hwnd);

    string? GetClassName(nint hwnd);
}
