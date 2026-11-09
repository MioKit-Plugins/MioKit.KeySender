using KeySender.Models;
using KeySender.Services;

namespace MioKit.KeySender.Tests.Fakes;

public sealed class DictionaryRunConfigurationSource : IRunConfigurationSource
{
    public Dictionary<string, NodeWrite> Nodes { get; } = new(StringComparer.Ordinal);

    public Task<RunConfiguration?> GetAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Nodes.TryGetValue(nodeId, out var write))
        {
            return Task.FromResult<RunConfiguration?>(new RunConfiguration(nodeId, write));
        }

        return Task.FromResult<RunConfiguration?>(null);
    }
}
