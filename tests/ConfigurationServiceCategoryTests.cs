using KeySender;
using KeySender.Services;
using KeySender.Validation;
using MioKit.KeySender.Tests.Fakes;
using Xunit;

namespace MioKit.KeySender.Tests;

public sealed class ConfigurationServiceCategoryTests
{
    [Fact]
    public async Task CreateCategory_empty_name_rejects_without_side_effects()
    {
        var data = ThrowingMioDataProvider.Create(out var proxy);
        var service = new ConfigurationService(data, plugin: null);

        var result = await service.CreateCategoryAsync("  ");

        Assert.False(result.Ok);
        Assert.Equal(ValidationCodes.InvalidName, result.Code);
        Assert.Equal(0, proxy.CallCount);
    }

    [Fact]
    public async Task RenameCategory_empty_name_rejects_without_side_effects()
    {
        var data = ThrowingMioDataProvider.Create(out var proxy);
        var service = new ConfigurationService(data, plugin: null);

        var result = await service.RenameCategoryAsync(
            Guid.NewGuid().ToString("D").ToUpperInvariant(),
            " ");

        Assert.False(result.Ok);
        Assert.Equal(ValidationCodes.InvalidName, result.Code);
        Assert.Equal(0, proxy.CallCount);
    }

    [Fact]
    public async Task DeleteCategory_missing_returns_not_found_without_side_effects()
    {
        var data = ThrowingMioDataProvider.Create(out var proxy);
        var service = new ConfigurationService(data, plugin: null);

        var result = await service.DeleteCategoryAsync("missing-category");

        Assert.False(result.Ok);
        Assert.Equal(ValidationCodes.CategoryNotFound, result.Code);
        Assert.Equal(0, proxy.CallCount);
    }

    [Fact]
    public async Task MoveNodes_with_builtin_rejects_entire_batch_without_side_effects()
    {
        var data = ThrowingMioDataProvider.Create(out var proxy);
        var service = new ConfigurationService(data, plugin: null);

        var result = await service.MoveNodesAsync(
            [
                Guid.NewGuid().ToString("D").ToUpperInvariant(),
                KeySenderConst.BuiltInCopyNodeId,
            ],
            Guid.NewGuid().ToString("D").ToUpperInvariant());

        Assert.False(result.Ok);
        Assert.Equal(ValidationCodes.BuiltinReadOnly, result.Code);
        Assert.Equal(0, proxy.CallCount);
    }

    [Fact]
    public async Task MoveNodes_missing_target_returns_not_found_without_side_effects()
    {
        var data = ThrowingMioDataProvider.Create(out var proxy);
        var service = new ConfigurationService(data, plugin: null);
        var userId = Guid.NewGuid().ToString("D").ToUpperInvariant();

        var result = await service.MoveNodesAsync([userId], "missing-category");

        Assert.False(result.Ok);
        Assert.Equal(ValidationCodes.CategoryNotFound, result.Code);
        Assert.Equal(0, proxy.CallCount);
    }
}
