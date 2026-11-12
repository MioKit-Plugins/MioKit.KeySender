using KeySender;
using KeySender.Models;
using KeySender.Validation;
using Xunit;

namespace MioKit.KeySender.Tests;

public sealed class SequenceAnalyzerTests
{
    [Fact]
    public void T01_single_keydown_is_valid_structure_with_unclosed_warning()
    {
        var write = CreateWrite(Down("e1", "LeftControl"));

        var result = SequenceAnalyzer.AnalyzeSequence(write);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Code == ValidationCodes.UnclosedKeyDown && w.Key == "LeftControl");
        Assert.Equal(
            "24341a9bc395181487a27ee060c48600cce0076db2938e3b43195bc4b72586ed",
            result.WarningFingerprint);
    }

    [Fact]
    public void T02_single_keyup_is_valid_structure_with_unmatched_warning()
    {
        var write = CreateWrite(Up("e1", "LeftControl"));

        var result = SequenceAnalyzer.AnalyzeSequence(write);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Code == ValidationCodes.UnmatchedKeyUp && w.Key == "LeftControl");
        Assert.False(string.IsNullOrWhiteSpace(result.WarningFingerprint));
    }

    [Fact]
    public void Pairing_down_down_up_is_not_reference_counted()
    {
        var write = CreateWrite(
            Down("e1", "KeyA"),
            Down("e2", "KeyA"),
            Up("e3", "KeyA"));

        var result = SequenceAnalyzer.AnalyzeSequence(write);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Code == ValidationCodes.RepeatedKeyDown && w.EventIndex == 1);
        Assert.DoesNotContain(result.Warnings, w => w.Code == ValidationCodes.UnclosedKeyDown);
        Assert.DoesNotContain(result.Warnings, w => w.Code == ValidationCodes.UnmatchedKeyUp);
    }

    [Fact]
    public void Pairing_interleaved_sequence_has_no_warnings()
    {
        var write = CreateWrite(
            Down("e1", "KeyA"),
            Down("e2", "KeyB"),
            Up("e3", "KeyA"),
            Up("e4", "KeyB"));

        var result = SequenceAnalyzer.AnalyzeSequence(write);

        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Fingerprint_ignores_eventId_name_and_description()
    {
        var a = CreateWrite(Down("id-a", "KeyC", 50));
        a.Name = "One";
        a.Description = "Desc A";
        a.BeforeDelayMs = 10;
        a.AfterDelayMs = 20;

        var b = CreateWrite(Down("id-b", "KeyC", 50));
        b.Name = "Two";
        b.Description = "Desc B";
        b.BeforeDelayMs = 10;
        b.AfterDelayMs = 20;

        var fa = SequenceAnalyzer.AnalyzeSequence(a).WarningFingerprint;
        var fb = SequenceAnalyzer.AnalyzeSequence(b).WarningFingerprint;
        Assert.Equal(fa, fb);
    }

    [Fact]
    public void Fingerprint_changes_when_delay_or_key_changes()
    {
        var baseWrite = CreateWrite(Down("e1", "KeyC", 50));
        var changedDelay = CreateWrite(Down("e1", "KeyC", 51));
        var changedKey = CreateWrite(Down("e1", "KeyD", 50));

        var f0 = SequenceAnalyzer.AnalyzeSequence(baseWrite).WarningFingerprint;
        Assert.NotEqual(f0, SequenceAnalyzer.AnalyzeSequence(changedDelay).WarningFingerprint);
        Assert.NotEqual(f0, SequenceAnalyzer.AnalyzeSequence(changedKey).WarningFingerprint);
    }

    [Fact]
    public void Ctrl_Alt_Delete_trajectory_is_hard_error()
    {
        var write = CreateWrite(
            Down("e1", "LeftControl"),
            Down("e2", "LeftAlt"),
            Down("e3", "Delete"));

        var result = SequenceAnalyzer.AnalyzeSequence(write);

        Assert.False(result.IsValid);
        Assert.Contains(result.HardErrors, e => e.Code == ValidationCodes.UnsupportedKeyCombination);
    }

    [Fact]
    public void Delete_then_modifiers_also_triggers_ctrl_alt_delete()
    {
        var write = CreateWrite(
            Down("e1", "Delete"),
            Down("e2", "RightControl"),
            Down("e3", "RightAlt"));

        var result = SequenceAnalyzer.AnalyzeSequence(write);

        Assert.Contains(result.HardErrors, e => e.Code == ValidationCodes.UnsupportedKeyCombination);
    }

    [Fact]
    public void Mixed_left_right_modifiers_with_delete_are_blocked()
    {
        var write = CreateWrite(
            Down("e1", "LeftControl"),
            Down("e2", "RightAlt"),
            Down("e3", "Delete"));

        var result = SequenceAnalyzer.AnalyzeSequence(write);

        Assert.Contains(result.HardErrors, e => e.Code == ValidationCodes.UnsupportedKeyCombination);
    }

    [Fact]
    public void Hard_errors_for_empty_unknown_duplicate_version_and_limits()
    {
        var empty = CreateWrite();
        empty.Sequence.Events.Clear();
        Assert.Contains(
            SequenceAnalyzer.AnalyzeSequence(empty).HardErrors,
            e => e.Code == ValidationCodes.EmptySequence && e.FieldPath == "sequence.events"
                && e.Message == "请至少添加一个按键事件");

        var unknownType = CreateWrite(new KeyboardEvent
        {
            EventId = "e1",
            Type = "keyHold",
            Key = "KeyA",
            DelayBeforeMs = 0,
        });
        Assert.Contains(
            SequenceAnalyzer.AnalyzeSequence(unknownType).HardErrors,
            e => e.Code == ValidationCodes.UnknownEventType);

        var unknownKey = CreateWrite(new KeyboardEvent
        {
            EventId = "e1",
            Type = KeySenderConst.EventTypeKeyDown,
            Key = "MouseLeft",
            DelayBeforeMs = 0,
        });
        Assert.Contains(
            SequenceAnalyzer.AnalyzeSequence(unknownKey).HardErrors,
            e => e.Code == ValidationCodes.UnsupportedKey);

        var duplicate = CreateWrite(Down("same", "KeyA"), Up("same", "KeyA"));
        Assert.Contains(
            SequenceAnalyzer.AnalyzeSequence(duplicate).HardErrors,
            e => e.Code == ValidationCodes.DuplicateEventId);

        var oldVersion = CreateWrite(Down("e1", "KeyA"));
        oldVersion.Sequence.Version = 1;
        Assert.Contains(
            SequenceAnalyzer.AnalyzeSequence(oldVersion).HardErrors,
            e => e.Code == ValidationCodes.UnsupportedSequenceVersion);

        var tooMany = CreateWrite();
        tooMany.Sequence.Events = Enumerable.Range(0, 10001)
            .Select(i => Down($"e{i}", "KeyA"))
            .ToList();
        Assert.Contains(
            SequenceAnalyzer.AnalyzeSequence(tooMany).HardErrors,
            e => e.Code == ValidationCodes.EventCountOutOfRange);
    }

    [Fact]
    public void Name_description_nul_and_illegal_delays_are_hard_errors()
    {
        var badName = CreateWrite(Down("e1", "KeyA"));
        badName.Name = " ";
        Assert.Contains(
            SequenceAnalyzer.AnalyzeSequence(badName).HardErrors,
            e => e.Code == ValidationCodes.InvalidName && e.FieldPath == "name" && e.Message == "名称不能为空");

        var nulName = CreateWrite(Down("e1", "KeyA"));
        nulName.Name = "a\0b";
        Assert.Contains(
            SequenceAnalyzer.AnalyzeSequence(nulName).HardErrors,
            e => e.Code == ValidationCodes.InvalidName && e.FieldPath == "name" && e.Message == "名称含有无效字符");

        var longDesc = CreateWrite(Down("e1", "KeyA"));
        longDesc.Description = new string('x', 501);
        Assert.Contains(
            SequenceAnalyzer.AnalyzeSequence(longDesc).HardErrors,
            e => e.Code == ValidationCodes.InvalidDescription);

        var negativeDelay = CreateWrite(Down("e1", "KeyA", -1));
        negativeDelay.BeforeDelayMs = -1;
        negativeDelay.AfterDelayMs = -1;
        Assert.Equal(
            [
                ("beforeDelayMs", "节点前等待必须在 0–2147483647 毫秒之间"),
                ("afterDelayMs", "节点后等待必须在 0–2147483647 毫秒之间"),
                ("sequence.events[0].delayBeforeMs", "第 1 条事件的等待必须在 0–2147483647 毫秒之间"),
            ],
            SequenceAnalyzer.AnalyzeSequence(negativeDelay).HardErrors
                .Where(e => e.Code == ValidationCodes.InvalidDelay)
                .Select(e => (e.FieldPath, e.Message)));
    }

    [Fact]
    public void Paired_ctrl_c_has_no_warnings()
    {
        var write = CreateWrite(
            Down("e1", "LeftControl", 0),
            Down("e2", "KeyC", 50),
            Up("e3", "KeyC", 30),
            Up("e4", "LeftControl", 20));

        var result = SequenceAnalyzer.AnalyzeSequence(write);

        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Pairing_keeps_modifier_sides_and_numpad_enter_separate()
    {
        var write = CreateWrite(
            Down("e1", "LeftControl"),
            Down("e2", "NumpadEnter"),
            Up("e3", "RightControl"),
            Up("e4", "Enter"));

        var result = SequenceAnalyzer.AnalyzeSequence(write);

        Assert.True(result.IsValid);
        Assert.Equal(
            [
                (ValidationCodes.UnmatchedKeyUp, "RightControl", (int?)2, "sequence.events[2]"),
                (ValidationCodes.UnmatchedKeyUp, "Enter", (int?)3, "sequence.events[3]"),
                (ValidationCodes.UnclosedKeyDown, "LeftControl", (int?)0, "sequence.events[0]"),
                (ValidationCodes.UnclosedKeyDown, "NumpadEnter", (int?)1, "sequence.events[1]"),
            ],
            result.Warnings.Select(w => (w.Code, w.Key, w.EventIndex, w.FieldPath)));
    }

    [Fact]
    public void Maximum_length_and_large_cumulative_delays_are_valid()
    {
        var write = CreateWrite();
        write.Name = "  " + string.Concat(Enumerable.Repeat("😀", 50)) + "  ";
        write.Description = new string('x', 500);
        write.BeforeDelayMs = int.MaxValue;
        write.AfterDelayMs = int.MaxValue;
        write.Sequence.Events = Enumerable.Range(0, 10000)
            .Select(i => i % 2 == 0
                ? Down($"e{i}", "KeyA", int.MaxValue)
                : Up($"e{i}", "KeyA", int.MaxValue))
            .ToList();

        var result = SequenceAnalyzer.AnalyzeSequence(write);

        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
        write.Name += "😀";
        Assert.Contains(SequenceAnalyzer.AnalyzeSequence(write).HardErrors, issue =>
            issue.Code == ValidationCodes.InvalidName && issue.FieldPath == "name"
            && issue.Message == "名称不能超过 100 个字符");
    }

    [Fact]
    public void Releasing_the_first_modifier_avoids_a_simultaneous_ctrl_alt_delete()
    {
        var result = SequenceAnalyzer.AnalyzeSequence(CreateWrite(
            Down("e1", "LeftControl"),
            Up("e2", "LeftControl"),
            Down("e3", "RightAlt"),
            Down("e4", "Delete"),
            Up("e5", "Delete"),
            Up("e6", "RightAlt")));

        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
    }

    private static NodeWrite CreateWrite(params KeyboardEvent[] events)
        => new()
        {
            Name = "Node",
            Description = "",
            BeforeDelayMs = 0,
            AfterDelayMs = 0,
            Sequence = new KeyboardSequence
            {
                Version = 2,
                Events = events.ToList(),
            },
        };

    private static KeyboardEvent Down(string id, string key, int delay = 0)
        => new()
        {
            EventId = id,
            Type = KeySenderConst.EventTypeKeyDown,
            Key = key,
            DelayBeforeMs = delay,
        };

    private static KeyboardEvent Up(string id, string key, int delay = 0)
        => new()
        {
            EventId = id,
            Type = KeySenderConst.EventTypeKeyUp,
            Key = key,
            DelayBeforeMs = delay,
        };
}
