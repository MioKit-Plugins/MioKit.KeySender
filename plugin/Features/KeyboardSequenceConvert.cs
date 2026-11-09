using System.Text.Json;
using System.Text.Json.Serialization;
using KeySender.Models;

namespace KeySender;

/// <summary>
/// Versioned JSON converter for <see cref="KeyboardSequence"/> stored as LongText.
/// </summary>
public sealed class KeyboardSequenceConvert : IMioPropertyConvert
{
    public static KeyboardSequenceConvert Instance { get; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public object? ConvertToStoreValue(object? value, MioStoreType storeType)
    {
        if (value is null)
        {
            return null;
        }

        if (value is not KeyboardSequence sequence)
        {
            throw new InvalidOperationException(
                $"Expected {nameof(KeyboardSequence)}, got {value.GetType().FullName}.");
        }

        return JsonSerializer.Serialize(sequence, Options);
    }

    public object? ConvertToMemoryValue(object? value, MioStoreType storeType)
    {
        if (value is null)
        {
            return null;
        }

        if (value is KeyboardSequence existing)
        {
            return existing;
        }

        var json = value as string
                   ?? value.ToString()
                   ?? throw new InvalidOperationException("Sequence store value is empty.");

        var sequence = JsonSerializer.Deserialize<KeyboardSequence>(json, Options)
                       ?? throw new InvalidOperationException("Sequence JSON deserialized to null.");

        if (sequence.Version != KeySenderConst.SequenceVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported sequence version {sequence.Version}; expected {KeySenderConst.SequenceVersion}.");
        }

        sequence.Events ??= [];
        return sequence;
    }
}
