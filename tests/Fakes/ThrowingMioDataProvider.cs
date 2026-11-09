using System.Reflection;
using MioKit.Sdk;

namespace MioKit.KeySender.Tests.Fakes;

/// <summary>
/// DispatchProxy that fails on any call — proves builtin_read_only rejects before side effects.
/// </summary>
internal class ThrowingMioDataProvider : DispatchProxy
{
    public int CallCount { get; private set; }

    public static IMioDataProvider Create(out ThrowingMioDataProvider proxy)
    {
        var created = Create<IMioDataProvider, ThrowingMioDataProvider>();
        proxy = (ThrowingMioDataProvider)(object)created;
        return created;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        CallCount++;
        throw new InvalidOperationException(
            $"Data provider must not be called ({targetMethod?.Name}).");
    }
}
