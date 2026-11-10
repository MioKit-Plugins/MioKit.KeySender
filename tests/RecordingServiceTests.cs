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

    private static KeyboardHookAction Push(FakeKeyboardHookSource hook, bool up)
        => hook.Push(new() { VkCode = 0xA0, ScanCode = 0x2A, IsKeyUp = up });
}
