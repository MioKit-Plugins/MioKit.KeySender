using KeySender.Models;
using KeySender.Validation;

namespace KeySender.Services;

/// <summary>
/// Extracts TargetIdentity from InvokeContext and verifies/restores the original top-level window
/// before each send. Cleanup Ups do not require the target to remain valid.
/// </summary>
public sealed class TargetWindowService
{
    public const int FocusConfirmTimeoutMs = 500;
    public const int FocusPollMs = 10;

    private readonly ITargetWindowOps _ops;
    private readonly IDelayService _delay;

    public TargetWindowService(ITargetWindowOps ops, IDelayService delay)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    public TargetIdentity Extract(InvokeContext context)
    {
        var snapshot = context.Context ?? context.SearchResult?.Request.Context;
        var fg = snapshot?.ForegroundWindow;
        var handle = fg?.Handle;
        var hwnd = handle?.DangerousGetHandle() ?? 0;

        uint? pid = fg?.ProcessId;
        string? className = fg?.ClassName;
        DateTime? startTime = null;

        if (hwnd != 0 && pid is null)
        {
            pid = _ops.GetWindowProcessId(hwnd);
        }

        if (hwnd != 0 && className is null)
        {
            className = _ops.GetClassName(hwnd);
        }

        if (pid is uint p and > 0)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)p);
                startTime = process.StartTime;
            }
            catch
            {
                // Optional identity field; ignore failures.
            }
        }

        return new TargetIdentity
        {
            Hwnd = hwnd,
            ProcessId = pid,
            ProcessStartTime = startTime,
            ClassName = className,
            CaptureTime = snapshot?.CaptureTime,
        };
    }

    /// <summary>
    /// Verify identity and restore foreground to the original HWND.
    /// Returns null on success, or a validation/error code on failure.
    /// </summary>
    public async Task<string?> EnsureForegroundAsync(
        TargetIdentity target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target.IsMissing)
        {
            return ValidationCodes.TargetMissing;
        }

        if (!_ops.IsWindow(target.Hwnd))
        {
            return ValidationCodes.TargetInvalid;
        }

        if (target.ProcessId is uint expectedPid)
        {
            var livePid = _ops.GetWindowProcessId(target.Hwnd);
            if (livePid is null || livePid.Value != expectedPid)
            {
                return ValidationCodes.TargetInvalid;
            }
        }

        if (!string.IsNullOrEmpty(target.ClassName))
        {
            var liveClass = _ops.GetClassName(target.Hwnd);
            if (!string.Equals(liveClass, target.ClassName, StringComparison.Ordinal))
            {
                return ValidationCodes.TargetInvalid;
            }
        }

        if (target.ProcessStartTime is DateTime expectedStart && target.ProcessId is uint pid)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)pid);
                if (process.StartTime != expectedStart)
                {
                    return ValidationCodes.TargetInvalid;
                }
            }
            catch
            {
                return ValidationCodes.TargetInvalid;
            }
        }

        // Host already activates the captured target before InvokeAsync for
        // IRequireTargetWindowFeature (same idea as QuickInput / launcher).
        // Do not ShowWindow/SW_RESTORE here — that changes maximized/fullscreen size.
        // Minimized targets are rare: they usually were not the launch foreground.
        if (_ops.GetForegroundWindow() == target.Hwnd)
        {
            return null;
        }

        _ops.SetForegroundWindow(target.Hwnd);

        var deadline = Environment.TickCount64 + FocusConfirmTimeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_ops.GetForegroundWindow() == target.Hwnd)
            {
                return null;
            }

            await _delay.DelayAsync(FocusPollMs, cancellationToken).ConfigureAwait(false);
        }

        return _ops.GetForegroundWindow() == target.Hwnd
            ? null
            : ValidationCodes.TargetFocusFailed;
    }
}
