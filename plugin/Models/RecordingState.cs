namespace KeySender.Models;

public enum RecordingState
{
    Idle = 0,
    Recording = 2,
    Review = 4,
    Cancelled = 5,
    Failed = 6,
}
