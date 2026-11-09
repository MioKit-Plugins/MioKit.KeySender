using KeySender.Models;
using KeySender.Services;
using KeySender.Validation;

namespace MioKit.KeySender.Tests.Fakes;

public sealed class FakeConfigurationService : IConfigurationService
{
    public List<NodeWrite> Created { get; } = [];

    public List<string?> AcknowledgedFingerprints { get; } = [];

    public Func<NodeWrite, string?, ConfigurationResult>? CreateHandler { get; set; }

    public Dictionary<string, NodeDetail> Details { get; } = new(StringComparer.Ordinal);

    public Task<ConfigurationResult> CreateAsync(
        NodeWrite write,
        string? acknowledgedWarningFingerprint,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AcknowledgedFingerprints.Add(acknowledgedWarningFingerprint);

        if (CreateHandler is not null)
        {
            return Task.FromResult(CreateHandler(write, acknowledgedWarningFingerprint));
        }

        var blocked = ConfigHelpers.EvaluateSaveGate(write, acknowledgedWarningFingerprint);
        if (blocked is not null)
        {
            return Task.FromResult(blocked);
        }

        var id = Guid.NewGuid().ToString("D").ToUpperInvariant();
        Created.Add(write);
        var detail = ConfigHelpers.ToDetail(id, write, isBuiltIn: false);
        Details[id] = detail;
        return Task.FromResult(ConfigurationResult.Success(id, detail));
    }

    public Task<ConfigurationResult> GetDetailAsync(
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Details.TryGetValue(nodeId, out var detail))
        {
            return Task.FromResult(ConfigurationResult.Success(nodeId, detail));
        }

        return Task.FromResult(ConfigurationResult.Fail(ValidationCodes.NodeNotFound, nodeId: nodeId));
    }
}
