using KeySender;
using KeySender.Models;
using KeySender.Services;
using MioKit.KeySender.Tests.Fakes;
using MioKit.Sdk;
using Xunit;

namespace MioKit.KeySender.Tests;

public sealed class ExecutionServiceTests
{
    private static readonly TargetIdentity ValidTarget = new()
    {
        Hwnd = 0x1000,
        ProcessId = 4242,
        ClassName = "FakeWindowClass",
    };

    [Fact]
    public async Task Single_down_succeeds_and_ledger_holds_key()
    {
        var harness = CreateHarness();
        var nodeId = "node-down";
        harness.Configs.Nodes[nodeId] = Write("hold", Down("LeftShift"));

        await harness.Service.EnqueueAsync(nodeId, "hold", ValidTarget, InvokeSource.SearchBox);

        var held = harness.Service.GetHeldKeys();
        Assert.True(harness.Service.SenderAvailable);
        Assert.Single(held.Keys);
        Assert.Equal("LeftShift", held.Keys[0].Key);
        Assert.False(held.Keys[0].Uncertain);

        var recent = harness.Service.GetRecentRuns();
        Assert.Contains(recent, r => r.NodeId == nodeId && r.State == RunState.Succeeded);

        Assert.Equal([("LeftShift", true)], harness.Sender.Submitted);
    }

    [Fact]
    public async Task Second_run_up_releases_held_key()
    {
        var harness = CreateHarness();
        harness.Configs.Nodes["a"] = Write("down", Down("LeftControl"));
        harness.Configs.Nodes["b"] = Write("up", Up("LeftControl"));

        await harness.Service.EnqueueAsync("a", "down", ValidTarget, InvokeSource.SearchBox);
        Assert.Single(harness.Service.GetHeldKeys().Keys);

        await harness.Service.EnqueueAsync("b", "up", ValidTarget, InvokeSource.SearchBox);

        Assert.Empty(harness.Service.GetHeldKeys().Keys);
        Assert.Equal(
            [("LeftControl", true), ("LeftControl", false)],
            harness.Sender.Submitted);
    }

    [Fact]
    public async Task Active_failure_cleans_earlier_held_keys_and_cancels_queued()
    {
        var harness = CreateHarness();
        harness.Configs.Nodes["held"] = Write("held", Down("LeftShift"));
        harness.Configs.Nodes["fail"] = Write(
            "fail",
            beforeDelayMs: 50,
            Down("KeyA"));
        harness.Configs.Nodes["queued"] = Write("queued", Down("KeyB"));

        await harness.Service.EnqueueAsync("held", "held", ValidTarget, InvokeSource.SearchBox);
        Assert.Single(harness.Service.GetHeldKeys().Keys);

        harness.Sender.Resolver = (_, down, _) =>
            down ? KeyboardSendResult.Failed(5) : KeyboardSendResult.Ok();

        var failTask = harness.Service.EnqueueAsync("fail", "fail", ValidTarget, InvokeSource.SearchBox);
        var queuedTask = harness.Service.EnqueueAsync("queued", "queued", ValidTarget, InvokeSource.SearchBox);

        await WaitForAsync(() => harness.Delay.CurrentGate is not null);
        Assert.Equal(1, harness.Service.GetActiveAndQueuedRuns().Count(r => r.State == RunState.Queued));

        harness.Delay.Release();

        await Assert.ThrowsAsync<InvalidOperationException>(() => failTask);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queuedTask);

        Assert.Empty(harness.Service.GetHeldKeys().Keys);
        Assert.Contains(harness.Sender.Submitted, e => e is ("LeftShift", false));
        Assert.DoesNotContain(harness.Sender.Submitted, e => e is ("KeyB", true));
    }

    [Fact]
    public async Task Cancel_queued_only_does_not_clear_ledger()
    {
        var harness = CreateHarness();
        harness.Configs.Nodes["held"] = Write("held", Down("LeftAlt"));
        harness.Configs.Nodes["active"] = Write("active", beforeDelayMs: 50, Down("KeyZ"));
        harness.Configs.Nodes["queued"] = Write("queued", Down("KeyY"));

        await harness.Service.EnqueueAsync("held", "held", ValidTarget, InvokeSource.SearchBox);
        Assert.Single(harness.Service.GetHeldKeys().Keys);

        var activeTask = harness.Service.EnqueueAsync("active", "active", ValidTarget, InvokeSource.SearchBox);
        var queuedTask = harness.Service.EnqueueAsync("queued", "queued", ValidTarget, InvokeSource.SearchBox);

        await WaitForAsync(() => harness.Service.GetActiveAndQueuedRuns().Any(r => r.State == RunState.Queued));
        var queued = harness.Service.GetActiveAndQueuedRuns().Single(r => r.State == RunState.Queued);
        Assert.True(harness.Service.TryCancel(queued.RunId));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queuedTask);

        harness.Delay.Release();
        await activeTask;

        var held = harness.Service.GetHeldKeys().Keys;
        Assert.Contains(held, k => k.Key == "LeftAlt");
        Assert.DoesNotContain(harness.Sender.Submitted, e => e is ("LeftAlt", false));
    }

    [Fact]
    public async Task ReleaseAll_with_no_active_run_clears_held_keys_via_ups()
    {
        var harness = CreateHarness();
        harness.Configs.Nodes["a"] = Write("a", Down("LeftShift"), Down("LeftControl"));

        await harness.Service.EnqueueAsync("a", "a", ValidTarget, InvokeSource.SearchBox);
        Assert.Equal(2, harness.Service.GetHeldKeys().Keys.Count);

        var beforeUps = harness.Sender.Submitted.Count;
        var snapshot = await harness.Service.ReleaseAllAsync();

        Assert.Empty(snapshot.Keys);
        Assert.True(snapshot.SenderAvailable);
        Assert.False(snapshot.IsReleasing);

        var ups = harness.Sender.Submitted.Skip(beforeUps).ToArray();
        Assert.Equal(2, ups.Length);
        Assert.All(ups, e => Assert.False(e.KeyDown));
        // Reverse hold order: Control then Shift.
        Assert.Equal("LeftControl", ups[0].Key);
        Assert.Equal("LeftShift", ups[1].Key);
    }

    [Fact]
    public async Task Successful_completion_does_not_emit_automatic_keyup()
    {
        var harness = CreateHarness();
        harness.Configs.Nodes["only-down"] = Write("only-down", Down("LeftShift"));

        await harness.Service.EnqueueAsync("only-down", "only-down", ValidTarget, InvokeSource.SearchBox);

        Assert.Equal([("LeftShift", true)], harness.Sender.Submitted);
        Assert.Single(harness.Service.GetHeldKeys().Keys);
        Assert.Contains(harness.Service.GetRecentRuns(), r => r.State == RunState.Succeeded);
    }

    [Fact]
    public async Task Repeated_down_does_not_double_count_in_ledger()
    {
        var harness = CreateHarness();
        harness.Configs.Nodes["rep"] = Write(
            "rep",
            Down("LeftShift"),
            Down("LeftShift"));

        await harness.Service.EnqueueAsync("rep", "rep", ValidTarget, InvokeSource.SearchBox);

        Assert.Single(harness.Service.GetHeldKeys().Keys);
        Assert.Equal(
            [("LeftShift", true), ("LeftShift", true)],
            harness.Sender.Submitted);
    }

    private static Harness CreateHarness()
    {
        var sender = new FakeKeyboardSender();
        var delay = new GateDelayService();
        var configs = new DictionaryRunConfigurationSource();
        var ops = new FakeTargetWindowOps();
        var targets = new TargetWindowService(ops, delay);
        var ledger = new HeldKeyLedger();
        var service = new ExecutionService(
            ledger,
            sender,
            targets,
            configs,
            delay,
            new NoOpExternalKeyPrep(),
            tasks: null,
            hotkeys: null,
            logger: null);
        return new Harness(service, sender, delay, configs, ledger);
    }

    private static NodeWrite Write(string name, params KeyboardEvent[] events)
        => Write(name, beforeDelayMs: 0, events);

    private static NodeWrite Write(string name, int beforeDelayMs, params KeyboardEvent[] events)
        => new()
        {
            Name = name,
            Description = "",
            BeforeDelayMs = beforeDelayMs,
            AfterDelayMs = 0,
            Sequence = new KeyboardSequence
            {
                Version = KeySenderConst.SequenceVersion,
                Events = events.ToList(),
            },
        };

    private static KeyboardEvent Down(string key) => new()
    {
        EventId = Guid.NewGuid().ToString("D"),
        Type = KeySenderConst.EventTypeKeyDown,
        Key = key,
        DelayBeforeMs = 0,
    };

    private static KeyboardEvent Up(string key) => new()
    {
        EventId = Guid.NewGuid().ToString("D"),
        Type = KeySenderConst.EventTypeKeyUp,
        Key = key,
        DelayBeforeMs = 0,
    };

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < timeoutMs)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Condition not met in time.");
    }

    private sealed record Harness(
        ExecutionService Service,
        FakeKeyboardSender Sender,
        GateDelayService Delay,
        DictionaryRunConfigurationSource Configs,
        HeldKeyLedger Ledger);
}
