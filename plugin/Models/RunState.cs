namespace KeySender.Models;

/// <summary>Run lifecycle states (design 02 §8).</summary>
public enum RunState
{
    Queued,
    Loading,
    BeforeDelay,
    WaitingExternalKeys,
    EventDelay,
    Sending,
    AfterDelay,
    Cancelling,
    Succeeded,
    Failed,
    Cancelled,
}
