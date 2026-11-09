using Xunit;

namespace MioKit.KeySender.Tests;

public sealed class ScaffoldSmokeTests
{
    [Fact]
    public void Plugin_assembly_is_loadable()
    {
        var type = typeof(global::KeySender.KeySenderPlugin);
        Assert.Equal("KeySender", type.Namespace);
        Assert.Equal("KeySenderPlugin", type.Name);
    }
}
