using System.Text;
using System.Text.Json;
using KeySender;
using KeySender.Models;
using KeySender.Services;
using KeySender.Validation;
using Xunit;

namespace MioKit.KeySender.Tests;

public sealed class TransferPackageParserTests
{
    [Theory]
    [InlineData("-1")]
    [InlineData("2147483648")]
    [InlineData("1.0")]
    [InlineData("1e0")]
    [InlineData("\"1\"")]
    [InlineData("true")]
    [InlineData("null")]
    public void Event_delay_requires_an_in_range_json_integer(string value)
    {
        var parsed = TransferPackageParser.Parse(Package($$"""
            { "type": "keyDown", "key": "KeyA", "delayBeforeMs": {{value}} }
            """));

        Assert.False(parsed.Ok);
        Assert.Equal(ValidationCodes.InvalidFormat, parsed.Code);
        Assert.Contains("delayBeforeMs", parsed.Message);
    }

    [Theory]
    [InlineData("\"nodeId\": \"saved-node\",", "", "")]
    [InlineData("\"hotkey\": 42,", "", "")]
    [InlineData("\"isBuiltIn\": true,", "", "")]
    [InlineData("", "\"EventList\": [],", "")]
    [InlineData("", "", "\"eventId\": \"saved-row\",")]
    public void Import_whitelist_rejects_unsupported_properties(string node, string sequence, string @event)
    {
        var parsed = TransferPackageParser.Parse(Package($$"""
            { {{@event}} "type": "keyDown", "key": "KeyA" }
            """, node, sequence));

        Assert.False(parsed.Ok);
        Assert.Equal(ValidationCodes.InvalidFormat, parsed.Code);
        Assert.Contains("Unknown field", parsed.Message);
    }

    [Fact]
    public void Escaped_nul_in_description_is_rejected()
    {
        var parsed = TransferPackageParser.Parse(Package(
            """{ "type": "keyDown", "key": "KeyA" }""",
            "\"description\": \"a\\u0000b\","));

        Assert.False(parsed.Ok);
        Assert.Equal(ValidationCodes.InvalidFormat, parsed.Code);
        Assert.Contains("NUL", parsed.Message);
    }

    [Fact]
    public void Optional_values_default_and_each_parse_assigns_new_event_ids()
    {
        var json = Package("""
            { "type": "keyDown", "key": "KeyA" },
            { "type": "keyUp", "key": "KeyA" }
            """);
        var first = TransferPackageParser.Parse(json);
        var second = TransferPackageParser.Parse(json);

        Assert.True(first.CanImport);
        Assert.Null(first.WarningFingerprint);
        var write = Assert.Single(first.Nodes);
        Assert.Equal("Example", write.Name);
        Assert.Empty(write.Description);
        Assert.Equal(0, write.BeforeDelayMs);
        Assert.Equal(0, write.AfterDelayMs);
        Assert.All(write.Sequence.Events, row => Assert.Equal(0, row.DelayBeforeMs));
        var ids = write.Sequence.Events.Select(row => row.EventId).ToArray();
        Assert.All(ids, id => Assert.True(Guid.TryParseExact(id, "D", out _)));
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(Assert.Single(second.Nodes).Sequence.Events, row => ids.Contains(row.EventId));
    }

    [Fact]
    public void Sequence_version_mismatch_is_reported_in_preview()
    {
        var parsed = TransferPackageParser.Parse(Package(
            """{ "type": "keyUp", "key": "KeyA" }""", sequenceVersion: 1));

        Assert.True(parsed.Ok);
        Assert.False(parsed.CanImport);
        Assert.Contains(Assert.Single(parsed.Analyses).HardErrors,
            error => error.Code == ValidationCodes.UnsupportedSequenceVersion);
    }

    [Theory]
    [InlineData(KeySenderConst.EventCountMax, true)]
    [InlineData(KeySenderConst.EventCountMax + 1, false)]
    public void Import_enforces_the_per_sequence_event_limit(int count, bool canImport)
    {
        const string down = """{ "type": "keyDown", "key": "KeyA" }""";
        const string up = """{ "type": "keyUp", "key": "KeyA" }""";
        var events = string.Join(',', Enumerable.Range(0, count).Select(i => i % 2 == 0 ? down : up));

        var parsed = TransferPackageParser.Parse(Package(events));

        Assert.True(parsed.Ok);
        Assert.Equal(canImport, parsed.CanImport);
        var errors = Assert.Single(parsed.Analyses).HardErrors;
        if (canImport)
        {
            Assert.Empty(errors);
        }
        else
        {
            Assert.Contains(errors, error => error.Code == ValidationCodes.EventCountOutOfRange);
        }
    }

    [Fact]
    public void Import_accepts_a_utf8_file_at_the_byte_limit()
    {
        var json = Package("""{ "type": "keyUp", "key": "KeyA" }""");
        var bytes = Encoding.UTF8.GetBytes(json.PadRight(KeySenderConst.TransferMaxBytes));

        Assert.Equal(KeySenderConst.TransferMaxBytes, bytes.Length);
        Assert.True(TransferPackageParser.Parse(bytes).CanImport);
    }

    [Fact]
    public void Categorized_package_round_trips_distinct_categories_and_uncategorized_nodes()
    {
        var node = Assert.Single(TransferPackageParser.Parse(Package(
            """{ "type": "keyUp", "key": "KeyA" }""")).Nodes);
        var categories = new TransferCategory[] { new("办公", "常用"), new("办公", "备用") };
        int?[] categoryIndices = [0, null, 1, 0];

        var json = TransferPackageParser.WritePackageString([node, node, node, node], categories, categoryIndices);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(3, document.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.False(document.RootElement.GetProperty("nodes")[1].TryGetProperty("categoryIndex", out _));

        var parsed = TransferPackageParser.Parse(json);
        Assert.True(parsed.CanImport);
        Assert.Equal(categories, parsed.Categories);
        Assert.Equal(categoryIndices, parsed.CategoryIndices);
    }

    [Fact]
    public void Uncategorized_exports_keep_the_v2_shape()
    {
        var node = Assert.Single(TransferPackageParser.Parse(Package(
            """{ "type": "keyUp", "key": "KeyA" }""")).Nodes);

        var json = TransferPackageParser.WritePackageString([node], [], [null]);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(2, document.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("categories", out _));
        Assert.False(document.RootElement.GetProperty("nodes")[0].TryGetProperty("categoryIndex", out _));

        var parsed = TransferPackageParser.Parse(json);
        Assert.True(parsed.CanImport);
        Assert.Empty(parsed.Categories);
        Assert.Null(Assert.Single(parsed.CategoryIndices));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1")]
    [InlineData("0.0")]
    [InlineData("\"0\"")]
    [InlineData("null")]
    public void Invalid_category_reference_is_rejected(string categoryIndex)
    {
        var parsed = TransferPackageParser.Parse(Package(
            """{ "type": "keyUp", "key": "KeyA" }""",
            $$""" "categoryIndex": {{categoryIndex}}, """,
            formatVersion: 3,
            rootFields: """ "categories": [{"name": "办公"}], """));

        Assert.False(parsed.Ok);
        Assert.Equal(ValidationCodes.InvalidFormat, parsed.Code);
        Assert.Contains("categoryIndex", parsed.Message);
    }

    [Theory]
    [InlineData("""{ "name": " " }""")]
    [InlineData("""{ "name": "A\u0000B" }""")]
    [InlineData("""{ "name": "办公", "description": "A\u0000B" }""")]
    [InlineData("""{ "name": "办公", "name": "备用" }""")]
    [InlineData("""{ "name": "办公", "id": "host-id" }""")]
    public void Malformed_category_metadata_is_rejected(string category)
    {
        var parsed = TransferPackageParser.Parse(Package(
            """{ "type": "keyUp", "key": "KeyA" }""",
            """ "categoryIndex": 0, """,
            formatVersion: 3,
            rootFields: $$""" "categories": [{{category}}], """));

        Assert.False(parsed.Ok);
        Assert.Equal(ValidationCodes.InvalidFormat, parsed.Code);
    }

    [Fact]
    public void V2_does_not_accept_category_fields()
    {
        var parsed = TransferPackageParser.Parse(Package(
            """{ "type": "keyUp", "key": "KeyA" }""",
            """ "categoryIndex": 0, """));

        Assert.False(parsed.Ok);
        Assert.Equal(ValidationCodes.InvalidFormat, parsed.Code);
    }

    private static string Package(
        string events,
        string nodeFields = "",
        string sequenceFields = "",
        int sequenceVersion = 2,
        int formatVersion = 2,
        string rootFields = "") => $$"""
        {
          "format": "miokit.key-sender",
          "formatVersion": {{formatVersion}},
          {{rootFields}}
          "nodes": [{
            {{nodeFields}}
            "name": "  Example  ",
            "sequence": { {{sequenceFields}} "version": {{sequenceVersion}}, "events": [{{events}}] }
          }]
        }
        """;
}
