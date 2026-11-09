using System.Runtime.InteropServices;
using KeySender.Models;

namespace KeySender.Services;

/// <summary>
/// Sends exactly one KEYBDINPUT via SendInput(1). Does not batch sequences.
/// NumpadEnter uses VK_RETURN + scan 0x1C + extended (catalog).
/// </summary>
/// <remarks>
/// On x64 the Win32 INPUT union is sized for MOUSEINPUT (40 bytes total).
/// Declaring only KEYBDINPUT under-sizes cbSize and yields ERROR_INVALID_PARAMETER (87).
/// </remarks>
public sealed class NativeKeyboardSender : IKeyboardSender
{
    internal static readonly nint ExtraInfoTag = unchecked((nint)0x4B535244); // "KSRD"

    private const uint InputKeyboard = 1;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;

    /// <summary>Marshal.SizeOf&lt;NativeInput&gt; for the current process bitness (40 on x64, 28 on x86).</summary>
    public static int NativeInputSize => Marshal.SizeOf<NativeInput>();

    public Task<KeyboardSendResult> SendAsync(
        KeyTokenInfo key,
        bool keyDown,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            uint flags = 0;
            if (key.Extended)
            {
                flags |= KeyEventExtendedKey;
            }

            if (!keyDown)
            {
                flags |= KeyEventKeyUp;
            }

            NativeInput[] inputs =
            [
                new NativeInput
                {
                    Type = InputKeyboard,
                    U = new NativeInputUnion
                    {
                        Ki = new NativeKeyboardInput
                        {
                            Vk = key.VirtualKey,
                            Scan = key.ScanCode,
                            Flags = flags,
                            Time = 0,
                            ExtraInfo = ExtraInfoTag,
                        },
                    },
                },
            ];

            var cbSize = Marshal.SizeOf<NativeInput>();
            var sent = SendInput(1, inputs, cbSize);
            if (sent == 1)
            {
                return Task.FromResult(KeyboardSendResult.Ok());
            }

            var error = Marshal.GetLastWin32Error();
            return Task.FromResult(KeyboardSendResult.Failed(error));
        }
        catch (Exception)
        {
            // Ambiguous: caller treats Down as uncertain and enters cleanup.
            return Task.FromResult(KeyboardSendResult.Ambiguous(Marshal.GetLastWin32Error()));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeInput
    {
        public uint Type;
        public NativeInputUnion U;
    }

    /// <summary>
    /// Must be large enough for MOUSEINPUT so INPUT.cbSize matches user32 expectations.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct NativeInputUnion
    {
        [FieldOffset(0)]
        public NativeMouseInput Mi;

        [FieldOffset(0)]
        public NativeKeyboardInput Ki;

        [FieldOffset(0)]
        public NativeHardwareInput Hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeMouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeKeyboardInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeHardwareInput
    {
        public uint Msg;
        public ushort ParamL;
        public ushort ParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] NativeInput[] inputs, int size);
}
