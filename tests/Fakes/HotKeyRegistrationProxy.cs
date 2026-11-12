using System.Reflection;
using MioKit.Sdk;

namespace MioKit.KeySender.Tests.Fakes;

public class HotKeyRegistrationProxy : DispatchProxy
{
    public IReadOnlyCollection<HotKeyRegistration> Registrations { get; set; } = [];

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name == "get_" + nameof(IGlobalHotKeyService.Registrations)
            ? Registrations
            : throw new NotSupportedException(targetMethod?.Name);
}
