using System.Reflection;
using KeySender;
using KeySender.Models;
using KeySender.Services;
using KeySender.Validation;
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

    [Fact]
    public async Task Before_each_event_and_after_delays_wrap_the_original_send_order()
    {
        var steps = new List<string>();
        var delay = new CallbackDelay(ms => steps.Add($"delay:{ms}"));
        var sender = new FakeKeyboardSender
        {
            Resolver = (key, down, _) =>
            {
                steps.Add($"{key.Token}:{(down ? "down" : "up")}");
                return KeyboardSendResult.Ok();
            },
        };
        var configs = new DictionaryRunConfigurationSource();
        var write = Write(Down("LeftControl"), Down("KeyC"), Up("KeyC"), Up("LeftControl"));
        write.BeforeDelayMs = 200;
        write.Sequence.Events[1].DelayBeforeMs = 50;
        write.Sequence.Events[2].DelayBeforeMs = 30;
        write.Sequence.Events[3].DelayBeforeMs = 20;
        write.AfterDelayMs = 300;
        configs.Nodes["copy"] = write;
        var service = new ExecutionService(sender, new(new FakeTargetWindowOps(), delay), configs, delay);

        await service.EnqueueAsync("copy", null, Target, InvokeSource.SearchBox).WaitAsync(Timeout);

        Assert.Equal(
            ["delay:200", "delay:0", "LeftControl:down", "delay:50", "KeyC:down",
                "delay:30", "KeyC:up", "delay:20", "LeftControl:up", "delay:300"],
            steps);
        var run = Assert.Single(service.GetRecentRuns());
        Assert.Equal("test", run.NodeName);
        Assert.Equal(4, run.EventCount);
        Assert.Equal(3, run.EventIndex);
        Assert.Equal(RunState.Succeeded, run.State);
    }

    [Fact]
    public async Task Queue_holds_64_waiting_runs_and_drains_in_FIFO_order()
    {
        var h = Create();
        var activeConfig = Write(Down("KeyA"), Up("KeyA"));
        activeConfig.BeforeDelayMs = 1;
        h.Configs.Nodes["active"] = activeConfig;
        var queued = Enumerable.Range(0, ExecutionService.MaxQueued)
            .Select(i => (Id: $"queued-{i}", Key: $"F{(i % 24) + 1}"))
            .ToArray();
        foreach (var item in queued)
            h.Configs.Nodes[item.Id] = Write(Down(item.Key), Up(item.Key));

        var active = h.Service.EnqueueAsync("active", null, Target, InvokeSource.SearchBox);
        try
        {
            await WaitFor(() => h.Delay.CurrentGate is not null);
            var waiting = queued.Select(item =>
                h.Service.EnqueueAsync(item.Id, null, Target, InvokeSource.SearchBox)).ToArray();

            var snapshots = h.Service.GetActiveAndQueuedRuns();
            Assert.Equal(65, snapshots.Count);
            Assert.Equal(RunState.BeforeDelay, snapshots[0].State);
            Assert.Null(snapshots[0].QueuePosition);
            Assert.Equal(queued.Select(item => item.Id), snapshots.Skip(1).Select(run => run.NodeId));
            Assert.Equal(Enumerable.Range(0, 64).Select(i => (int?)i),
                snapshots.Skip(1).Select(run => run.QueuePosition));
            var overflow = Assert.Throws<InvalidOperationException>(() =>
            {
                _ = h.Service.EnqueueAsync("overflow", null, Target, InvokeSource.SearchBox);
            });
            Assert.StartsWith(ValidationCodes.QueueFull + ":", overflow.Message);
            Assert.False(h.Service.TryReserveForRecording(out var rejection));
            Assert.Equal(ValidationCodes.ExecutionBusy, rejection);

            h.Delay.Release();
            await Task.WhenAll(waiting.Prepend(active)).WaitAsync(Timeout);

            var expected = queued.Select(item => item.Key).Prepend("KeyA")
                .SelectMany(key => new[] { (key, true), (key, false) });
            Assert.Equal(expected, h.Sender.Submitted);
            Assert.Equal(queued.Reverse().Take(50).Select(item => item.Id),
                h.Service.GetRecentRuns().Select(run => run.NodeId));
        }
        finally
        {
            await h.Service.StopAsync().WaitAsync(Timeout);
        }
    }

    [Fact]
    public async Task Queued_run_reads_current_config_when_it_starts()
    {
        var h = Create();
        var first = Write(Down("KeyA"), Up("KeyA"));
        first.BeforeDelayMs = 1;
        h.Configs.Nodes["active"] = first;
        h.Configs.Nodes["queued"] = Write(Down("KeyB"), Up("KeyB"));

        var active = h.Service.EnqueueAsync("active", null, Target, InvokeSource.SearchBox);
        try
        {
            await WaitFor(() => h.Delay.CurrentGate is not null);
            var queued = h.Service.EnqueueAsync("queued", null, Target, InvokeSource.SearchBox);
            h.Configs.Nodes["active"] = Write(Down("KeyX"), Up("KeyX"));
            h.Configs.Nodes["queued"] = Write(Down("KeyY"), Up("KeyY"));

            h.Delay.Release();
            await Task.WhenAll(active, queued).WaitAsync(Timeout);

            Assert.Equal([("KeyA", true), ("KeyA", false), ("KeyY", true), ("KeyY", false)], h.Sender.Submitted);
        }
        finally
        {
            await h.Service.StopAsync().WaitAsync(Timeout);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_cancellation_interrupts_before_or_after_delay(bool afterSend)
    {
        var h = Create();
        var write = Write(Down("KeyA"));
        if (afterSend) write.AfterDelayMs = 1;
        else write.BeforeDelayMs = 1;
        h.Configs.Nodes["n"] = write;
        using var caller = new CancellationTokenSource();

        var task = h.Service.EnqueueAsync("n", null, Target, InvokeSource.SearchBox, caller.Token);
        try
        {
            await WaitFor(() => h.Delay.CurrentGate is not null);
            Assert.Equal(afterSend ? RunState.AfterDelay : RunState.BeforeDelay,
                Assert.Single(h.Service.GetActiveAndQueuedRuns()).State);

            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(Timeout));

            Assert.Equal(afterSend ? new[] { ("KeyA", true) } : [], h.Sender.Submitted);
            var run = Assert.Single(h.Service.GetRecentRuns());
            Assert.Equal(RunState.Cancelled, run.State);
            Assert.True(run.IsCancellationRequested);
        }
        finally
        {
            await h.Service.StopAsync().WaitAsync(Timeout);
        }
    }

    [Fact]
    public async Task Missing_invalid_and_recycled_targets_prevent_sends()
    {
        var h = Create();
        h.Configs.Nodes["invalid"] = Write(Down("MouseLeft"));
        h.Configs.Nodes["valid"] = Write(Down("KeyA"));
        var missingTarget = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = h.Service.EnqueueAsync("valid", null, new(), InvokeSource.SearchBox);
        });
        Assert.StartsWith(ValidationCodes.TargetMissing + ":", missingTarget.Message);

        foreach (var (id, target, code) in new[]
        {
            ("missing", Target, ValidationCodes.NodeNotFound),
            ("invalid", Target, ValidationCodes.UnsupportedKey),
            ("valid", new TargetIdentity { Hwnd = Target.Hwnd, ProcessId = 123 }, ValidationCodes.TargetInvalid),
            ("valid", new TargetIdentity { Hwnd = Target.Hwnd, ClassName = "OtherClass" }, ValidationCodes.TargetInvalid),
        })
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                h.Service.EnqueueAsync(id, null, target, InvokeSource.SearchBox).WaitAsync(Timeout));
            Assert.StartsWith(code + ":", error.Message);
        }

        Assert.Empty(h.Sender.Submitted);
        Assert.All(h.Service.GetRecentRuns(), run => Assert.Equal(RunState.Failed, run.State));
    }

    [Fact]
    public async Task Playback_returns_to_the_original_target_between_events()
    {
        var h = Create();
        h.Configs.Nodes["n"] = Write(Down("KeyA"), Up("KeyA"));
        var foregroundAtSend = new List<nint>();
        h.Sender.Resolver = (_, down, _) =>
        {
            foregroundAtSend.Add(h.Windows.Foreground);
            if (down) h.Windows.Foreground = 0x2000;
            return KeyboardSendResult.Ok();
        };
        h.Windows.Foreground = 0x3000;

        await h.Service.EnqueueAsync("n", null, Target, InvokeSource.SearchBox).WaitAsync(Timeout);

        Assert.Equal([Target.Hwnd, Target.Hwnd], foregroundAtSend);
        Assert.Equal([("KeyA", true), ("KeyA", false)], h.Sender.Submitted);
    }

    [Fact]
    public async Task Losing_focus_mid_run_fails_when_the_original_target_cannot_be_reactivated()
    {
        var windows = new FakeTargetWindowOps { AllowFocusChange = false };
        var delay = new TaskDelayService();
        var sender = new FakeKeyboardSender
        {
            Resolver = (_, _, _) =>
            {
                windows.Foreground = 0x2000;
                return KeyboardSendResult.Ok();
            },
        };
        var configs = new DictionaryRunConfigurationSource();
        configs.Nodes["n"] = Write(Down("KeyA"), Up("KeyA"));
        var service = new ExecutionService(sender, new(windows, delay), configs, delay);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EnqueueAsync("n", null, Target, InvokeSource.SearchBox).WaitAsync(Timeout));

        Assert.StartsWith(ValidationCodes.TargetFocusFailed + ":", error.Message);
        Assert.Equal([("KeyA", true)], sender.Submitted);
        Assert.Equal(1, Assert.Single(service.GetRecentRuns()).EventIndex);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Registered_hotkey_conflicts_stop_playback_before_the_matching_event(bool registeredDuringPlayback)
    {
        var hotkeys = DispatchProxy.Create<IGlobalHotKeyService, HotKeyRegistrationProxy>();
        var registry = (HotKeyRegistrationProxy)(object)hotkeys;
        var registration = new HotKeyRegistration("shortcut", null,
            new HotKeyInfo(HotKeyInfo.TypeExactMatch, KeyCode.Control, KeyCode.K),
            SuppressEvent: true, StopPropagation: true, Status: HotKeyStatus.Registered);
        var h = Create(hotkeys: hotkeys);
        h.Configs.Nodes["n"] = Write(Down("RightControl"), Down("KeyK"), Up("KeyK"), Up("RightControl"));
        if (registeredDuringPlayback)
        {
            h.Sender.Resolver = (_, _, _) =>
            {
                registry.Registrations = [registration];
                return KeyboardSendResult.Ok();
            };
        }
        else
        {
            registry.Registrations = [registration];
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Service.EnqueueAsync("n", null, Target, InvokeSource.SearchBox).WaitAsync(Timeout));

        Assert.StartsWith(ValidationCodes.OutputHotkeyConflict + ":", error.Message);
        Assert.Equal(registeredDuringPlayback ? new[] { ("RightControl", true) } : [], h.Sender.Submitted);
        Assert.Equal(ValidationCodes.OutputHotkeyConflict, Assert.Single(h.Service.GetRecentRuns()).Error);
        Assert.Equal(registration, Assert.Single(registry.Registrations));
    }

    [Fact]
    public async Task Ambiguous_native_failure_does_not_retry_or_send_later_events()
    {
        var h = Create();
        h.Configs.Nodes["n"] = Write(Down("KeyA"), Up("KeyA"));
        h.Sender.Resolver = (_, _, _) => throw new InvalidOperationException("Native submission status unknown");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Service.EnqueueAsync("n", null, Target, InvokeSource.SearchBox).WaitAsync(Timeout));

        Assert.StartsWith(ValidationCodes.InputSendFailed + ":", error.Message);
        Assert.Equal([("KeyA", true)], h.Sender.Submitted);
        var run = Assert.Single(h.Service.GetRecentRuns());
        Assert.Equal(RunState.Failed, run.State);
        Assert.Equal(0, run.EventIndex);
    }

    [Fact]
    public async Task Recording_reservation_excludes_playback_and_shutdown_rejects_new_work_until_restart()
    {
        var h = Create();
        h.Configs.Nodes["n"] = Write(Down("KeyA"), Up("KeyA"));
        Assert.True(h.Service.TryReserveForRecording(out _));
        Assert.False(h.Service.TryReserveForRecording(out var busy));
        Assert.Equal(ValidationCodes.RecordingBusy, busy);
        var rejectedWhileRecording = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = h.Service.EnqueueAsync("n", null, Target, InvokeSource.SearchBox);
        });
        Assert.StartsWith(ValidationCodes.RecordingBusy + ":", rejectedWhileRecording.Message);

        h.Service.EndRecordingReservation();
        await h.Service.StopAsync().WaitAsync(Timeout);
        Assert.False(h.Service.TryReserveForRecording(out var stopping));
        Assert.Equal(ValidationCodes.PluginStopping, stopping);
        var rejectedWhileStopped = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = h.Service.EnqueueAsync("n", null, Target, InvokeSource.SearchBox);
        });
        Assert.StartsWith(ValidationCodes.PluginStopping + ":", rejectedWhileStopped.Message);
        Assert.Empty(h.Sender.Submitted);

        await h.Service.StartAsync();
        await h.Service.EnqueueAsync("n", null, Target, InvokeSource.SearchBox).WaitAsync(Timeout);
        Assert.Equal([("KeyA", true), ("KeyA", false)], h.Sender.Submitted);
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static (ExecutionService Service, FakeKeyboardSender Sender, GateDelayService Delay,
        DictionaryRunConfigurationSource Configs, FakeTargetWindowOps Windows) Create(
        IMioTaskService? tasks = null, IGlobalHotKeyService? hotkeys = null)
    {
        var sender = new FakeKeyboardSender(); var delay = new GateDelayService();
        var configs = new DictionaryRunConfigurationSource();
        var windows = new FakeTargetWindowOps();
        return (new(sender, new(windows, delay), configs, delay, tasks, hotkeys), sender, delay, configs, windows);
    }

    private static NodeWrite Write(params KeyboardEvent[] events) => new()
    {
        Name = "test", Description = "", Sequence = new() { Events = events.ToList() },
    };
    private static KeyboardEvent Down(string key) => new() { EventId = Guid.NewGuid().ToString(), Type = "keyDown", Key = key };
    private static KeyboardEvent Up(string key) => new() { EventId = Guid.NewGuid().ToString(), Type = "keyUp", Key = key };
    private static async Task WaitFor(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class CallbackDelay(Action<int> onDelay) : IDelayService
    {
        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onDelay(milliseconds);
            return Task.CompletedTask;
        }
    }
}

public class ManagedTaskProxy : DispatchProxy
{
    public CancellationToken Token { get; set; }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
        => method!.Name == "RunAsync" && args![1] is Func<CancellationToken, Task> run
            ? run(Token) : throw new NotSupportedException(method.Name);
}
