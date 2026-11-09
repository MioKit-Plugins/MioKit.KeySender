using KeySender.Models;

namespace KeySender.BuiltIn;

/// <summary>
/// Generates fresh v2 Ctrl+primary sequences for built-in recipes on each read.
/// </summary>
public static class BuiltinSequenceFactory
{
    public static NodeWrite CreateWrite(BuiltInKeyRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        return new NodeWrite
        {
            Name = recipe.Name,
            Description = recipe.Description,
            BeforeDelayMs = 0,
            AfterDelayMs = 0,
            Sequence = CreateSequence(recipe.PrimaryKey),
        };
    }

    public static NodeWrite CreateWrite(string builtInId)
    {
        if (!BuiltInKeyCatalog.TryGet(builtInId, out var recipe))
        {
            throw new ArgumentException($"Unknown built-in id: {builtInId}", nameof(builtInId));
        }

        return CreateWrite(recipe);
    }

    public static KeyboardSequence CreateSequence(string primaryKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryKey);

        return new KeyboardSequence
        {
            Version = KeySenderConst.SequenceVersion,
            Events =
            [
                Event(KeySenderConst.EventTypeKeyDown, "LeftControl"),
                Event(KeySenderConst.EventTypeKeyDown, primaryKey),
                Event(KeySenderConst.EventTypeKeyUp, primaryKey),
                Event(KeySenderConst.EventTypeKeyUp, "LeftControl"),
            ],
        };
    }

    public static KeyboardSequence CloneWithFreshEventIds(KeyboardSequence source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new KeyboardSequence
        {
            Version = source.Version == 0 ? KeySenderConst.SequenceVersion : source.Version,
            Events = (source.Events ?? []).Select(ev => new KeyboardEvent
            {
                EventId = Guid.NewGuid().ToString("D").ToUpperInvariant(),
                Type = ev.Type,
                Key = ev.Key,
                DelayBeforeMs = ev.DelayBeforeMs,
            }).ToList(),
        };
    }

    private static KeyboardEvent Event(string type, string key) => new()
    {
        EventId = Guid.NewGuid().ToString("D").ToUpperInvariant(),
        Type = type,
        Key = key,
        DelayBeforeMs = 0,
    };
}
