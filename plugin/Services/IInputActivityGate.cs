namespace KeySender.Services;

/// <summary>
/// Recording reservation gate for package E. Rejects enqueue while reserved;
/// reservation itself fails when runs are active/queued or keys are held.
/// </summary>
public interface IInputActivityGate
{
    bool IsRecordingActive { get; }

    /// <summary>
    /// Atomically reserve input for recording when no queued/active run, no held keys,
    /// and sender is available.
    /// </summary>
    bool TryReserveForRecording(out string? rejectCode);

    void EndRecordingReservation();
}
