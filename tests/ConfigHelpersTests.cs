using KeySender;
using KeySender.Models;
using KeySender.Services;
using KeySender.Validation;
using Xunit;

namespace MioKit.KeySender.Tests;

public sealed class ConfigHelpersTests
{
    [Fact]
    public void Fingerprint_confirmation_required_then_accepts_matching_fingerprint()
    {
        var write = new NodeWrite
        {
            Name = "hold",
            Description = "",
            BeforeDelayMs = 0,
            AfterDelayMs = 0,
            Sequence = new KeyboardSequence
            {
                Version = KeySenderConst.SequenceVersion,
                Events =
                [
                    new KeyboardEvent
                    {
                        EventId = "e1",
                        Type = KeySenderConst.EventTypeKeyDown,
                        Key = "LeftControl",
                        DelayBeforeMs = 0,
                    },
                ],
            },
        };

        var without = ConfigHelpers.EvaluateSaveGate(write, acknowledgedWarningFingerprint: null);
        Assert.NotNull(without);
        Assert.Equal(ValidationCodes.SequenceConfirmationRequired, without!.Code);
        Assert.NotNull(without.Analysis);
        Assert.True(without.Analysis!.HasWarnings);

        var fingerprint = without.Analysis.WarningFingerprint;
        var withMismatch = ConfigHelpers.EvaluateSaveGate(write, "deadbeef");
        Assert.NotNull(withMismatch);
        Assert.Equal(ValidationCodes.SequenceConfirmationRequired, withMismatch!.Code);

        var withMatch = ConfigHelpers.EvaluateSaveGate(write, fingerprint);
        Assert.Null(withMatch);
    }

    [Fact]
    public void Copy_naming_truncates_to_100()
    {
        var longName = new string('A', 100);
        var copy = ConfigHelpers.BuildCopyName(longName);
        Assert.Equal(100, copy.Length);
        Assert.EndsWith(KeySenderConst.CopyNameSuffix, copy);

        var shortCopy = ConfigHelpers.BuildCopyName("复制");
        Assert.Equal("复制 副本", shortCopy);
    }

    [Fact]
    public void RejectIfBuiltIn_returns_builtin_read_only()
    {
        var result = ConfigHelpers.RejectIfBuiltIn(KeySenderConst.BuiltInPasteNodeId.ToLowerInvariant());
        Assert.NotNull(result);
        Assert.Equal(ValidationCodes.BuiltinReadOnly, result!.Code);
        Assert.Equal(KeySenderConst.BuiltInPasteNodeId, result.NodeId);
    }

    [Fact]
    public void RejectIfAnyBuiltIn_rejects_entire_batch()
    {
        var result = ConfigHelpers.RejectIfAnyBuiltIn(
        [
            Guid.NewGuid().ToString("D"),
            KeySenderConst.BuiltInCutNodeId.ToLowerInvariant(),
        ]);
        Assert.NotNull(result);
        Assert.Equal(ValidationCodes.BuiltinReadOnly, result!.Code);
    }
}
