using KeySender.Models;

namespace KeySender.Services;

/// <summary>
/// Test seam that returns a frozen <see cref="NodeWrite"/> for a node id
/// without requiring a live MioObject / full ConfigurationService.
/// </summary>
public interface IRunConfigurationSource
{
    Task<RunConfiguration?> GetAsync(string nodeId, CancellationToken cancellationToken = default);
}

public sealed class RunConfiguration
{
    public RunConfiguration(string nodeId, NodeWrite write)
    {
        NodeId = nodeId;
        Write = write ?? throw new ArgumentNullException(nameof(write));
    }

    public string NodeId { get; }

    public NodeWrite Write { get; }
}

/// <summary>Production adapter over <see cref="ConfigurationService.GetDetailAsync"/>.</summary>
public sealed class ConfigurationRunSource : IRunConfigurationSource
{
    private readonly ConfigurationService _configuration;

    public ConfigurationRunSource(ConfigurationService configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public async Task<RunConfiguration?> GetAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        var result = await _configuration.GetDetailAsync(nodeId, cancellationToken).ConfigureAwait(false);
        if (!result.Ok || result.Detail is null)
        {
            return null;
        }

        return new RunConfiguration(result.Detail.NodeId, result.Detail.ToWrite());
    }
}
