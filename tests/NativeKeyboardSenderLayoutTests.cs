using KeySender.Services;
using Xunit;

namespace MioKit.KeySender.Tests;

public class NativeKeyboardSenderLayoutTests
{
    [Fact]
    public void NativeInput_size_matches_win32_input_for_process_bitness()
    {
        // Win32 INPUT: DWORD type + union(MOUSEINPUT).
        // x64 MOUSEINPUT is 32 bytes with ULONG_PTR padding → INPUT = 40.
        // x86 MOUSEINPUT is 24 bytes → INPUT = 28.
        var expected = Environment.Is64BitProcess ? 40 : 28;
        Assert.Equal(expected, NativeKeyboardSender.NativeInputSize);
    }
}
