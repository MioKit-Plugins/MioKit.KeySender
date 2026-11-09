using KeySender.Validation;

namespace KeySender.Models;

/// <summary>
/// Backend result for ConfigurationService operations (UiService maps to bridge ApiResult later).
/// </summary>
public sealed class ConfigurationResult
{
    public bool Ok { get; init; }

    public string? Code { get; init; }

    public string? Message { get; init; }

    public string? NodeId { get; init; }

    public NodeDetail? Detail { get; init; }

    public SequenceAnalysisResult? Analysis { get; init; }

    public bool Persisted { get; init; }

    public IReadOnlyList<string>? CreatedIds { get; init; }

    public IReadOnlyList<string>? DeletedIds { get; init; }

    public IReadOnlyList<string>? RemainingIds { get; init; }

    public static ConfigurationResult Success(string? nodeId = null, NodeDetail? detail = null) => new()
    {
        Ok = true,
        NodeId = nodeId,
        Detail = detail,
    };

    public static ConfigurationResult Fail(
        string code,
        string? message = null,
        SequenceAnalysisResult? analysis = null,
        string? nodeId = null) => new()
    {
        Ok = false,
        Code = code,
        Message = message,
        Analysis = analysis,
        NodeId = nodeId,
    };
}
