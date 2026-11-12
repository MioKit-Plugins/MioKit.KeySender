using KeySender.BuiltIn;
using KeySender.Models;
using KeySender.Validation;

namespace KeySender.Services;

/// <summary>
/// Pure helpers for structural validation, built-in guards, and copy naming.
/// </summary>
public static class ConfigHelpers
{
    /// <summary>
    /// Returns null when save may proceed; otherwise a failure result
    /// (<see cref="ValidationCodes.ValidationFailed"/>).
    /// </summary>
    public static ConfigurationResult? EvaluateSaveGate(
        NodeWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var analysis = SequenceAnalyzer.AnalyzeSequence(write);
        if (!analysis.IsValid)
        {
            return ConfigurationResult.Fail(
                ValidationCodes.ValidationFailed,
                "Hard validation failed.",
                analysis);
        }

        return null;
    }

    public static ConfigurationResult? RejectIfBuiltIn(string? nodeId)
    {
        if (!BuiltInKeyCatalog.IsBuiltIn(nodeId))
        {
            return null;
        }

        return ConfigurationResult.Fail(
            ValidationCodes.BuiltinReadOnly,
            "Built-in nodes are read-only and cannot be updated or deleted.",
            nodeId: BuiltInKeyCatalog.CanonicalId(nodeId!));
    }

    public static ConfigurationResult? RejectIfAnyBuiltIn(IEnumerable<string> nodeIds)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);

        foreach (var id in nodeIds)
        {
            var reject = RejectIfBuiltIn(id);
            if (reject is not null)
            {
                return reject;
            }
        }

        return null;
    }

    /// <summary>
    /// Appends “ 副本” and truncates to <see cref="KeySenderConst.NameMaxLength"/> UTF-16 units.
    /// </summary>
    public static string BuildCopyName(string? sourceName)
    {
        var baseName = (sourceName ?? string.Empty).Trim();
        if (baseName.Length == 0)
        {
            baseName = "未命名";
        }

        var suffix = KeySenderConst.CopyNameSuffix;
        var maxBase = KeySenderConst.NameMaxLength - suffix.Length;
        if (maxBase < 1)
        {
            return suffix.Length <= KeySenderConst.NameMaxLength
                ? suffix
                : suffix[..KeySenderConst.NameMaxLength];
        }

        if (baseName.Length > maxBase)
        {
            baseName = baseName[..maxBase];
        }

        return baseName + suffix;
    }

    public static NodeDetail ToDetail(
        string nodeId, NodeWrite write, bool isBuiltIn, CategorySnapshot? category = null)
        => new()
        {
            NodeId = nodeId,
            Name = write.Name,
            Description = write.Description,
            CategoryId = category?.Id,
            CategoryName = category?.Name,
            BeforeDelayMs = write.BeforeDelayMs,
            AfterDelayMs = write.AfterDelayMs,
            Sequence = write.Sequence,
            IsBuiltIn = isBuiltIn,
            IsReadOnly = isBuiltIn,
            CanEdit = !isBuiltIn,
            CanDelete = !isBuiltIn,
        };
}
