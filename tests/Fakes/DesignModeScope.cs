using System.Reflection;
using Avalonia.Controls;

namespace MioKit.KeySender.Tests.Fakes;

/// <summary>
/// Selects the SDK design store so a tree can be exercised without a running host.
/// Tests using this scope must join the nonparallel configuration persistence collection.
/// </summary>
internal sealed class DesignModeScope : IDisposable
{
    private readonly MethodInfo _setter;
    private readonly bool _previous;

    private DesignModeScope(MethodInfo setter, bool previous)
    {
        _setter = setter;
        _previous = previous;
    }

    public static DesignModeScope Enter()
    {
        var property = typeof(Design).GetProperty(nameof(Design.IsDesignMode))!;
        var setter = property.GetSetMethod(nonPublic: true)
            ?? throw new InvalidOperationException("Design mode setter is unavailable.");
        var scope = new DesignModeScope(setter, (bool)property.GetValue(null)!);
        setter.Invoke(null, [true]);
        return scope;
    }

    public void Dispose() => _setter.Invoke(null, [_previous]);
}
