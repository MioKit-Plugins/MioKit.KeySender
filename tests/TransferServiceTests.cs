using System.Text;
using KeySender;
using KeySender.Models;
using KeySender.Services;
using KeySender.Validation;
using MioKit.KeySender.Tests.Fakes;
using Xunit;

namespace MioKit.KeySender.Tests;

public sealed class TransferServiceTests
{
    private const string ValidV2Package = """
        {
          "format": "miokit.key-sender",
          "formatVersion": 2,
          "nodes": [
            {
              "name": "复制",
              "description": "按录制间隔发送 Ctrl+C",
              "beforeDelayMs": 200,
              "afterDelayMs": 300,
              "sequence": {
                "version": 2,
                "events": [
                  { "type": "keyDown", "key": "LeftControl", "delayBeforeMs": 0 },
                  { "type": "keyDown", "key": "KeyC", "delayBeforeMs": 50 },
                  { "type": "keyUp", "key": "KeyC", "delayBeforeMs": 30 },
                  { "type": "keyUp", "key": "LeftControl", "delayBeforeMs": 20 }
                ]
              }
            },
            {
              "name": "保持左 Shift",
              "description": "正常结束后继续按住，后续显式释放",
              "beforeDelayMs": 0,
              "afterDelayMs": 0,
              "sequence": {
                "version": 2,
                "events": [
                  { "type": "keyDown", "key": "LeftShift", "delayBeforeMs": 0 }
                ]
              }
            },
            {
              "name": "释放左 Shift",
              "description": "可释放另一个节点留下的键",
              "beforeDelayMs": 0,
              "afterDelayMs": 0,
              "sequence": {
                "version": 2,
                "events": [
                  { "type": "keyUp", "key": "LeftShift", "delayBeforeMs": 0 }
                ]
              }
            }
          ]
        }
        """;

    [Fact]
    public void Valid_v2_package_parses_with_ctrl_c_total_600_and_warnings()
    {
        var parsed = TransferPackageParser.Parse(ValidV2Package);
        Assert.True(parsed.Ok);
        Assert.True(parsed.CanImport);
        Assert.Equal(3, parsed.Nodes.Count);
        Assert.NotNull(parsed.WarningFingerprint);

        var copy = parsed.Nodes[0];
        long total = copy.BeforeDelayMs + copy.AfterDelayMs;
        foreach (var ev in copy.Sequence.Events)
        {
            total += ev.DelayBeforeMs;
        }

        Assert.Equal(600, total);
        Assert.Contains(parsed.Analyses[1].Warnings, w => w.Code == ValidationCodes.UnclosedKeyDown);
        Assert.Contains(parsed.Analyses[2].Warnings, w => w.Code == ValidationCodes.UnmatchedKeyUp);
    }

    [Fact]
    public void V1_package_is_rejected()
    {
        var json = """
            {
              "format": "miokit.key-sender",
              "formatVersion": 1,
              "nodes": [
                {
                  "name": "old",
                  "sequence": { "version": 1, "events": [ { "type": "keyDown", "key": "KeyA", "delayBeforeMs": 0 } ] }
                }
              ]
            }
            """;
        var parsed = TransferPackageParser.Parse(json);
        Assert.False(parsed.Ok);
        Assert.Equal(ValidationCodes.UnsupportedFormatVersion, parsed.Code);
    }

    [Fact]
    public async Task Bad_package_does_not_partial_import()
    {
        var config = new FakeConfigurationService();
        var transfer = new TransferService(config);

        var bad = """
            {
              "format": "miokit.key-sender",
              "formatVersion": 2,
              "nodes": [
                {
                  "name": "ok",
                  "sequence": {
                    "version": 2,
                    "events": [ { "type": "keyDown", "key": "KeyA", "delayBeforeMs": 0 }, { "type": "keyUp", "key": "KeyA", "delayBeforeMs": 0 } ]
                  }
                },
                {
                  "name": "bad",
                  "sequence": {
                    "version": 2,
                    "events": [ { "type": "keyDown", "key": "NotARealKey", "delayBeforeMs": 0 } ]
                  }
                }
              ]
            }
            """;

        var preview = transfer.PreviewFromString("win", bad);
        Assert.True(preview.Ok);
        Assert.False(preview.Preview!.CanImport);

        var commit = await transfer.CommitAsync("win", preview.Preview.PreviewId);
        Assert.False(commit.Ok);
        Assert.Equal(ValidationCodes.ImportInvalid, commit.Code);
        Assert.Empty(config.Created);
    }

    [Fact]
    public async Task Down_only_import_does_not_require_confirmation()
    {
        var config = new FakeConfigurationService();
        var transfer = new TransferService(config);
        var json = """
            {
              "format": "miokit.key-sender",
              "formatVersion": 2,
              "nodes": [
                {
                  "name": "hold",
                  "sequence": {
                    "version": 2,
                    "events": [ { "type": "keyDown", "key": "LeftShift", "delayBeforeMs": 0 } ]
                  }
                }
              ]
            }
            """;

        var preview = transfer.PreviewFromString("win", json);
        Assert.True(preview.Ok);
        Assert.True(preview.Preview!.CanImport);
        Assert.False(string.IsNullOrEmpty(preview.Preview.WarningFingerprint));

        var allowed = await transfer.CommitAsync(
            "win",
            preview.Preview.PreviewId);
        Assert.True(allowed.Ok);
        Assert.Single(config.Created);
        Assert.Equal("LeftShift", config.Created[0].Sequence.Events[0].Key);
        Assert.Equal("keyDown", config.Created[0].Sequence.Events[0].Type);
    }

    [Fact]
    public async Task Bom_file_imports_when_original_bytes_are_unchanged()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(ValidV2Package)).ToArray();
        var config = new FakeConfigurationService();
        var transfer = new TransferService(config);
        var preview = transfer.PreviewFromBytes("window", bytes).Preview!;
        Assert.True((await transfer.CommitAsync("window", preview.PreviewId, revalidateBytes: bytes)).Ok);

        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            preview = transfer.PreviewFromBytes("window", bytes, path).Preview!;
            Assert.True((await transfer.CommitAsync("window", preview.PreviewId)).Ok);
        }
        finally { File.Delete(path); }
        Assert.Equal(6, config.Created.Count);
    }

    [Fact]
    public async Task Changed_bytes_are_still_rejected_after_bom_fix()
    {
        var transfer = new TransferService(new FakeConfigurationService());
        var bytes = Encoding.UTF8.GetBytes(ValidV2Package);
        var preview = transfer.PreviewFromBytes("window", bytes).Preview!;
        var changed = bytes.Concat(new byte[] { 0x20 }).ToArray();
        Assert.Equal(ValidationCodes.ImportChanged,
            (await transfer.CommitAsync("window", preview.PreviewId, revalidateBytes: changed)).Code);
    }

    [Fact]
    public void Oversized_package_rejected()
    {
        var huge = new string('a', KeySenderConst.TransferMaxBytes + 8);
        var parsed = TransferPackageParser.Parse(Encoding.UTF8.GetBytes(huge));
        Assert.False(parsed.Ok);
        Assert.Equal(ValidationCodes.FileTooLarge, parsed.Code);
    }

    [Fact]
    public void Depth_limit_rejected()
    {
        // Build nested arrays deeper than 16 under a decoy structure that still parses as JSON.
        var sb = new StringBuilder();
        sb.Append("{\"format\":\"miokit.key-sender\",\"formatVersion\":2,\"nodes\":");
        for (var i = 0; i < 20; i++)
        {
            sb.Append('[');
        }

        sb.Append('1');
        for (var i = 0; i < 20; i++)
        {
            sb.Append(']');
        }

        sb.Append('}');
        var parsed = TransferPackageParser.Parse(sb.ToString());
        Assert.False(parsed.Ok);
        Assert.True(
            parsed.Code is ValidationCodes.ImportLimitExceeded or ValidationCodes.InvalidFormat,
            parsed.Code);
    }

    [Fact]
    public void Node_count_over_500_rejected()
    {
        var sb = new StringBuilder();
        sb.Append("{\"format\":\"miokit.key-sender\",\"formatVersion\":2,\"nodes\":[");
        for (var i = 0; i < 501; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append("{\"name\":\"n");
            sb.Append(i);
            sb.Append("\",\"sequence\":{\"version\":2,\"events\":[{\"type\":\"keyDown\",\"key\":\"KeyA\",\"delayBeforeMs\":0}]}}");
        }

        sb.Append("]}");
        var parsed = TransferPackageParser.Parse(sb.ToString());
        Assert.False(parsed.Ok);
        Assert.Equal(ValidationCodes.ImportLimitExceeded, parsed.Code);
    }

    [Fact]
    public void Export_roundtrip_preserves_events_without_builtin_markers()
    {
        var writes = new List<NodeWrite>
        {
            new()
            {
                Name = "复制",
                Description = "x",
                BeforeDelayMs = 0,
                AfterDelayMs = 0,
                Sequence = new KeyboardSequence
                {
                    Version = 2,
                    Events =
                    [
                        new KeyboardEvent { Type = "keyDown", Key = "LeftControl", DelayBeforeMs = 0, EventId = "a" },
                        new KeyboardEvent { Type = "keyDown", Key = "KeyC", DelayBeforeMs = 10, EventId = "b" },
                        new KeyboardEvent { Type = "keyUp", Key = "KeyC", DelayBeforeMs = 10, EventId = "c" },
                        new KeyboardEvent { Type = "keyUp", Key = "LeftControl", DelayBeforeMs = 10, EventId = "d" },
                    ],
                },
            },
        };

        var json = TransferPackageParser.WritePackageString(writes);
        Assert.DoesNotContain("isBuiltIn", json, StringComparison.Ordinal);
        Assert.DoesNotContain("eventId", json, StringComparison.OrdinalIgnoreCase);

        var parsed = TransferPackageParser.Parse(json);
        Assert.True(parsed.Ok);
        Assert.Equal(4, parsed.Nodes[0].Sequence.Events.Count);
        Assert.Equal("KeyC", parsed.Nodes[0].Sequence.Events[1].Key);
    }
}
