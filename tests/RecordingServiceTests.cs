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
    public void Trajectory_records_downs_ups_with_zero_default_delays()
    {
        var (svc, hook, clock, gate) = Create();
        var start = svc.Start("win-1");
        Assert.True(start.Ok);
        Assert.Equal(RecordingState.Recording, start.Snapshot!.State);
        Assert.True(gate.IsRecordingActive);

        var id = start.Snapshot.RecordingId;
        PushDown(hook, 0x41, 0x1E); // KeyA
        clock.Advance(50);
        PushDown(hook, 0x42, 0x30); // KeyB
        clock.Advance(30);
        PushUp(hook, 0x41, 0x1E);
        clock.Advance(20);
        PushUp(hook, 0x42, 0x30);

        var stop = svc.Stop(id);
        Assert.True(stop.Ok);
        Assert.Equal(RecordingState.Review, stop.Snapshot!.State);
        Assert.Equal(4, stop.Snapshot.EventCount);
        Assert.True(gate.IsRecordingActive); // review still reserves

        var fragment = svc.GetFragment(id).Fragment!;
        Assert.Equal(
            ["keyDown", "keyDown", "keyUp", "keyUp"],
            fragment.Events.Select(e => e.Type).ToArray());
        Assert.Equal(
            ["KeyA", "KeyB", "KeyA", "KeyB"],
            fragment.Events.Select(e => e.Key).ToArray());
        Assert.All(fragment.Events, e => Assert.Equal(0, e.DelayBeforeMs));
    }

    [Fact]
    public void Stop_freezes_fragment_drain_ups_not_appended_allows_down_only()
    {
        var (svc, hook, clock, gate) = Create();
        var start = svc.Start("win-1");
        var id = start.Snapshot!.RecordingId;

        PushDown(hook, 0xA0, 0x2A); // LeftShift
        Assert.Equal(1, svc.GetSnapshot(id)!.EventCount);

        var stop = svc.Stop(id);
        Assert.Equal(RecordingState.Draining, stop.Snapshot!.State);
        Assert.Equal(1, stop.Snapshot.EventCount);

        // Drain Up must not append.
        PushUp(hook, 0xA0, 0x2A);
        svc.TickDrain();
        Assert.Equal(RecordingState.Review, svc.GetSnapshot(id)!.State);

        var fragment = svc.GetFragment(id).Fragment!;
        Assert.Single(fragment.Events);
        Assert.Equal("keyDown", fragment.Events[0].Type);
        Assert.Equal("LeftShift", fragment.Events[0].Key);
        Assert.True(gate.IsRecordingActive);

        // Timeout path also reaches review without appending.
        clock.Advance(1);
    }

    [Fact]
    public void Cannot_start_when_gate_rejects_keys_held()
    {
        var (svc, _, _, gate) = Create();
        gate.AllowReserve = false;
        gate.RejectCode = ValidationCodes.KeysHeld;

        var result = svc.Start("win-1");
        Assert.False(result.Ok);
        Assert.Equal(ValidationCodes.KeysHeld, result.Code);
        Assert.False(gate.IsRecordingActive);
        Assert.Equal(0, gate.EndCalls);
    }

    [Fact]
    public void Cannot_start_when_gate_rejects_execution_busy()
    {
        var (svc, _, _, gate) = Create();
        gate.AllowReserve = false;
        gate.RejectCode = ValidationCodes.ExecutionBusy;

        var result = svc.Start("win-1");
        Assert.False(result.Ok);
        Assert.Equal(ValidationCodes.ExecutionBusy, result.Code);
    }

    [Fact]
    public void Apply_only_in_review_cancel_discards_and_ends_reservation()
    {
        var (svc, hook, clock, gate) = Create();
        var start = svc.Start("win-1");
        var id = start.Snapshot!.RecordingId;

        PushDown(hook, 0x41, 0x1E);
        Assert.False(svc.Apply(id).Ok);
        Assert.Equal(ValidationCodes.RecordingNotReady, svc.Apply(id).Code);

        Assert.True(svc.Stop(id).Ok);
        // Finish drain via timeout so review is reachable without appending Up.
        clock.Advance(KeySenderConst.RecordingDrainTimeoutMs);
        svc.TickDrain();
        Assert.Equal(RecordingState.Review, svc.GetSnapshot(id)!.State);

        var cancel = svc.Cancel(id);
        Assert.True(cancel.Ok);
        Assert.Equal(RecordingState.Cancelled, cancel.Snapshot!.State);
        Assert.False(gate.IsRecordingActive);
        Assert.True(gate.EndCalls >= 1);

        // Apply after cancel fails
        Assert.False(svc.Apply(id).Ok);
    }

    [Fact]
    public void Apply_returns_events_and_ends_reservation_without_writing_eav()
    {
        var (svc, hook, _, gate) = Create();
        var id = svc.Start("win-1").Snapshot!.RecordingId;
        PushDown(hook, 0x43, 0x2E); // KeyC
        PushUp(hook, 0x43, 0x2E);
        Assert.True(svc.Stop(id).Ok);

        var apply = svc.Apply(id);
        Assert.True(apply.Ok);
        Assert.NotNull(apply.Fragment);
        Assert.Equal(2, apply.Fragment!.Events.Count);
        Assert.False(gate.IsRecordingActive);
        Assert.Null(svc.GetSnapshot(id));
    }

    [Fact]
    public void Auto_repeat_downs_are_deduped()
    {
        var (svc, hook, clock, _) = Create();
        var id = svc.Start("win-1").Snapshot!.RecordingId;
        PushDown(hook, 0x41, 0x1E);
        clock.Advance(30);
        PushDown(hook, 0x41, 0x1E);
        clock.Advance(30);
        PushDown(hook, 0x41, 0x1E);
        Assert.True(svc.Stop(id).Ok);
        clock.Advance(KeySenderConst.RecordingDrainTimeoutMs);
        svc.TickDrain();

        var events = svc.GetFragment(id).Fragment!.Events;
        Assert.Single(events);
        Assert.Equal("keyDown", events[0].Type);
        Assert.Equal("KeyA", events[0].Key);
        Assert.Equal(0, events[0].DelayBeforeMs);
    }

    [Fact]
    public void Injected_events_are_not_recorded()
    {
        var (svc, hook, _, _) = Create();
        var id = svc.Start("win-1").Snapshot!.RecordingId;
        hook.Push(new RawKeyboardEvent
        {
            VkCode = 0x41,
            ScanCode = 0x1E,
            IsKeyUp = false,
            Injected = true,
        });
        Assert.Equal(0, svc.GetSnapshot(id)!.EventCount);
        Assert.True(svc.Stop(id).Ok);
        Assert.Equal(RecordingState.Review, svc.GetSnapshot(id)!.State);
    }

    private static (RecordingService Svc, FakeKeyboardHookSource Hook, FakeMonotonicClock Clock, FakeInputActivityGate Gate)
        Create()
    {
        var gate = new FakeInputActivityGate();
        var hook = new FakeKeyboardHookSource();
        var clock = new FakeMonotonicClock();
        var svc = new RecordingService(gate, hook, clock, () => true);
        return (svc, hook, clock, gate);
    }

    private static void PushDown(FakeKeyboardHookSource hook, ushort vk, ushort scan, bool extended = false)
        => hook.Push(new RawKeyboardEvent
        {
            VkCode = vk,
            ScanCode = scan,
            Extended = extended,
            IsKeyUp = false,
        });

    private static void PushUp(FakeKeyboardHookSource hook, ushort vk, ushort scan, bool extended = false)
        => hook.Push(new RawKeyboardEvent
        {
            VkCode = vk,
            ScanCode = scan,
            Extended = extended,
            IsKeyUp = true,
        });
}
