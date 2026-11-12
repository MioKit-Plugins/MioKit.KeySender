using KeySender.Models;
using Xunit;

namespace MioKit.KeySender.Tests;

public sealed class KeyTokenCatalogTests
{
    [Theory]
    [InlineData("KeyA", 0x41, false)]
    [InlineData("KeyZ", 0x5A, false)]
    [InlineData("Digit0", 0x30, false)]
    [InlineData("Digit9", 0x39, false)]
    [InlineData("F1", 0x70, false)]
    [InlineData("F24", 0x87, false)]
    [InlineData("LeftControl", 0xA2, false)]
    [InlineData("RightControl", 0xA3, true)]
    [InlineData("LeftAlt", 0xA4, false)]
    [InlineData("RightAlt", 0xA5, true)]
    [InlineData("Enter", 0x0D, false)]
    [InlineData("NumpadEnter", 0x0D, true)]
    [InlineData("Delete", 0x2E, true)]
    [InlineData("NumpadDivide", 0x6F, true)]
    [InlineData("BrowserBack", 0xA6, true)]
    [InlineData("VolumeMute", 0xAD, true)]
    [InlineData("LaunchApp2", 0xB7, true)]
    public void Lookup_returns_expected_vk_and_extended(string token, int vk, bool extended)
    {
        Assert.True(KeyTokenCatalog.TryGet(token, out var info));
        Assert.Equal((ushort)vk, info.VirtualKey);
        Assert.Equal(extended, info.Extended);
    }

    [Fact]
    public void NumpadEnter_uses_scan_1C_and_is_distinct_from_Enter()
    {
        Assert.True(KeyTokenCatalog.TryGet("NumpadEnter", out var numpad));
        Assert.True(KeyTokenCatalog.TryGet("Enter", out var enter));
        Assert.Equal(0x1C, numpad.ScanCode);
        Assert.True(numpad.Extended);
        Assert.False(enter.Extended);
        Assert.Equal(enter.VirtualKey, numpad.VirtualKey);
    }

    [Theory]
    [InlineData(0x10, 0x2A, false, "LeftShift")]
    [InlineData(0x10, 0x36, false, "RightShift")]
    [InlineData(0x11, 0x1D, false, "LeftControl")]
    [InlineData(0x11, 0x1D, true, "RightControl")]
    [InlineData(0x12, 0x38, false, "LeftAlt")]
    [InlineData(0x12, 0x38, true, "RightAlt")]
    [InlineData(0x0D, 0x1C, false, "Enter")]
    [InlineData(0x0D, 0x1C, true, "NumpadEnter")]
    [InlineData(0xBF, 0x35, false, "Slash")]
    [InlineData(0x6F, 0x35, true, "NumpadDivide")]
    [InlineData(0x23, 0x4F, false, "Numpad1")]
    [InlineData(0x23, 0x4F, true, "End")]
    [InlineData(0x2E, 0x53, false, "NumpadDecimal")]
    [InlineData(0x2E, 0x53, true, "Delete")]
    [InlineData(0x90, 0x45, true, "NumLock")]
    [InlineData(0x13, 0x45, false, "Pause")]
    [InlineData(0x2C, 0x37, true, "PrintScreen")]
    [InlineData(0x6A, 0x37, false, "NumpadMultiply")]
    [InlineData(0xAD, 0, false, "VolumeMute")]
    public void Raw_hook_events_resolve_physical_key_variants(int vk, int scan, bool extended, string token)
    {
        Assert.True(KeyTokenCatalog.TryResolve((ushort)vk, (ushort)scan, extended, out var info));
        Assert.Equal(token, info.Token);
    }

    [Fact]
    public void Unknown_and_wrong_case_tokens_are_rejected()
    {
        Assert.False(KeyTokenCatalog.Contains("leftcontrol"));
        Assert.False(KeyTokenCatalog.Contains("MouseLeft"));
        Assert.False(KeyTokenCatalog.TryGet("VK_PACKET", out _));
    }

    [Fact]
    public void Catalog_is_closed_and_non_empty()
    {
        Assert.True(KeyTokenCatalog.All.Count >= 130);
        Assert.All(KeyTokenCatalog.All, info => Assert.False(string.IsNullOrWhiteSpace(info.Token)));
    }
}
