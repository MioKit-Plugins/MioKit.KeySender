using KeySender;
using KeySender.Models;
using KeySender.Services;
using KeySender.Validation;
using MioKit.KeySender.Tests.Fakes;
using Xunit;

namespace MioKit.KeySender.Tests;

public sealed class ExportFileTests : IDisposable
{
    private const string SavedId = "saved-node";
    private const string OriginalContent = "existing data the user chose to replace";
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("keysender-export-");

    private string Destination => Path.Combine(_directory.FullName, "export.json");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_node_preserves_the_destination_and_removes_staging_file(bool destinationExists)
    {
        if (destinationExists)
        {
            await File.WriteAllTextAsync(Destination, OriginalContent);
        }

        var result = await ExportFile.WriteAsync(
            new TransferService(CreateConfiguration()), [SavedId, "removed-node"], Destination);

        Assert.False(result.Ok);
        Assert.Equal(ValidationCodes.NodeNotFound, result.Code);
        if (destinationExists)
        {
            Assert.Equal(OriginalContent, await File.ReadAllTextAsync(Destination));
            AssertOnlyDestinationRemains();
        }
        else
        {
            Assert.Empty(_directory.EnumerateFileSystemInfos());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Successful_export_publishes_a_complete_package(bool destinationExists)
    {
        if (destinationExists)
        {
            await File.WriteAllTextAsync(Destination, new string('x', 4096));
        }

        var result = await ExportFile.WriteAsync(new TransferService(CreateConfiguration()), [SavedId], Destination);

        Assert.True(result.Ok);
        Assert.Equal(1, result.ExportedCount);
        var package = TransferPackageParser.Parse(await File.ReadAllBytesAsync(Destination));
        Assert.True(package.CanImport, package.Message);
        var node = Assert.Single(package.Nodes);
        Assert.Equal("保存的序列", node.Name);
        Assert.Equal("KeyA", Assert.Single(node.Sequence.Events).Key);
        AssertOnlyDestinationRemains();
    }

    [Fact]
    public async Task Oversized_export_preserves_the_existing_backup_and_removes_staging_file()
    {
        await File.WriteAllTextAsync(Destination, OriginalContent);
        var events = Enumerable.Range(0, KeySenderConst.EventCountMax)
            .Select(index => new KeyboardEvent
            {
                EventId = $"event-{index}",
                Type = index % 2 == 0 ? KeySenderConst.EventTypeKeyDown : KeySenderConst.EventTypeKeyUp,
                Key = "KeyA",
            }).ToList();
        var configuration = new FakeConfigurationService();
        var nodeIds = new List<string>();
        for (var index = 0; index < 6; index++)
        {
            var saved = await configuration.CreateAsync(new NodeWrite
            {
                Name = $"序列 {index + 1}",
                Sequence = new KeyboardSequence { Events = events },
            });
            Assert.True(saved.Ok, saved.Message);
            nodeIds.Add(saved.NodeId!);
        }

        var result = await ExportFile.WriteAsync(new TransferService(configuration), nodeIds, Destination);

        Assert.False(result.Ok);
        Assert.Equal(ValidationCodes.FileTooLarge, result.Code);
        Assert.Equal(OriginalContent, await File.ReadAllTextAsync(Destination));
        AssertOnlyDestinationRemains();
    }

    [Fact]
    public async Task Cancellation_during_export_preserves_the_destination_and_removes_staging_file()
    {
        await File.WriteAllTextAsync(Destination, OriginalContent);
        using var cancellation = new CancellationTokenSource();
        var transfer = new TransferService(new CancelAfterReadConfiguration(CreateConfiguration(), cancellation));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ExportFile.WriteAsync(transfer, [SavedId], Destination, cancellation.Token));

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(OriginalContent, await File.ReadAllTextAsync(Destination));
        AssertOnlyDestinationRemains();
    }

    [Fact]
    public async Task Failed_replacement_cleans_staging_and_keeps_the_destination()
    {
        Directory.CreateDirectory(Destination);
        var markerPath = Path.Combine(Destination, "existing.txt");
        await File.WriteAllTextAsync(markerPath, OriginalContent);

        var error = await Record.ExceptionAsync(() =>
            ExportFile.WriteAsync(new TransferService(CreateConfiguration()), [SavedId], Destination));

        Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        Assert.Equal(OriginalContent, await File.ReadAllTextAsync(markerPath));
        AssertOnlyDestinationRemains();
    }

    public void Dispose() => _directory.Delete(recursive: true);

    private void AssertOnlyDestinationRemains() =>
        Assert.Equal(Destination, Assert.Single(_directory.EnumerateFileSystemInfos()).FullName);

    private static FakeConfigurationService CreateConfiguration()
    {
        var configuration = new FakeConfigurationService();
        configuration.Details[SavedId] = ConfigHelpers.ToDetail(SavedId, new NodeWrite
        {
            Name = "保存的序列",
            Sequence = new KeyboardSequence
            {
                Events = [new KeyboardEvent { EventId = "event", Type = "keyDown", Key = "KeyA" }],
            },
        }, isBuiltIn: false);
        return configuration;
    }

    private sealed class CancelAfterReadConfiguration(
        IConfigurationService configuration,
        CancellationTokenSource cancellation) : IConfigurationService
    {
        public async Task<ConfigurationResult> GetDetailAsync(
            string nodeId, CancellationToken cancellationToken = default)
        {
            var result = await configuration.GetDetailAsync(nodeId, cancellationToken);
            await cancellation.CancelAsync();
            return result;
        }

        public Task<ConfigurationResult> CreateAsync(
            NodeWrite write, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ConfigurationResult> CreateInCategoryAsync(
            NodeWrite write, string? categoryId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConfigurationResult> ResolveImportCategoryAsync(
            string name, string description, bool allowReuse, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
