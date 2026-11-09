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
    private ManualResetEventSlim? _started;
    private Exception? _startError;
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_sync)
        {
            if (_hook != IntPtr.Zero || _thread is { IsAlive: true })
            {
                throw new InvalidOperationException("Native keyboard hook is already running.");
            }

            _handler = handler;
            _startError = null;
            _started = new ManualResetEventSlim(false);
            _thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "KeySender.NativeKeyboardHook",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        if (!_started.Wait(TimeSpan.FromSeconds(5)))
        {
            Stop();
            throw new InvalidOperationException(ValidationCodes.RecordingUnavailable + ": hook thread start timed out.");
        }

        if (_startError is not null)
        {
            var err = _startError;
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
        }

        if (thread is null)
        {
            return;
        }

        if (threadId != 0 && Environment.CurrentManagedThreadId != (int)threadId)
        {
            PostThreadMessage(threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);
            if (!thread.Join(TimeSpan.FromSeconds(5)))
            {
                // Keep references; report via caller if needed.
            }
        }
        else if (Environment.CurrentManagedThreadId != thread.ManagedThreadId)
        {
            thread.Join(TimeSpan.FromSeconds(5));
        }

        lock (_sync)
        {
            _thread = null;
            _threadId = 0;
            _handler = null;
            _proc = null;
            _hook = IntPtr.Zero;
            _started?.Dispose();
            _started = null;
            _startError = null;
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

    private void ThreadMain()
    {
        try
        {
            _threadId = GetCurrentThreadId();
            _proc = HookCallback;
            IntPtr module;
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                module = GetModuleHandle(process.MainModule?.ModuleName);
            }

            _hook = SetWindowsHookEx(WhKeyboardLl, _proc, module, 0);
            if (_hook == IntPtr.Zero)
            {
                _startError = new InvalidOperationException(
                    "SetWindowsHookEx failed: " + Marshal.GetLastWin32Error());
                _started?.Set();
                return;
            }

            _started?.Set();

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            _startError ??= ex;
            _started?.Set();
        }
        finally
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var handler = _handler;
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
