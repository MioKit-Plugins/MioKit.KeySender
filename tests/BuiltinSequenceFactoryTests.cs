using KeySender;
using KeySender.BuiltIn;
using KeySender.Validation;
using Xunit;

namespace MioKit.KeySender.Tests;

public sealed class BuiltinSequenceFactoryTests
{
    [Theory]
    [InlineData(KeySenderConst.BuiltInCopyNodeId, "KeyC")]
    [InlineData(KeySenderConst.BuiltInCutNodeId, "KeyX")]
    [InlineData(KeySenderConst.BuiltInPasteNodeId, "KeyV")]
    public void BuiltIn_factory_produces_valid_paired_sequences(string nodeId, string primaryKey)
    {
        var write1 = BuiltinSequenceFactory.CreateWrite(nodeId);
        var write2 = BuiltinSequenceFactory.CreateWrite(nodeId);

        Assert.Equal(0, write1.BeforeDelayMs);
        Assert.Equal(0, write1.AfterDelayMs);
        Assert.Equal(KeySenderConst.SequenceVersion, write1.Sequence.Version);
        Assert.Equal(4, write1.Sequence.Events.Count);
        Assert.Equal("LeftControl", write1.Sequence.Events[0].Key);
        Assert.Equal(KeySenderConst.EventTypeKeyDown, write1.Sequence.Events[0].Type);
        Assert.Equal(primaryKey, write1.Sequence.Events[1].Key);
        Assert.Equal(primaryKey, write1.Sequence.Events[2].Key);
        Assert.Equal(KeySenderConst.EventTypeKeyUp, write1.Sequence.Events[2].Type);
        Assert.Equal("LeftControl", write1.Sequence.Events[3].Key);
        Assert.All(write1.Sequence.Events, e => Assert.Equal(0, e.DelayBeforeMs));

        // Fresh eventIds on each read
        Assert.NotEqual(
            write1.Sequence.Events.Select(e => e.EventId).ToArray(),
            write2.Sequence.Events.Select(e => e.EventId).ToArray());

        var analysis = SequenceAnalyzer.AnalyzeSequence(write1);
        Assert.True(analysis.IsValid);
        Assert.Empty(analysis.Warnings);
    }
}
