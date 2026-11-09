namespace KeySender.Services;

public interface IMonotonicClock
{
    long ElapsedMilliseconds { get; }
}
