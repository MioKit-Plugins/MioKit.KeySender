using KeySender;
using KeySender.BuiltIn;
using Xunit;

namespace MioKit.KeySender.Tests;

public sealed class BuiltInKeyCatalogTests
{
    [Theory]
    [InlineData(KeySenderConst.BuiltInCopyNodeId)]
    [InlineData("e32c3712-9407-4a41-8dd8-134810f5f93d")]
    [InlineData("{E32C3712-9407-4A41-8DD8-134810F5F93D}")]
    public void IsBuiltIn_normalizes_guid_case_and_format(string id)
    {
        Assert.True(BuiltInKeyCatalog.IsBuiltIn(id));
        Assert.Equal(KeySenderConst.BuiltInCopyNodeId, BuiltInKeyCatalog.CanonicalId(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    public void IsBuiltIn_rejects_non_catalog_ids(string? id)
    {
        Assert.False(BuiltInKeyCatalog.IsBuiltIn(id));
    }

    [Fact]
    public void Catalog_contains_exactly_three_recipes()
    {
        Assert.Equal(3, BuiltInKeyCatalog.All.Count);
        Assert.All(BuiltInKeyCatalog.All, r => Assert.True(Guid.TryParseExact(r.Id, "D", out _)));
    }
}
