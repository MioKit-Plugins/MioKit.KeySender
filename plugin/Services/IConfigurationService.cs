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

    Task<ConfigurationResult> CreateInCategoryAsync(
        NodeWrite write,
        string? categoryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reuse a unique matching category when allowed, or create one. Reports a newly saved
    /// category's ID and persistence status even if it could not be attached to the tree.
    /// </summary>
    Task<ConfigurationResult> ResolveImportCategoryAsync(
        string name,
        string description,
        bool allowReuse,
        CancellationToken cancellationToken = default);

    Task<ConfigurationResult> GetDetailAsync(
        string nodeId,
        CancellationToken cancellationToken = default);
}
