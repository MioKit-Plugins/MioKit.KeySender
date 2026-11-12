using System.Text;
using KeySender;
using KeySender.Models;
using KeySender.Services;
using KeySender.Validation;
using MioKit.KeySender.Tests.Fakes;
using MioKit.Sdk;
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
    public async Task Preview_belongs_to_its_window_and_is_consumed_by_successful_import()
    {
        var config = new FakeConfigurationService();
        var transfer = new TransferService(config);
        var preview = Assert.IsType<ImportPreview>(transfer.PreviewFromString("owner", ValidV2Package).Preview);

        var fromOtherWindow = await transfer.CommitAsync("other", preview.PreviewId);
        Assert.Equal(ValidationCodes.ImportPreviewExpired, fromOtherWindow.Code);
        Assert.Empty(config.Created);

        var committed = await transfer.CommitAsync("owner", preview.PreviewId);

        Assert.True(committed.Ok);
        Assert.Equal(new[] { "复制", "保持左 Shift", "释放左 Shift" }, committed.CreatedItems!.Select(x => x.Name));
        Assert.All(committed.CreatedItems!, item => Assert.False(item.IsBuiltIn));
        Assert.Equal(3, committed.CreatedIds!.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ValidationCodes.ImportPreviewExpired, transfer.GetPreview(preview.PreviewId).Code);
        Assert.Equal(ValidationCodes.ImportPreviewExpired, (await transfer.CommitAsync("owner", preview.PreviewId)).Code);
        Assert.Equal(3, config.Created.Count);
    }

    [Fact]
    public async Task Concurrent_commit_of_the_same_preview_does_not_write_a_second_batch()
    {
        var firstSaveEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishFirstSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saves = 0;
        var config = new FakeConfigurationService
        {
            CreateHandler = async _ =>
            {
                var save = Interlocked.Increment(ref saves);
                if (save == 1)
                {
                    firstSaveEntered.SetResult();
                    await finishFirstSave.Task;
                }

                return ConfigurationResult.Success($"saved-{save}");
            },
        };
        var transfer = new TransferService(config);
        var preview = Assert.IsType<ImportPreview>(transfer.PreviewFromString("owner", ValidV2Package).Preview);
        var first = transfer.CommitAsync("owner", preview.PreviewId);

        try
        {
            await firstSaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var duplicate = await transfer.CommitAsync("owner", preview.PreviewId)
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(ValidationCodes.ImportInProgress, duplicate.Code);
            Assert.Equal(1, Volatile.Read(ref saves));
        }
        finally
        {
            finishFirstSave.TrySetResult();
        }

        var committed = await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(committed.Ok);
        Assert.Equal(new[] { "saved-1", "saved-2", "saved-3" }, committed.CreatedIds);
        Assert.Equal(3, saves);
        Assert.Equal(ValidationCodes.ImportPreviewExpired, transfer.GetPreview(preview.PreviewId).Code);
    }

    [Fact]
    public async Task Cancellation_after_first_save_reports_saved_nodes_and_consumes_preview()
    {
        using var cancellation = new CancellationTokenSource();
        var attemptedNames = new List<string>();
        var config = new FakeConfigurationService
        {
            CreateHandler = write =>
            {
                attemptedNames.Add(write.Name);
                cancellation.Cancel();
                return Task.FromResult(ConfigurationResult.Success("saved-before-cancel"));
            },
        };
        var transfer = new TransferService(config);
        var preview = Assert.IsType<ImportPreview>(transfer.PreviewFromString("owner", ValidV2Package).Preview);

        var cancelled = await transfer.CommitAsync("owner", preview.PreviewId, cancellationToken: cancellation.Token);

        Assert.Equal(ValidationCodes.Cancelled, cancelled.Code);
        Assert.True(cancelled.Persisted);
        Assert.Equal(new[] { "saved-before-cancel" }, cancelled.CreatedIds);
        Assert.Equal(ValidationCodes.ImportPreviewExpired, (await transfer.CommitAsync("owner", preview.PreviewId)).Code);
        Assert.Equal("复制", Assert.Single(attemptedNames));
    }

    [Fact]
    public async Task Cancellation_before_first_save_leaves_preview_available_for_retry()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var config = new FakeConfigurationService();
        var transfer = new TransferService(config);
        var preview = Assert.IsType<ImportPreview>(transfer.PreviewFromString("owner", ValidV2Package).Preview);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transfer.CommitAsync("owner", preview.PreviewId, cancellationToken: cancellation.Token));
        Assert.Empty(config.Created);

        Assert.True((await transfer.CommitAsync("owner", preview.PreviewId)).Ok);
        Assert.Equal(3, config.Created.Count);
    }

    [Fact]
    public async Task Releasing_a_stale_preview_does_not_release_its_replacement()
    {
        var config = new FakeConfigurationService();
        var transfer = new TransferService(config);
        var original = Assert.IsType<ImportPreview>(transfer.PreviewFromString("owner", ValidV2Package).Preview);
        var replacement = Assert.IsType<ImportPreview>(transfer.PreviewFromString("owner", ValidV2Package).Preview);

        transfer.ReleasePreview(original.PreviewId);

        Assert.Equal(ValidationCodes.ImportPreviewExpired, transfer.GetPreview(original.PreviewId).Code);
        Assert.True(transfer.GetPreview(replacement.PreviewId).Ok);
        transfer.ReleasePreview(replacement.PreviewId);
        Assert.Equal(ValidationCodes.ImportPreviewExpired, (await transfer.CommitAsync("owner", replacement.PreviewId)).Code);
        Assert.Empty(config.Created);
    }

    [Fact]
    public async Task Missing_source_file_is_rejected_before_creating_nodes()
    {
        var config = new FakeConfigurationService();
        var transfer = new TransferService(config);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".miokeys");
        try
        {
            await File.WriteAllTextAsync(path, ValidV2Package);
            var bytes = await File.ReadAllBytesAsync(path);
            var preview = Assert.IsType<ImportPreview>(transfer.PreviewFromBytes("owner", bytes, path).Preview);
            File.Delete(path);

            var failed = await transfer.CommitAsync("owner", preview.PreviewId);

            Assert.Equal(ValidationCodes.FileAccessFailed, failed.Code);
            Assert.Empty(config.Created);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Failed_first_save_without_persistence_allows_retrying_the_preview()
    {
        var config = new FakeConfigurationService
        {
            CreateHandler = _ => Task.FromResult(
                ConfigurationResult.Fail(ValidationCodes.PersistenceFailed, "write rejected")),
        };
        var transfer = new TransferService(config);
        var preview = Assert.IsType<ImportPreview>(transfer.PreviewFromString("owner", ValidV2Package).Preview);

        var failed = await transfer.CommitAsync("owner", preview.PreviewId);

        Assert.Equal(ValidationCodes.PersistenceFailed, failed.Code);
        Assert.False(failed.Persisted);
        Assert.Empty(config.Created);
        Assert.True(transfer.GetPreview(preview.PreviewId).Ok);

        config.CreateHandler = null;
        Assert.True((await transfer.CommitAsync("owner", preview.PreviewId)).Ok);
        Assert.Equal(3, config.Created.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_after_persistence_reports_created_ids_and_consumes_preview(bool secondNodePersisted)
    {
        var attemptedNames = new List<string>();
        var config = new FakeConfigurationService
        {
            CreateHandler = write =>
            {
                attemptedNames.Add(write.Name);
                if (write.Name == "复制")
                {
                    return Task.FromResult(ConfigurationResult.Success("created-first"));
                }

                return Task.FromResult(new ConfigurationResult
                {
                    Code = ValidationCodes.PersistenceFailed,
                    Persisted = secondNodePersisted,
                    CreatedIds = secondNodePersisted ? ["created-second"] : null,
                    NodeId = secondNodePersisted ? "created-second" : "not-saved",
                });
            },
        };
        var transfer = new TransferService(config);
        var preview = Assert.IsType<ImportPreview>(transfer.PreviewFromString("owner", ValidV2Package).Preview);

        var failed = await transfer.CommitAsync("owner", preview.PreviewId);

        Assert.Equal(ValidationCodes.PersistenceFailed, failed.Code);
        Assert.True(failed.Persisted);
        var expected = secondNodePersisted ? new[] { "created-first", "created-second" } : ["created-first"];
        Assert.Equal(expected, failed.CreatedIds);
        Assert.Equal(ValidationCodes.ImportPreviewExpired, (await transfer.CommitAsync("owner", preview.PreviewId)).Code);
        Assert.Equal(new[] { "复制", "保持左 Shift" }, attemptedNames);
    }

    [Fact]
    public async Task Export_writes_selected_saved_nodes_in_requested_order()
    {
        var config = new FakeConfigurationService();
        var copy = new ConfigurationService(ThrowingMioDataProvider.Create(out _), plugin: null);
        var builtIn = Assert.IsType<NodeDetail>((await copy.GetDetailAsync(KeySenderConst.BuiltInCopyNodeId)).Detail);
        config.Details[builtIn.NodeId] = builtIn;
        var writes = TransferPackageParser.Parse(ValidV2Package).Nodes;
        var release = Assert.IsType<NodeDetail>((await config.CreateAsync(writes[2])).Detail);
        var transfer = new TransferService(config);
        using var stream = new MemoryStream();

        var result = await transfer.ExportAsync([release.NodeId, builtIn.NodeId], stream);

        Assert.True(result.Ok);
        Assert.Equal(2, result.ExportedCount);
        var json = Encoding.UTF8.GetString(stream.ToArray());
        Assert.DoesNotContain(release.NodeId, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(builtIn.NodeId, json, StringComparison.OrdinalIgnoreCase);
        var parsed = TransferPackageParser.Parse(json);
        Assert.True(parsed.CanImport);
        Assert.Empty(parsed.Categories);
        Assert.All(parsed.CategoryIndices, index => Assert.Null(index));
        Assert.Equal(new[] { release.Name, builtIn.Name }, parsed.Nodes.Select(node => node.Name));
        Assert.Equal("keyUp", Assert.Single(parsed.Nodes[0].Sequence.Events).Type);
    }

    [Fact]
    public async Task Export_of_a_batch_with_a_missing_node_does_not_write_to_the_output_stream()
    {
        var config = new FakeConfigurationService();
        var write = TransferPackageParser.Parse(ValidV2Package).Nodes[0];
        var saved = await config.CreateAsync(write);
        var transfer = new TransferService(config);
        using var stream = new MemoryStream();

        var result = await transfer.ExportAsync([saved.NodeId!, "missing"], stream);

        Assert.Equal(ValidationCodes.NodeNotFound, result.Code);
        Assert.Empty(stream.ToArray());
    }

    [Fact]
    public async Task Export_accepts_500_nodes_and_rejects_501_without_writing()
    {
        var config = new FakeConfigurationService();
        var write = TransferPackageParser.Parse(ValidV2Package).Nodes[0];
        var nodeIds = Enumerable.Range(0, KeySenderConst.TransferMaxNodes)
            .Select(index => $"node-{index}").ToArray();
        foreach (var id in nodeIds)
        {
            config.Details[id] = ConfigHelpers.ToDetail(id, write, isBuiltIn: false);
        }

        var transfer = new TransferService(config);
        using var output = new MemoryStream();
        var accepted = await transfer.ExportAsync(nodeIds, output);
        Assert.True(accepted.Ok, accepted.Message);
        Assert.Equal(KeySenderConst.TransferMaxNodes, accepted.ExportedCount);
        var parsed = TransferPackageParser.Parse(output.ToArray());
        Assert.True(parsed.CanImport, parsed.Message);
        Assert.Equal(KeySenderConst.TransferMaxNodes, parsed.Nodes.Count);

        output.SetLength(0);
        var rejected = await transfer.ExportAsync([.. nodeIds, "missing"], output);
        Assert.False(rejected.Ok);
        Assert.Equal(ValidationCodes.ExportLimitExceeded, rejected.Code);
        Assert.Equal(0, output.Length);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Category_only_persistence_is_reported_and_consumes_the_preview(bool categoryAttachFailed)
    {
        var config = new FakeConfigurationService
        {
            CreateInCategoryHandler = (_, _) => Task.FromResult(
                ConfigurationResult.Fail(ValidationCodes.PersistenceFailed, "node save failed")),
        };
        if (categoryAttachFailed)
        {
            config.ResolveImportCategoryHandler = (_, _, _) => Task.FromResult(new ConfigurationResult
            {
                Code = ValidationCodes.PersistenceFailed,
                NodeId = "saved-category",
                Persisted = true,
            });
        }

        var transfer = new TransferService(config);
        var write = TransferPackageParser.Parse(ValidV2Package).Nodes[0];
        var json = TransferPackageParser.WritePackageString([write], [new("办公", "")], [0]);
        var preview = Assert.IsType<ImportPreview>(transfer.PreviewFromString("owner", json).Preview);

        var failed = await transfer.CommitAsync("owner", preview.PreviewId);

        Assert.Equal(ValidationCodes.PersistenceFailed, failed.Code);
        Assert.True(failed.Persisted);
        Assert.Empty(failed.CreatedIds!);
        var expected = categoryAttachFailed ? "saved-category" : Assert.Single(config.Categories.Keys);
        Assert.Equal(expected, Assert.Single(failed.CreatedCategoryIds!));
        Assert.Equal(ValidationCodes.ImportPreviewExpired, (await transfer.CommitAsync("owner", preview.PreviewId)).Code);
    }
}

[Collection(ConfigurationPersistenceCollection.Name)]
public sealed class CategorizedTransferServiceTests
{
    [Fact]
    public async Task Selected_nodes_round_trip_with_categories_and_reuse_a_unique_local_match()
    {
        using var designMode = DesignModeScope.Enter();
        var (_, source) = CreateHarness();
        var work = (await source.CreateCategoryAsync("Work", "文件里的说明")).NodeId!;
        var game = (await source.CreateCategoryAsync("游戏", "游戏常用按键")).NodeId!;
        var unselected = (await source.CreateCategoryAsync("未选择", "")).NodeId!;
        await source.CreateInCategoryAsync(Write("未选择节点"), unselected);
        var first = (await source.CreateInCategoryAsync(Write("工作一"), work)).NodeId!;
        var second = (await source.CreateInCategoryAsync(Write("工作二"), work)).NodeId!;
        var third = (await source.CreateInCategoryAsync(Write("游戏一"), game)).NodeId!;
        var uncategorized = (await source.CreateAsync(Write("未分类节点"))).NodeId!;
        var selected = new[] { first, uncategorized, third, second, KeySenderConst.BuiltInCopyNodeId };
        using var stream = new MemoryStream();

        var exported = await new TransferService(source).ExportAsync(selected, stream);

        Assert.True(exported.Ok, exported.Message);
        var json = Encoding.UTF8.GetString(stream.ToArray());
        foreach (var id in selected.Concat([work, game, unselected]))
        {
            Assert.DoesNotContain(id, json, StringComparison.OrdinalIgnoreCase);
        }

        var parsed = TransferPackageParser.Parse(json);
        Assert.True(parsed.CanImport, parsed.Message);
        Assert.Equal(new TransferCategory[] { new("Work", "文件里的说明"), new("游戏", "游戏常用按键") }, parsed.Categories);
        Assert.Equal(new int?[] { 0, null, 1, 0, null }, parsed.CategoryIndices);

        var (group, target) = CreateHarness();
        var localWork = (await target.CreateCategoryAsync("  work  ", "本机说明")).NodeId!;
        var transfer = new TransferService(target);
        var cancelled = Assert.IsType<ImportPreview>(transfer.PreviewFromString("owner", json).Preview);
        Assert.Equal(new string?[] { "Work", null, "游戏", "Work", null }, cancelled.Items.Select(item => item.CategoryName));
        transfer.ReleasePreview(cancelled.PreviewId);
        Assert.Equal(ValidationCodes.ImportPreviewExpired, (await transfer.CommitAsync("owner", cancelled.PreviewId)).Code);
        Assert.Single(group.Children.OfType<KeySenderCategory>());
        Assert.Empty(group.GetDescendants<KeySenderNode>());

        var preview = Assert.IsType<ImportPreview>(transfer.PreviewFromString("owner", json).Preview);
        var imported = await transfer.CommitAsync("owner", preview.PreviewId);

        Assert.True(imported.Ok, imported.Message);
        var newGame = Assert.Single(imported.CreatedCategoryIds!);
        Assert.Equal(parsed.Nodes.Select(node => node.Name), imported.CreatedItems!.Select(node => node.Name));
        Assert.Equal(new string?[] { localWork, null, newGame, localWork, null }, imported.CreatedItems!.Select(node => node.CategoryId));
        Assert.Equal(new string?[] { "work", null, "游戏", "work", null }, imported.CreatedItems!.Select(node => node.CategoryName));
        var byId = group.Children.OfType<KeySenderCategory>().ToDictionary(category => category.Id);
        Assert.Equal("本机说明", byId[localWork].GetDescription());
        Assert.Equal("游戏常用按键", byId[newGame].GetDescription());
        Assert.Equal(2, byId[localWork].Children.OfType<KeySenderNode>().Count());
        Assert.Single(byId[newGame].Children.OfType<KeySenderNode>());
        var rootNodes = group.Children.OfType<KeySenderNode>().ToArray();
        Assert.Equal(2, rootNodes.Length);
        Assert.All(rootNodes, node => Assert.NotEqual(KeySenderConst.BuiltInCopyNodeId, node.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Same_name_categories_stay_separate_when_the_source_or_destination_is_ambiguous(bool duplicateInFile)
    {
        using var designMode = DesignModeScope.Enter();
        var (group, configuration) = CreateHarness();
        var existing = (await configuration.CreateCategoryAsync("Work", "本机一")).NodeId!;
        var other = duplicateInFile ? null : (await configuration.CreateCategoryAsync("work", "本机二")).NodeId;
        var categories = new List<TransferCategory> { new(" work ", "文件一") };
        var writes = new List<NodeWrite> { Write("一") };
        var indices = new List<int?> { 0 };
        if (duplicateInFile)
        {
            categories.Add(new("WORK", "文件二"));
            writes.Add(Write("二"));
            indices.Add(1);
        }

        var transfer = new TransferService(configuration);
        var json = TransferPackageParser.WritePackageString(writes, categories, indices);
        var preview = Assert.IsType<ImportPreview>(transfer.PreviewFromString("owner", json).Preview);
        var imported = await transfer.CommitAsync("owner", preview.PreviewId);

        Assert.True(imported.Ok, imported.Message);
        var newIds = imported.CreatedCategoryIds!;
        Assert.Equal(categories.Count, newIds.Distinct().Count());
        Assert.DoesNotContain(existing, newIds);
        Assert.DoesNotContain(other, newIds);
        Assert.Equal(newIds, imported.CreatedItems!.Select(node => node.CategoryId));
        var byId = group.Children.OfType<KeySenderCategory>().ToDictionary(category => category.Id);
        Assert.Equal(categories.Select(category => category.Description), newIds.Select(id => byId[id].GetDescription()));
    }

    private static (KeySenderSearchGroup Group, ConfigurationService Service) CreateHarness()
    {
        var plugin = new KeySenderPlugin();
        var group = new KeySenderSearchGroup(KeySenderConst.SearchGroupId);
        group.SetParent(plugin);
        return (group, new ConfigurationService(new RecordingMioDataProvider(), plugin));
    }

    private static NodeWrite Write(string name) => new()
    {
        Name = name,
        Sequence = new KeyboardSequence
        {
            Events =
            [
                new KeyboardEvent { EventId = "down", Key = "KeyA", Type = "keyDown" },
                new KeyboardEvent { EventId = "up", Key = "KeyA", Type = "keyUp" },
            ],
        },
    };
}
