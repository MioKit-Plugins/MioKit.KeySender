using KeySender.Services;
using KeySender.Validation;

namespace MioKit.KeySender.Tests.Fakes;

public sealed class FakeInputActivityGate : IInputActivityGate
{
    public bool AllowReserve { get; set; } = true;

    public string? RejectCode { get; set; } = ValidationCodes.ExecutionBusy;

    public int ReserveCalls { get; private set; }

    public int EndCalls { get; private set; }

    public bool IsRecordingActive { get; private set; }

    public bool TryReserveForRecording(out string? rejectCode)
    {
        ReserveCalls++;
        if (!AllowReserve)
        {
            rejectCode = RejectCode;
            return false;
        }

        IsRecordingActive = true;
        rejectCode = null;
        return true;
    }

    public void EndRecordingReservation()
    {
        EndCalls++;
        IsRecordingActive = false;
    }
}
