using KeySender;
using KeySender.Models;
using KeySender.Services;
using KeySender.Validation;
using MioKit.KeySender.Tests.Fakes;
using Xunit;

namespace MioKit.KeySender.Tests;

public sealed class RecordingServiceTests
{
    [Fact]
    public void Stop_immediately_returns_unpaired_events_and_stops_the_hook()
    {
        var gate = new FakeInputActivityGate(); var hook = new FakeKeyboardHookSource();
        using var recorder = new RecordingService(gate, hook, new FakeTargetWindowOps());
        var notifications = new List<RecordingState>();
        recorder.RecordingChanged += (_, snapshot) => notifications.Add(snapshot.State);
        var id = recorder.Start("window").Snapshot!.RecordingId;
        Push(hook, false); Push(hook, false);
        var stop = recorder.Stop(id);
        Assert.Equal(RecordingState.Review, stop.Snapshot!.State);
        Assert.Equal(["keyDown"], stop.Fragment!.Events.Select(e => e.Type));
        Assert.False(hook.IsRunning);
        Assert.False(gate.IsRecordingActive);
        Assert.Equal(RecordingState.Review, notifications.Last());
        Assert.Equal(KeyboardHookAction.PassThrough, Push(hook, true));
        Assert.Single(recorder.GetFragment(id).Fragment!.Events);
        Assert.True(recorder.Apply(id).Ok);
        Assert.Null(recorder.GetSnapshot());
    }

    [Fact]
    public void Repeated_states_are_deduplicated_per_key_across_interleaved_events()
    {
        var hook = new FakeKeyboardHookSource();
        using var recorder = new RecordingService(new FakeInputActivityGate(), hook, new FakeTargetWindowOps());
        var id = recorder.Start("window").Snapshot!.RecordingId;
        Push(hook, true); Push(hook, true); // A first Up is meaningful; its repeats are not.
        Push(hook, false);
        hook.Push(new() { VkCode = 0x41, ScanCode = 0x1E });
        Push(hook, false); // Shift is still down, even after another key's event.
        Push(hook, true); Push(hook, true);
        Push(hook, false); // A new press after release is retained.
        var events = recorder.Stop(id).Fragment!.Events;
        Assert.Equal(["keyUp", "keyDown", "keyDown", "keyUp", "keyDown"], events.Select(e => e.Type));
        Assert.Equal(["LeftShift", "LeftShift", "KeyA", "LeftShift", "LeftShift"], events.Select(e => e.Key));
        recorder.Apply(id);
        id = recorder.Start("window").Snapshot!.RecordingId;
        Push(hook, false); // State is private to this recording session.
        Assert.Single(recorder.Stop(id).Fragment!.Events);
    }

    [Fact]
    public void Records_a_standalone_keyup_and_keeps_default_delays_zero()
    {
        var hook = new FakeKeyboardHookSource();
        using var recorder = new RecordingService(new FakeInputActivityGate(), hook, new FakeTargetWindowOps());
        var id = recorder.Start("window").Snapshot!.RecordingId;
        Push(hook, true);
        var fragment = recorder.Stop(id).Fragment!;
        var ev = Assert.Single(fragment.Events);
        Assert.Equal("keyUp", ev.Type); Assert.Equal(0, ev.DelayBeforeMs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Foreground_loss_stops_recording_and_notifies_without_waiting_for_keys(bool poll)
    {
        var hook = new FakeKeyboardHookSource(); var windows = new FakeTargetWindowOps();
        using var recorder = new RecordingService(new FakeInputActivityGate(), hook, windows);
        RecordingSnapshot? latest = null; recorder.RecordingChanged += (_, snapshot) => latest = snapshot;
        var id = recorder.Start("window").Snapshot!.RecordingId;
        Push(hook, false); windows.Foreground = 0x2000;
        if (poll) recorder.CheckForeground();
        else Assert.Equal(KeyboardHookAction.PassThrough, Push(hook, true));
        Assert.Equal(RecordingState.Review, latest!.State);
        Assert.Contains(ValidationCodes.RecordingInterrupted, latest.Notices);
        Assert.False(hook.IsRunning);
        Assert.Single(recorder.GetFragment(id).Fragment!.Events);
    }

    [Fact]
    public void Cancel_discards_events_and_releases_only_the_recording_reservation()
    {
        var hook = new FakeKeyboardHookSource(); var gate = new FakeInputActivityGate();
        using var recorder = new RecordingService(gate, hook, new FakeTargetWindowOps());
        var id = recorder.Start("window").Snapshot!.RecordingId;
        Push(hook, false);
        Assert.Equal(RecordingState.Cancelled, recorder.Cancel(id).Snapshot!.State);
        Assert.False(hook.IsRunning); Assert.False(gate.IsRecordingActive);
        Assert.False(recorder.GetFragment(id).Ok);
        Assert.True(recorder.Start("window").Ok);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_while_cleanup_is_pending_does_not_stop_the_hook_twice(bool stopToReview)
    {
        var hook = new BlockingStopHook();
        var gate = new FakeInputActivityGate();
        using var recorder = new RecordingService(gate, hook, new FakeTargetWindowOps());
        var oldId = recorder.Start("old").Snapshot!.RecordingId;
        var oldCallback = hook.LastStartedHandler!;
        var first = Task.Run(() => stopToReview ? recorder.Stop(oldId) : recorder.Cancel(oldId));

        try
        {
            await hook.StopStarted.WaitAsync(Timeout);
            Assert.True(recorder.Cancel(oldId).Ok);
            Assert.Equal(1, hook.StopCalls);
            Assert.True(gate.IsRecordingActive);
            Assert.Equal(0, gate.EndCalls);
            Assert.Equal(ValidationCodes.RecordingBusy, recorder.Start("new").Code);
        }
        finally
        {
            hook.AllowStop();
            await first.WaitAsync(Timeout);
        }

        Assert.Equal(1, gate.EndCalls);
        Assert.True(recorder.Start("new").Ok);
        Assert.True(hook.IsRunning);
        Assert.True(gate.IsRecordingActive);
        Assert.Equal(KeyboardHookAction.PassThrough, oldCallback(new() { VkCode = 0x41, ScanCode = 0x1E }));
        Assert.Equal(0, recorder.GetSnapshot()!.EventCount);
    }

    [Fact]
    public async Task Applying_a_fragment_cannot_start_new_recording_during_automatic_hook_cleanup()
    {
        var hook = new BlockingStopHook();
        var gate = new FakeInputActivityGate();
        var windows = new FakeTargetWindowOps();
        using var recorder = new RecordingService(gate, hook, windows);
        var oldId = recorder.Start("old").Snapshot!.RecordingId;
        var callback = hook.LastStartedHandler!;
        var input = new RawKeyboardEvent { VkCode = 0x41, ScanCode = 0x1E };
        Assert.Equal(KeyboardHookAction.Consume, callback(input));
        windows.Foreground = 0x2000;
        var interrupted = Task.Run(() => callback(input));

        try
        {
            await hook.StopStarted.WaitAsync(Timeout);
            Assert.Equal("KeyA", Assert.Single(recorder.Apply(oldId).Fragment!.Events).Key);
            Assert.Null(recorder.GetSnapshot());
            Assert.Equal(ValidationCodes.RecordingBusy, recorder.Start("new").Code);
            Assert.True(gate.IsRecordingActive);
        }
        finally
        {
            hook.AllowStop();
            await interrupted.WaitAsync(Timeout);
        }

        Assert.Equal(KeyboardHookAction.PassThrough, await interrupted);
        Assert.Equal(1, gate.EndCalls);
        Assert.True(recorder.Start("new").Ok);
    }

    [Fact]
    public void Injected_events_are_ignored_and_execution_busy_is_reported()
    {
        var hook = new FakeKeyboardHookSource(); var gate = new FakeInputActivityGate();
        using var recorder = new RecordingService(gate, hook, new FakeTargetWindowOps());
        gate.AllowReserve = false; gate.RejectCode = ValidationCodes.ExecutionBusy;
        Assert.Equal(ValidationCodes.ExecutionBusy, recorder.Start("window").Code);
        gate.AllowReserve = true;
        recorder.Start("window");
        hook.Push(new() { VkCode = 0x41, ScanCode = 0x1E, Injected = true });
        Assert.Equal(0, recorder.GetSnapshot()!.EventCount);
    }

    [Fact]
    public void Hook_install_failure_releases_reservation_and_allows_retry()
    {
        var gate = new FakeInputActivityGate();
        var hook = new FailFirstStartHook();
        using var recorder = new RecordingService(gate, hook, new FakeTargetWindowOps());

        var failed = recorder.Start("window");

        Assert.False(failed.Ok);
        Assert.Equal(ValidationCodes.RecordingUnavailable, failed.Code);
        Assert.False(gate.IsRecordingActive);
        Assert.Equal(1, gate.EndCalls);
        Assert.Null(recorder.GetSnapshot());
        var started = recorder.Start("window");
        Assert.True(started.Ok);
        Assert.True(hook.IsRunning);
        Assert.True(gate.IsRecordingActive);
    }

    [Fact]
    public void Missing_foreground_window_releases_reservation_without_starting_the_hook()
    {
        var gate = new FakeInputActivityGate();
        var hook = new FakeKeyboardHookSource();
        using var recorder = new RecordingService(gate, hook, new FakeTargetWindowOps { Foreground = 0 });

        var failed = recorder.Start("window");

        Assert.Equal(ValidationCodes.RecordingUnavailable, failed.Code);
        Assert.False(hook.IsRunning);
        Assert.False(gate.IsRecordingActive);
        Assert.Null(recorder.GetSnapshot());
    }

    [Fact]
    public void Unsupported_and_lower_integrity_injected_events_pass_through_but_escape_is_recorded()
    {
        var hook = new FakeKeyboardHookSource();
        using var recorder = new RecordingService(new FakeInputActivityGate(), hook, new FakeTargetWindowOps());
        var id = recorder.Start("window").Snapshot!.RecordingId;

        Assert.Equal(KeyboardHookAction.PassThrough, hook.Push(new() { VkCode = 0xFF }));
        Assert.Equal(KeyboardHookAction.PassThrough,
            hook.Push(new() { VkCode = 0x41, ScanCode = 0x1E, LowerIntegrityInjected = true }));
        Assert.Equal(KeyboardHookAction.Consume, hook.Push(new() { VkCode = 0x1B, ScanCode = 0x01 }));
        Assert.Equal(RecordingState.Recording, recorder.GetSnapshot()!.State);

        Assert.Equal("Escape", Assert.Single(recorder.Stop(id).Fragment!.Events).Key);
    }

    [Fact]
    public void Reaching_recording_cap_preserves_the_last_event_and_notifies_review()
    {
        var gate = new FakeInputActivityGate();
        var hook = new FakeKeyboardHookSource();
        using var recorder = new RecordingService(gate, hook, new FakeTargetWindowOps(), new FakeMonotonicClock());
        var notifications = new List<RecordingSnapshot>();
        recorder.RecordingChanged += (_, snapshot) => notifications.Add(snapshot);
        var id = recorder.Start("window").Snapshot!.RecordingId;

        for (var i = 0; i < KeySenderConst.RecordingEventCap - 1; i++)
            Push(hook, up: i % 2 == 1);
        Assert.Equal(RecordingState.Recording, recorder.GetSnapshot()!.State);
        Assert.True(hook.IsRunning);

        Assert.Equal(KeyboardHookAction.Consume, Push(hook, up: true));
        Assert.Equal(KeyboardHookAction.PassThrough, Push(hook, up: false));

        var final = notifications.Last();
        Assert.Equal(RecordingState.Review, final.State);
        Assert.Equal(KeySenderConst.RecordingEventCap, final.EventCount);
        Assert.Equal([ValidationCodes.RecordingLimitReached], final.Notices);
        Assert.False(hook.IsRunning);
        Assert.False(gate.IsRecordingActive);
        var fragment = recorder.GetFragment(id).Fragment!;
        Assert.Equal(KeySenderConst.RecordingEventCap, fragment.Events.Count);
        Assert.Equal(KeySenderConst.EventTypeKeyUp, fragment.Events[^1].Type);
        Assert.Equal(fragment.Events.Count, fragment.Events.Select(e => e.EventId).Distinct().Count());
    }

    [Fact]
    public void Progress_reports_elapsed_time_and_throttles_notifications()
    {
        var clock = new FakeMonotonicClock();
        var hook = new FakeKeyboardHookSource();
        using var recorder = new RecordingService(new FakeInputActivityGate(), hook, new FakeTargetWindowOps(), clock);
        var notifications = new List<RecordingSnapshot>();
        recorder.RecordingChanged += (_, snapshot) => notifications.Add(snapshot);
        var id = recorder.Start("window").Snapshot!.RecordingId;

        clock.Advance(99);
        Push(hook, up: false);
        Assert.Single(notifications);
        Assert.Equal(1, recorder.GetSnapshot()!.EventCount);
        clock.Advance(1);
        Push(hook, up: true);
        clock.Advance(50);
        Push(hook, up: false);
        recorder.Stop(id);

        Assert.Collection(notifications,
            start =>
            {
                Assert.Equal(1, start.SequenceNumber);
                Assert.Equal(0, start.EventCount);
            },
            progress =>
            {
                Assert.Equal(2, progress.SequenceNumber);
                Assert.Equal(100, progress.ElapsedMs);
                Assert.Equal(2, progress.EventCount);
            },
            stop =>
            {
                Assert.Equal(3, stop.SequenceNumber);
                Assert.Equal(150, stop.ElapsedMs);
                Assert.Equal(3, stop.EventCount);
                Assert.Equal(RecordingState.Review, stop.State);
            });
    }

    [Fact]
    public void Empty_fragment_cannot_apply_and_old_ids_cannot_change_the_next_session()
    {
        var gate = new FakeInputActivityGate();
        var hook = new FakeKeyboardHookSource();
        using var recorder = new RecordingService(gate, hook, new FakeTargetWindowOps());
        var old = recorder.Start("first-window").Snapshot!.RecordingId;

        Assert.Equal(ValidationCodes.RecordingNotReady, recorder.Apply(old).Code);
        Assert.Equal(ValidationCodes.RecordingBusy, recorder.Start("second-window").Code);
        Assert.Empty(recorder.Stop(old).Fragment!.Events);
        Assert.Equal(ValidationCodes.EmptySequence, recorder.Apply(old).Code);
        Assert.Equal(ValidationCodes.RecordingBusy, recorder.Start("second-window").Code);
        recorder.Cancel(old);
        var current = recorder.Start("second-window").Snapshot!.RecordingId;

        Assert.True(recorder.Cancel(old).Ok);
        Assert.Equal(ValidationCodes.RecordingNotReady, recorder.Stop(old).Code);
        Assert.True(hook.IsRunning);
        Assert.True(gate.IsRecordingActive);
        Assert.Equal(current, recorder.GetSnapshot()!.RecordingId);
        Assert.Equal("second-window", recorder.GetSnapshot()!.WindowSessionId);
    }

    [Fact]
    public void Review_fragment_can_be_edited_by_the_caller_without_changing_the_recording()
    {
        var hook = new FakeKeyboardHookSource();
        using var recorder = new RecordingService(new FakeInputActivityGate(), hook, new FakeTargetWindowOps());
        var id = recorder.Start("window").Snapshot!.RecordingId;
        Push(hook, up: false);

        var review = recorder.Stop(id).Fragment!;
        var first = Assert.Single(review.Events);
        var eventId = first.EventId;
        first.Key = "KeyA";
        first.DelayBeforeMs = 125;

        var applied = Assert.Single(recorder.Apply(id).Fragment!.Events);
        Assert.Equal(eventId, applied.EventId);
        Assert.Equal("LeftShift", applied.Key);
        Assert.Equal(0, applied.DelayBeforeMs);
        Assert.Null(recorder.GetSnapshot());
    }

    private static KeyboardHookAction Push(FakeKeyboardHookSource hook, bool up)
        => hook.Push(new() { VkCode = 0xA0, ScanCode = 0x2A, IsKeyUp = up });

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class BlockingStopHook : IKeyboardHookSource
    {
        private readonly FakeKeyboardHookSource _source = new();
        private readonly TaskCompletionSource _stopStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _allowStop = new();
        private int _stopCalls;

        public bool IsRunning => _source.IsRunning;
        public Task StopStarted => _stopStarted.Task;
        public int StopCalls => Volatile.Read(ref _stopCalls);
        public Func<RawKeyboardEvent, KeyboardHookAction>? LastStartedHandler { get; private set; }

        public void Start(Func<RawKeyboardEvent, KeyboardHookAction> handler)
        {
            _source.Start(handler);
            LastStartedHandler = handler;
        }

        public void Stop()
        {
            if (Interlocked.Increment(ref _stopCalls) == 1)
            {
                _stopStarted.SetResult();
                if (!_allowStop.Wait(Timeout))
                    throw new TimeoutException("Test did not release the first hook stop.");
            }
            _source.Stop();
        }

        public void AllowStop() => _allowStop.Set();

        public void Dispose()
        {
            _source.Dispose();
            _allowStop.Dispose();
        }
    }

    private sealed class FailFirstStartHook : IKeyboardHookSource
    {
        private readonly FakeKeyboardHookSource _source = new();
        private bool _firstStart = true;

        public bool IsRunning => _source.IsRunning;

        public void Start(Func<RawKeyboardEvent, KeyboardHookAction> handler)
        {
            if (_firstStart)
            {
                _firstStart = false;
                throw new InvalidOperationException("Hook installation failed");
            }
            _source.Start(handler);
        }

        public void Stop() => _source.Stop();
        public void Dispose() => _source.Dispose();
    }
}
