namespace KeySender.Models;

public enum RecordingState
{
    Idle = 0,
    Preparing = 1,
    Recording = 2,
    Draining = 3,
    Review = 4,
    Cancelled = 5,
    Failed = 6,
}
