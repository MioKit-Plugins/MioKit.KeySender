namespace KeySender.Models;

public sealed class RecordingOperationResult
{
    public bool Ok { get; init; }

    public string? Code { get; init; }

    public string? Message { get; init; }

    public RecordingSnapshot? Snapshot { get; init; }

    public RecordingFragment? Fragment { get; init; }

    public static RecordingOperationResult Success(
        RecordingSnapshot snapshot,
        RecordingFragment? fragment = null) => new()
    {
        Ok = true,
        Snapshot = snapshot,
        Fragment = fragment,
    };

    public static RecordingOperationResult Fail(
        string code,
        string? message = null,
        RecordingSnapshot? snapshot = null) => new()
    {
        Ok = false,
        Code = code,
        Message = message,
        Snapshot = snapshot,
    };
}
