namespace KeySender.BuiltIn;

/// <summary>
/// Lightweight built-in node recipe (identity + display + primary key). No event list.
/// </summary>
public sealed class BuiltInKeyRecipe
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    /// <summary>Primary key token, e.g. <c>KeyC</c>.</summary>
    public required string PrimaryKey { get; init; }
}
