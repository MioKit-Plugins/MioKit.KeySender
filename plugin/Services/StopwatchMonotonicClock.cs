using System.Diagnostics;

namespace KeySender.Services;

public sealed class StopwatchMonotonicClock : IMonotonicClock
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();

    public long ElapsedMilliseconds => _watch.ElapsedMilliseconds;
}
