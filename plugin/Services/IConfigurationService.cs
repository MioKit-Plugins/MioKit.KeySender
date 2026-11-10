using KeySender.Models;

namespace KeySender.Services;

/// <summary>
/// Narrow configuration surface used by TransferService import commit / export load.
/// </summary>
public interface IConfigurationService
{
    Task<ConfigurationResult> CreateAsync(
        NodeWrite write,
        CancellationToken cancellationToken = default);

    Task<ConfigurationResult> GetDetailAsync(
        string nodeId,
        CancellationToken cancellationToken = default);
}
