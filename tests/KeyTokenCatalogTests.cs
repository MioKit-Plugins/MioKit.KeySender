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
