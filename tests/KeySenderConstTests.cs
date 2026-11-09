using KeySender;
using Xunit;

namespace MioKit.KeySender.Tests;

public sealed class KeySenderConstTests
{
    [Theory]
    [InlineData(nameof(KeySenderConst.PluginTypeId), "52DC58C9-C304-45ED-A5B8-38B57E626519")]
    [InlineData(nameof(KeySenderConst.SearchGroupTypeId), "D3244F5B-5AE1-4BA0-9BED-F8A2766BC71E")]
    [InlineData(nameof(KeySenderConst.ManagerNodeTypeId), "DDE52F58-EBCE-4212-80F9-B317AC4F6850")]
    [InlineData(nameof(KeySenderConst.KeySenderNodeTypeId), "52E4A1BC-D0D3-470B-88C8-4CEE269B5046")]
    [InlineData(nameof(KeySenderConst.CategoryTypeId), "9BEBDDC0-80B1-46FA-A8A7-DDF919C09352")]
    [InlineData(nameof(KeySenderConst.BeforeDelayMsPropertyId), "0474A0C5-AFD1-4D10-A469-11DAC00B32E5")]
    [InlineData(nameof(KeySenderConst.AfterDelayMsPropertyId), "7CF666D4-FDB3-4EC1-BC20-BFB429C51BE8")]
    [InlineData(nameof(KeySenderConst.SequencePropertyId), "E13906DF-FD5B-47C2-AD6D-83763C93212B")]
    [InlineData(nameof(KeySenderConst.BuiltInCopyNodeId), "E32C3712-9407-4A41-8DD8-134810F5F93D")]
    [InlineData(nameof(KeySenderConst.BuiltInCutNodeId), "00C1CF3E-39C4-464F-8D37-A80C47B414F3")]
    [InlineData(nameof(KeySenderConst.BuiltInPasteNodeId), "C13771F3-65A4-4F0B-A2B9-11798D8AFA24")]
    public void Fixed_identity_strings_match_design(string memberName, string expected)
    {
        var actual = (string)typeof(KeySenderConst).GetField(memberName)!.GetValue(null)!;
        Assert.Equal(expected, actual);
        Assert.True(Guid.TryParseExact(actual, "D", out _));
    }

    [Fact]
    public void Guid_companions_parse_from_string_ids()
    {
        Assert.Equal(Guid.Parse(KeySenderConst.PluginTypeId), KeySenderConst.PluginTypeGuid);
        Assert.Equal(KeySenderConst.PluginTypeGuid, KeySenderConst.PluginType);
        Assert.Equal(Guid.Parse(KeySenderConst.SearchGroupTypeId), KeySenderConst.SearchGroupTypeGuid);
        Assert.Equal(Guid.Parse(KeySenderConst.ManagerNodeTypeId), KeySenderConst.ManagerNodeTypeGuid);
        Assert.Equal(Guid.Parse(KeySenderConst.KeySenderNodeTypeId), KeySenderConst.KeySenderNodeTypeGuid);
        Assert.Equal(Guid.Parse(KeySenderConst.CategoryTypeId), KeySenderConst.CategoryTypeGuid);
        Assert.Equal(Guid.Parse(KeySenderConst.BeforeDelayMsPropertyId), KeySenderConst.BeforeDelayMsPropertyGuid);
        Assert.Equal(Guid.Parse(KeySenderConst.AfterDelayMsPropertyId), KeySenderConst.AfterDelayMsPropertyGuid);
        Assert.Equal(Guid.Parse(KeySenderConst.SequencePropertyId), KeySenderConst.SequencePropertyGuid);
    }

    [Fact]
    public void Fixed_instance_string_ids_match_design()
    {
        Assert.Equal("key-sender-search-group", KeySenderConst.SearchGroupId);
        Assert.Equal("key-sender-manager-node", KeySenderConst.ManagerNodeId);
        Assert.Equal("key-sender-default-category", KeySenderConst.DefaultCategoryId);
        Assert.Equal("默认分类", KeySenderConst.DefaultCategoryName);
        Assert.Equal("all", KeySenderConst.ListScopeAll);
        Assert.Equal("builtin", KeySenderConst.ListScopeBuiltin);
        Assert.Equal("category", KeySenderConst.ListScopeCategory);
    }
}
