using System.Reflection;
using KeySender;
using KeySender.Models;
using KeySender.Services;
using MioKit.KeySender.Tests.Fakes;
using MioKit.Sdk;
using Xunit;

namespace MioKit.KeySender.Tests;

public sealed class ExecutionServiceTests
{
    private static readonly TargetIdentity Target = new() { Hwnd = 0x1000 };

    [Fact]
    public async Task Unpaired_and_repeated_events_are_sent_exactly_as_configured()
    {
        var h = Create();
        h.Configs.Nodes["n"] = Write(Down("LeftShift"), Down("LeftShift"), Up("KeyA"));
        await h.Service.EnqueueAsync("n", "n", Target, InvokeSource.SearchBox);
        Assert.Equal([("LeftShift", true), ("LeftShift", true), ("KeyA", false)], h.Sender.Submitted);
        Assert.Equal(RunState.Succeeded, Assert.Single(h.Service.GetRecentRuns()).State);
        Assert.True(h.Service.TryReserveForRecording(out _));
        h.Service.EndRecordingReservation();
    }

    [Fact]
    public async Task Separate_nodes_can_explicitly_press_and_release_a_key()
    {
        var h = Create();
        h.Configs.Nodes["down"] = Write(Down("LeftControl"));
        h.Configs.Nodes["up"] = Write(Up("LeftControl"));
        await h.Service.EnqueueAsync("down", "down", Target, InvokeSource.SearchBox);
        await h.Service.EnqueueAsync("up", "up", Target, InvokeSource.SearchBox);
        Assert.Equal([("LeftControl", true), ("LeftControl", false)], h.Sender.Submitted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Managed_and_local_cancellation_stop_future_events_without_keyups(bool managed)
    {
        using var hostCancellation = new CancellationTokenSource();
        var tasks = managed ? DispatchProxy.Create<IMioTaskService, ManagedTaskProxy>() : null;
        if (tasks is not null) ((ManagedTaskProxy)(object)tasks).Token = hostCancellation.Token;
        var h = Create(tasks);
        var delayed = Up("LeftShift"); delayed.DelayBeforeMs = 50;
        h.Configs.Nodes["n"] = Write(Down("LeftShift"), delayed);
        var run = h.Service.EnqueueAsync("n", "n", Target, InvokeSource.SearchBox);
        await WaitFor(() => h.Delay.CurrentGate is not null);
        if (managed) hostCancellation.Cancel();
        else Assert.True(h.Service.TryCancel(h.Service.GetActiveAndQueuedRuns().Single().RunId));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal([("LeftShift", true)], h.Sender.Submitted);
        await h.Service.StopAsync();
        Assert.Equal([("LeftShift", true)], h.Sender.Submitted);
    }

    [Fact]
    public async Task Failure_does_not_release_previous_keys_or_cancel_other_requests()
    {
        var h = Create();
        h.Configs.Nodes["down"] = Write(Down("LeftShift"));
        await h.Service.EnqueueAsync("down", "down", Target, InvokeSource.SearchBox);
        h.Configs.Nodes["fail"] = Write(Down("KeyA")); h.Configs.Nodes["fail"].BeforeDelayMs = 50;
        h.Configs.Nodes["next"] = Write(Down("KeyB"));
        h.Sender.Resolver = (key, _, _) => key.Token == "KeyA" ? KeyboardSendResult.Failed(5) : KeyboardSendResult.Ok();
        var failure = h.Service.EnqueueAsync("fail", "fail", Target, InvokeSource.SearchBox);
        await WaitFor(() => h.Delay.CurrentGate is not null);
        var next = h.Service.EnqueueAsync("next", "next", Target, InvokeSource.SearchBox);
        h.Delay.Release();
        await Assert.ThrowsAsync<InvalidOperationException>(() => failure);
        await next;
        Assert.DoesNotContain(h.Sender.Submitted, e => !e.KeyDown);
        Assert.Contains(h.Sender.Submitted, e => e is ("KeyB", true));
    }

    [Fact]
    public async Task Queued_cancellation_is_isolated_and_shutdown_does_not_release_keys()
    {
        var h = Create();
        h.Configs.Nodes["active"] = Write(Down("LeftAlt"), Up("LeftAlt"));
        h.Configs.Nodes["active"].Sequence.Events[1].DelayBeforeMs = 50;
        h.Configs.Nodes["queued"] = Write(Down("KeyB"));
        var active = h.Service.EnqueueAsync("active", "active", Target, InvokeSource.SearchBox);
        await WaitFor(() => h.Delay.CurrentGate is not null);
        var queued = h.Service.EnqueueAsync("queued", "queued", Target, InvokeSource.SearchBox);
        var queuedId = h.Service.GetActiveAndQueuedRuns().Single(r => r.State == RunState.Queued).RunId;
        Assert.True(h.Service.TryCancel(queuedId));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.False(active.IsCompleted);
        await h.Service.StopAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        Assert.Equal([("LeftAlt", true)], h.Sender.Submitted);
    }

    private static (ExecutionService Service, FakeKeyboardSender Sender, GateDelayService Delay, DictionaryRunConfigurationSource Configs) Create(IMioTaskService? tasks = null)
    {
        var sender = new FakeKeyboardSender(); var delay = new GateDelayService();
        var configs = new DictionaryRunConfigurationSource();
        return (new(sender, new(new FakeTargetWindowOps(), delay), configs, delay, tasks), sender, delay, configs);
    }

    private static NodeWrite Write(params KeyboardEvent[] events) => new()
    {
        Name = "test", Description = "", Sequence = new() { Events = events.ToList() },
    };
    private static KeyboardEvent Down(string key) => new() { EventId = Guid.NewGuid().ToString(), Type = "keyDown", Key = key };
    private static KeyboardEvent Up(string key) => new() { EventId = Guid.NewGuid().ToString(), Type = "keyUp", Key = key };
    private static async Task WaitFor(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
}

public class ManagedTaskProxy : DispatchProxy
{
    public CancellationToken Token { get; set; }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
        => method!.Name == "RunAsync" && args![1] is Func<CancellationToken, Task> run
            ? run(Token) : throw new NotSupportedException(method.Name);
}
