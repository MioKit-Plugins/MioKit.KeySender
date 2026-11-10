using System.Diagnostics;
using System.Reflection;
using KeySender.Services;
using Xunit;

namespace MioKit.KeySender.Tests;

public sealed class NativeKeyboardHookTests
{
    [Fact]
    public void Stop_on_callback_thread_does_not_join_itself()
    {
        using var hook = new NativeKeyboardHook();
        long elapsed = long.MaxValue;
        var thread = new Thread(() =>
        {
            typeof(NativeKeyboardHook).GetField("_thread", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(hook, Thread.CurrentThread);
            // No native hook is installed; this exercises the mixed-ID branch of Stop.
            typeof(NativeKeyboardHook).GetField("_threadId", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(hook, uint.MaxValue);
            var watch = Stopwatch.StartNew();
            hook.Stop();
            elapsed = watch.ElapsedMilliseconds;
        }) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(2)), "Hook tried to wait for its own thread.");
        Assert.True(elapsed < 1000);
    }
}
