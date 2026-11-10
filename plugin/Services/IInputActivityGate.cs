namespace KeySender.Services;

/// <summary>
/// Recording reservation gate for package E. Rejects enqueue while reserved;
/// reservation itself fails when runs are active or queued.
/// </summary>
public interface IInputActivityGate
{
    bool IsRecordingActive { get; }

    /// <summary>
    /// Atomically reserve input for recording when no queued/active run, and the plugin is running.
    /// </summary>
    bool TryReserveForRecording(out string? rejectCode);

    void EndRecordingReservation();
}
