namespace KeySender.BuiltIn;

/// <summary>
/// Fixed built-in Copy/Cut/Paste identities. IsBuiltIn is derived from this catalog only.
/// </summary>
public static class BuiltInKeyCatalog
{
    private static readonly Dictionary<Guid, BuiltInKeyRecipe> ByGuid;
    private static readonly Dictionary<string, BuiltInKeyRecipe> ByExactId;

    public static IReadOnlyList<BuiltInKeyRecipe> All { get; }

    static BuiltInKeyCatalog()
    {
        All =
        [
            new BuiltInKeyRecipe
            {
                Id = KeySenderConst.BuiltInCopyNodeId,
                Name = "复制",
                Description = "向目标窗口发送 Ctrl+C",
                PrimaryKey = "KeyC",
            },
            new BuiltInKeyRecipe
            {
                Id = KeySenderConst.BuiltInCutNodeId,
                Name = "剪切",
                Description = "向目标窗口发送 Ctrl+X",
                PrimaryKey = "KeyX",
            },
            new BuiltInKeyRecipe
            {
                Id = KeySenderConst.BuiltInPasteNodeId,
                Name = "粘贴",
                Description = "向目标窗口发送 Ctrl+V",
                PrimaryKey = "KeyV",
            },
        ];

        ByGuid = new Dictionary<Guid, BuiltInKeyRecipe>(All.Count);
        ByExactId = new Dictionary<string, BuiltInKeyRecipe>(All.Count, StringComparer.Ordinal);
        foreach (var recipe in All)
        {
            ByExactId[recipe.Id] = recipe;
            ByGuid[Guid.Parse(recipe.Id)] = recipe;
        }
    }

    /// <summary>
    /// True when <paramref name="id"/> matches a catalog entry after Guid normalization
    /// (case-insensitive). Invalid Guid strings are never built-in.
    /// </summary>
    public static bool IsBuiltIn(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        if (ByExactId.ContainsKey(id))
        {
            return true;
        }

        return Guid.TryParse(id, out var guid) && ByGuid.ContainsKey(guid);
    }

    public static bool TryGet(string? id, out BuiltInKeyRecipe recipe)
    {
        recipe = null!;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        if (ByExactId.TryGetValue(id, out recipe!))
        {
            return true;
        }

        return Guid.TryParse(id, out var guid) && ByGuid.TryGetValue(guid, out recipe!);
    }

    /// <summary>
    /// Canonical uppercase D-format id used for persisted built-in instances.
    /// </summary>
    public static string CanonicalId(string id)
    {
        if (!TryGet(id, out var recipe))
        {
            throw new ArgumentException($"Not a built-in id: {id}", nameof(id));
        }

        return recipe.Id;
    }
}
