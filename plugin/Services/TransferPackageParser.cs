using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeySender.Models;
using KeySender.Validation;

namespace KeySender.Services;

/// <summary>
/// Pure v2 .miokeys parse/validate (design 05). No I/O, no DI.
/// </summary>
public static class TransferPackageParser
{
    private static readonly HashSet<string> RootFields = new(StringComparer.Ordinal)
    {
        "format", "formatVersion", "nodes",
    };

    private static readonly HashSet<string> NodeFields = new(StringComparer.Ordinal)
    {
        "name", "description", "beforeDelayMs", "afterDelayMs", "sequence",
    };

    private static readonly HashSet<string> SequenceFields = new(StringComparer.Ordinal)
    {
        "version", "events",
    };

    private static readonly HashSet<string> EventFields = new(StringComparer.Ordinal)
    {
        "type", "key", "delayBeforeMs",
    };

    public static TransferParseResult Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length > KeySenderConst.TransferMaxBytes)
        {
            return TransferParseResult.Fail(
                ValidationCodes.FileTooLarge,
                $"Package exceeds {KeySenderConst.TransferMaxBytes} bytes.");
        }

        // Hash the original file bytes, just as the commit-time file check does.
        var sha = SHA256.HashData(utf8Json);

        // Allow UTF-8 BOM.
        if (utf8Json.Length >= 3
            && utf8Json[0] == 0xEF
            && utf8Json[1] == 0xBB
            && utf8Json[2] == 0xBF)
        {
            utf8Json = utf8Json[3..];
        }

        if (utf8Json.IsEmpty)
        {
            return TransferParseResult.Fail(ValidationCodes.InvalidFormat, "Empty package.");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(
                utf8Json.ToArray(),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = KeySenderConst.TransferMaxJsonDepth,
                });
        }
        catch (JsonException ex)
        {
            var code = ex.Message.Contains("depth", StringComparison.OrdinalIgnoreCase)
                ? ValidationCodes.ImportLimitExceeded
                : ValidationCodes.InvalidFormat;
            return TransferParseResult.Fail(code, ex.Message);
        }

        using (doc)
        {
            return ParseDocument(doc.RootElement, sha);
        }
    }

    public static TransferParseResult Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Parse(Encoding.UTF8.GetBytes(json));
    }

    public static void WritePackage(Stream output, IReadOnlyList<NodeWrite> nodes)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(nodes);

        using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true, SkipValidation = false });
        writer.WriteStartObject();
        writer.WriteString("format", KeySenderConst.TransferFormat);
        writer.WriteNumber("formatVersion", KeySenderConst.TransferFormatVersion);
        writer.WritePropertyName("nodes");
        writer.WriteStartArray();

        foreach (var node in nodes)
        {
            WriteNode(writer, node);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
    }

    public static string WritePackageString(IReadOnlyList<NodeWrite> nodes)
    {
        using var ms = new MemoryStream();
        WritePackage(ms, nodes);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static TransferParseResult ParseDocument(JsonElement root, byte[] sha)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return TransferParseResult.Fail(ValidationCodes.InvalidFormat, "Root must be a JSON object.");
        }

        if (!ValidateObjectFields(root, RootFields, out var fieldError))
        {
            return TransferParseResult.Fail(ValidationCodes.InvalidFormat, fieldError);
        }

        if (!TryGetString(root, "format", required: true, out var format, out var formatErr)
            || !string.Equals(format, KeySenderConst.TransferFormat, StringComparison.Ordinal))
        {
            return TransferParseResult.Fail(
                ValidationCodes.InvalidFormat,
                formatErr ?? $"format must be '{KeySenderConst.TransferFormat}'.");
        }

        if (!TryGetInt32Strict(root, "formatVersion", required: true, out var formatVersion, out var fvErr))
        {
            return TransferParseResult.Fail(ValidationCodes.InvalidFormat, fvErr);
        }

        if (formatVersion != KeySenderConst.TransferFormatVersion)
        {
            return TransferParseResult.Fail(
                ValidationCodes.UnsupportedFormatVersion,
                $"Unsupported formatVersion {formatVersion}; expected {KeySenderConst.TransferFormatVersion}. v1 is not migrated.");
        }

        if (!root.TryGetProperty("nodes", out var nodesEl) || nodesEl.ValueKind != JsonValueKind.Array)
        {
            return TransferParseResult.Fail(ValidationCodes.InvalidFormat, "nodes must be an array.");
        }

        if (nodesEl.GetArrayLength() < 1 || nodesEl.GetArrayLength() > KeySenderConst.TransferMaxNodes)
        {
            return TransferParseResult.Fail(
                ValidationCodes.ImportLimitExceeded,
                $"nodes length must be 1–{KeySenderConst.TransferMaxNodes}.");
        }

        var writes = new List<NodeWrite>(nodesEl.GetArrayLength());
        var analyses = new List<SequenceAnalysisResult>(nodesEl.GetArrayLength());
        var anyHardError = false;

        var index = 0;
        foreach (var nodeEl in nodesEl.EnumerateArray())
        {
            if (!TryParseNode(nodeEl, index, out var write, out var parseError))
            {
                return TransferParseResult.Fail(ValidationCodes.InvalidFormat, parseError);
            }

            // Import events get fresh ids (design 05 §2).
            foreach (var ev in write.Sequence.Events)
            {
                ev.EventId = Guid.NewGuid().ToString("D");
            }

            var analysis = SequenceAnalyzer.AnalyzeSequence(write);
            writes.Add(write);
            analyses.Add(analysis);
            if (!analysis.IsValid)
            {
                anyHardError = true;
            }

            index++;
        }

        var fingerprint = SequenceAnalyzer.ComputePackageWarningFingerprint(writes);
        var canImport = !anyHardError;

        return new TransferParseResult
        {
            Ok = true,
            Nodes = writes,
            Analyses = analyses,
            CanImport = canImport,
            WarningFingerprint = fingerprint,
            ContentSha256 = sha,
            Code = anyHardError ? ValidationCodes.ValidationFailed : null,
            Message = anyHardError ? "One or more nodes failed hard validation." : null,
        };
    }

    private static bool TryParseNode(JsonElement nodeEl, int index, out NodeWrite write, out string? error)
    {
        write = new NodeWrite();
        error = null;

        if (nodeEl.ValueKind != JsonValueKind.Object)
        {
            error = $"nodes[{index}] must be an object.";
            return false;
        }

        if (!ValidateObjectFields(nodeEl, NodeFields, out error))
        {
            return false;
        }

        // Reject identity / host fields if present (already covered by whitelist), and NUL.
        if (!TryGetString(nodeEl, "name", required: true, out var name, out error))
        {
            return false;
        }

        name = name.Trim();
        // Length / NUL hard errors surface via SequenceAnalyzer in preview.

        string description = "";
        if (nodeEl.TryGetProperty("description", out _))
        {
            if (!TryGetString(nodeEl, "description", required: true, out description!, out error))
            {
                return false;
            }
        }

        if (!TryGetInt32Strict(nodeEl, "beforeDelayMs", required: false, out var before, out error))
        {
            return false;
        }

        if (!TryGetInt32Strict(nodeEl, "afterDelayMs", required: false, out var after, out error))
        {
            return false;
        }

        if (!nodeEl.TryGetProperty("sequence", out var seqEl) || seqEl.ValueKind != JsonValueKind.Object)
        {
            error = $"nodes[{index}].sequence is required.";
            return false;
        }

        if (!ValidateObjectFields(seqEl, SequenceFields, out error))
        {
            return false;
        }

        if (!TryGetInt32Strict(seqEl, "version", required: true, out var version, out error))
        {
            return false;
        }

        if (!seqEl.TryGetProperty("events", out var eventsEl) || eventsEl.ValueKind != JsonValueKind.Array)
        {
            error = $"nodes[{index}].sequence.events must be an array.";
            return false;
        }

        var count = eventsEl.GetArrayLength();
        // Length / version hard errors are reported via SequenceAnalyzer in preview.
        var events = new List<KeyboardEvent>(Math.Max(0, count));
        var ei = 0;
        foreach (var evEl in eventsEl.EnumerateArray())
        {
            if (!TryParseEvent(evEl, index, ei, out var ev, out error))
            {
                return false;
            }

            events.Add(ev);
            ei++;
        }

        write = new NodeWrite
        {
            Name = name,
            Description = description,
            BeforeDelayMs = before,
            AfterDelayMs = after,
            Sequence = new KeyboardSequence
            {
                Version = version,
                Events = events,
            },
        };
        return true;
    }

    private static bool TryParseEvent(
        JsonElement evEl,
        int nodeIndex,
        int eventIndex,
        out KeyboardEvent ev,
        out string? error)
    {
        ev = new KeyboardEvent();
        error = null;

        if (evEl.ValueKind != JsonValueKind.Object)
        {
            error = $"nodes[{nodeIndex}].sequence.events[{eventIndex}] must be an object.";
            return false;
        }

        if (!ValidateObjectFields(evEl, EventFields, out error))
        {
            return false;
        }

        // Unknown type/key become per-node hard errors in SequenceAnalyzer (preview canImport=false).
        if (!TryGetString(evEl, "type", required: true, out var type, out error) || type.Contains('\0'))
        {
            error = error ?? $"nodes[{nodeIndex}].sequence.events[{eventIndex}].type is invalid.";
            return false;
        }

        if (!TryGetString(evEl, "key", required: true, out var key, out error) || key.Contains('\0'))
        {
            error = error ?? $"nodes[{nodeIndex}].sequence.events[{eventIndex}].key is invalid.";
            return false;
        }

        if (!TryGetInt32Strict(evEl, "delayBeforeMs", required: false, out var delay, out error))
        {
            return false;
        }

        ev = new KeyboardEvent
        {
            Type = type,
            Key = key,
            DelayBeforeMs = delay,
            EventId = "", // filled by caller
        };
        return true;
    }

    private static void WriteNode(Utf8JsonWriter writer, NodeWrite node)
    {
        writer.WriteStartObject();
        writer.WriteString("name", node.Name ?? "");
        writer.WriteString("description", node.Description ?? "");
        writer.WriteNumber("beforeDelayMs", node.BeforeDelayMs);
        writer.WriteNumber("afterDelayMs", node.AfterDelayMs);
        writer.WritePropertyName("sequence");
        writer.WriteStartObject();
        writer.WriteNumber("version", KeySenderConst.SequenceVersion);
        writer.WritePropertyName("events");
        writer.WriteStartArray();
        foreach (var ev in node.Sequence?.Events ?? [])
        {
            writer.WriteStartObject();
            writer.WriteString("type", ev.Type);
            writer.WriteString("key", ev.Key);
            writer.WriteNumber("delayBeforeMs", ev.DelayBeforeMs);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static bool ValidateObjectFields(JsonElement obj, HashSet<string> allowed, out string? error)
    {
        error = null;
        foreach (var prop in obj.EnumerateObject())
        {
            if (!allowed.Contains(prop.Name))
            {
                error = $"Unknown field '{prop.Name}'.";
                return false;
            }
        }

        return true;
    }

    private static bool TryGetString(
        JsonElement obj,
        string name,
        bool required,
        out string value,
        out string? error)
    {
        value = "";
        error = null;
        if (!obj.TryGetProperty(name, out var el))
        {
            if (required)
            {
                error = $"Missing field '{name}'.";
                return false;
            }

            return true;
        }

        if (el.ValueKind != JsonValueKind.String)
        {
            error = $"Field '{name}' must be a string.";
            return false;
        }

        value = el.GetString() ?? "";
        if (value.Contains('\0'))
        {
            error = $"Field '{name}' contains NUL.";
            return false;
        }

        return true;
    }

    private static bool TryGetInt32Strict(
        JsonElement obj,
        string name,
        bool required,
        out int value,
        out string? error)
    {
        value = 0;
        error = null;
        if (!obj.TryGetProperty(name, out var el))
        {
            if (required)
            {
                error = $"Missing field '{name}'.";
                return false;
            }

            return true;
        }

        if (el.ValueKind == JsonValueKind.Null)
        {
            error = $"Field '{name}' must not be null.";
            return false;
        }

        if (el.ValueKind != JsonValueKind.Number)
        {
            error = $"Field '{name}' must be a JSON integer.";
            return false;
        }

        // Reject non-integer numbers (fraction / exponent that is not integer).
        var raw = el.GetRawText();
        if (raw.Contains('.') || raw.Contains('e', StringComparison.OrdinalIgnoreCase))
        {
            error = $"Field '{name}' must be a JSON integer (no fraction/exponent).";
            return false;
        }

        if (!el.TryGetInt32(out value) || value < KeySenderConst.DelayMsMin)
        {
            error = $"Field '{name}' is out of range.";
            return false;
        }

        return true;
    }
}
