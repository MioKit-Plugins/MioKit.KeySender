using KeySender;
using KeySender.BuiltIn;
using KeySender.Models;
using KeySender.Services;
using KeySender.Validation;
using MioKit.KeySender.Tests.Fakes;
using Xunit;

namespace MioKit.KeySender.Tests;

public sealed class ConfigurationServiceBuiltinTests
{
    [Fact]
    public async Task Update_builtin_returns_builtin_read_only_without_side_effects()
    {
        var data = ThrowingMioDataProvider.Create(out var proxy);
        var service = new ConfigurationService(data, plugin: null);
        var write = BuiltinSequenceFactory.CreateWrite(KeySenderConst.BuiltInCopyNodeId);

        var result = await service.UpdateAsync(
            KeySenderConst.BuiltInCopyNodeId.ToLowerInvariant(),
            write);

        Assert.False(result.Ok);
        Assert.Equal(ValidationCodes.BuiltinReadOnly, result.Code);
        Assert.Equal(0, proxy.CallCount);
    }

    [Fact]
    public async Task Delete_batch_with_builtin_rejects_entire_batch_without_side_effects()
    {
        var data = ThrowingMioDataProvider.Create(out var proxy);
        var service = new ConfigurationService(data, plugin: null);

        var result = await service.DeleteAsync(
        [
            Guid.NewGuid().ToString("D").ToUpperInvariant(),
            KeySenderConst.BuiltInPasteNodeId.ToLowerInvariant(),
        ]);

        Assert.False(result.Ok);
        Assert.Equal(ValidationCodes.BuiltinReadOnly, result.Code);
        Assert.Equal(0, proxy.CallCount);
    }

    [Fact]
    public async Task GetDetail_builtin_uses_factory_without_data_provider()
    {
        var data = ThrowingMioDataProvider.Create(out var proxy);
        var service = new ConfigurationService(data, plugin: null);

        var result = await service.GetDetailAsync(KeySenderConst.BuiltInCutNodeId);

        Assert.True(result.Ok);
        Assert.NotNull(result.Detail);
        Assert.True(result.Detail!.IsBuiltIn);
        Assert.True(result.Detail.IsReadOnly);
        Assert.False(result.Detail.CanEdit);
        Assert.False(result.Detail.CanDelete);
        Assert.Equal("剪切", result.Detail.Name);
        Assert.Equal(4, result.Detail.Sequence.Events.Count);
        Assert.Equal(0, proxy.CallCount);
    }
}
