using KeySender.Services;

namespace MioKit.KeySender.Tests.Fakes;

public sealed class FakeMonotonicClock : IMonotonicClock
{
    public long ElapsedMilliseconds { get; private set; }

    public void Advance(long ms)
    {
        if (ms < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ms));
        }

        ElapsedMilliseconds += ms;
    }

    public void Set(long ms) => ElapsedMilliseconds = ms;
}
