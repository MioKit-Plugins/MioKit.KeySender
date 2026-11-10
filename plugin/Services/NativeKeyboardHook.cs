using System.Runtime.InteropServices;
using KeySender.Models;
using KeySender.Validation;

namespace KeySender.Services;

/// <summary>
/// Production WH_KEYBOARD_LL on a dedicated thread with a message loop (design 03 §3).
/// </summary>
public sealed class NativeKeyboardHook : IKeyboardHookSource
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WmQuit = 0x0012;
    private const uint LlkhfExtended = 0x01;
    private const uint LlkhfInjected = 0x10;
    private const uint LlkhfLowerIlInjected = 0x02;
    private const uint LlkhfUp = 0x80;

    private readonly object _sync = new();
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;
    private Func<RawKeyboardEvent, KeyboardHookAction>? _handler;
    private bool _stopRequested;
    private bool _disposed;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _hook != IntPtr.Zero;
            }
        }
    }

    public void Start(Func<RawKeyboardEvent, KeyboardHookAction> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var started = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_hook != IntPtr.Zero || _thread is { IsAlive: true })
            {
                throw new InvalidOperationException("Native keyboard hook is already running.");
            }

            _handler = handler;
            _stopRequested = false;
            _thread = new Thread(() => ThreadMain(started))
            {
                IsBackground = true,
                Name = "KeySender.NativeKeyboardHook",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        if (!started.Task.Wait(TimeSpan.FromSeconds(5)))
        {
            Stop();
            throw new InvalidOperationException(ValidationCodes.RecordingUnavailable + ": hook thread start timed out.");
        }

        if (started.Task.Result is { } err)
        {
            Stop();
            throw new InvalidOperationException(ValidationCodes.RecordingUnavailable + ": " + err.Message, err);
        }
    }

    public void Stop()
    {
        Thread? thread;
        uint threadId;
        lock (_sync)
        {
            thread = _thread;
            threadId = _threadId;
            Volatile.Write(ref _stopRequested, true);
            Volatile.Write(ref _handler, null);
        }

        if (thread is null)
        {
            return;
        }

        if (threadId != 0)
        {
            PostThreadMessage(threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);
        }

        // A callback may stop its own source. Its message loop performs final cleanup.
        if (Thread.CurrentThread != thread && !thread.Join(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("Keyboard hook thread did not stop.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private void ThreadMain(TaskCompletionSource<Exception?> started)
    {
        var hook = IntPtr.Zero;
        try
        {
            // Create the queue before publishing the native thread ID to Stop.
            PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            lock (_sync) { _threadId = GetCurrentThreadId(); }
            _proc = HookCallback;
            IntPtr module;
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                module = GetModuleHandle(process.MainModule?.ModuleName);
            }

            hook = SetWindowsHookEx(WhKeyboardLl, _proc, module, 0);
            lock (_sync) { _hook = hook; }
            if (hook == IntPtr.Zero)
            {
                started.TrySetResult(new InvalidOperationException(
                    "SetWindowsHookEx failed: " + Marshal.GetLastWin32Error()));
                return;
            }

            started.TrySetResult(null);

            while (!Volatile.Read(ref _stopRequested) && GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            started.TrySetResult(ex);
        }
        finally
        {
            if (hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hook);
            }
            lock (_sync)
            {
                _hook = IntPtr.Zero;
                _handler = null;
                _proc = null;
                _threadId = 0;
                _thread = null;
            }
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var handler = Volatile.Read(ref _handler);
        if (handler is null || lParam == IntPtr.Zero)
        {
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        var msg = wParam.ToInt32();
        var flags = data.Flags;
        var isUp = (flags & LlkhfUp) != 0
                   || msg is WmKeyUp or WmSysKeyUp;
        var ev = new RawKeyboardEvent
        {
            VkCode = (ushort)data.VkCode,
            ScanCode = (ushort)data.ScanCode,
            Extended = (flags & LlkhfExtended) != 0,
            IsKeyUp = isUp,
            IsSystemKey = msg is WmSysKeyDown or WmSysKeyUp,
            Injected = (flags & LlkhfInjected) != 0,
            LowerIntegrityInjected = (flags & LlkhfLowerIlInjected) != 0,
            Timestamp = data.Time,
            ExtraInfo = data.DwExtraInfo,
        };

        KeyboardHookAction action;
        try
        {
            action = handler(ev);
        }
        catch
        {
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        if (action == KeyboardHookAction.Consume)
        {
            return (IntPtr)1;
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Pt;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out Msg msg, IntPtr hwnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, int msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
