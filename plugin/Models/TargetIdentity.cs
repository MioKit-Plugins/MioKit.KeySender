namespace KeySender.Models;

/// <summary>
/// Lightweight original top-level window identity extracted from InvokeContext.
/// Must not retain the full InvocationSnapshot.
/// </summary>
public sealed class TargetIdentity
{
    public nint Hwnd { get; init; }

    public uint? ProcessId { get; init; }

    /// <summary>Optional process start time used to detect HWND reuse.</summary>
    public DateTime? ProcessStartTime { get; init; }

    public string? ClassName { get; init; }

    public DateTimeOffset? CaptureTime { get; init; }

    public bool IsMissing => Hwnd == 0;
}
