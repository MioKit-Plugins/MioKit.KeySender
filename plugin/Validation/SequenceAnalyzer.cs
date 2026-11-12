using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KeySender.Models;

namespace KeySender.Validation;

/// <summary>
/// Pure domain validation and pairing analysis for <see cref="NodeWrite"/>.
/// </summary>
public static class SequenceAnalyzer
{
    private static readonly string[] CtrlTokens = ["LeftControl", "RightControl"];
    private static readonly string[] AltTokens = ["LeftAlt", "RightAlt"];
    private const string DeleteToken = "Delete";

    public static SequenceAnalysisResult AnalyzeSequence(NodeWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var hardErrors = new List<ValidationIssue>();
        var warnings = new List<ValidationIssue>();

        ValidateName(write.Name, hardErrors);
        ValidateDescription(write.Description, hardErrors);
        ValidateDelay(write.BeforeDelayMs, "beforeDelayMs", "节点前等待", hardErrors);
        ValidateDelay(write.AfterDelayMs, "afterDelayMs", "节点后等待", hardErrors);

        var sequence = write.Sequence ?? new KeyboardSequence();
        var events = sequence.Events ?? [];

        if (sequence.Version != KeySenderConst.SequenceVersion)
        {
            hardErrors.Add(new ValidationIssue
            {
                Code = ValidationCodes.UnsupportedSequenceVersion,
                FieldPath = "sequence.version",
                Message = $"序列版本 {sequence.Version} 不受支持，请使用版本 {KeySenderConst.SequenceVersion}",
            });
        }

        if (events.Count < KeySenderConst.EventCountMin || events.Count > KeySenderConst.EventCountMax)
        {
            hardErrors.Add(new ValidationIssue
            {
                Code = events.Count == 0
                    ? ValidationCodes.EmptySequence
                    : ValidationCodes.EventCountOutOfRange,
                FieldPath = "sequence.events",
                Message = events.Count == 0
                    ? "请至少添加一个按键事件"
                    : $"按键事件数量必须在 {KeySenderConst.EventCountMin}–{KeySenderConst.EventCountMax} 条之间",
            });
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var pressed = new HashSet<string>(StringComparer.Ordinal);
        var lastDownIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var cadReported = false;
        long delayTotal = write.BeforeDelayMs;
        delayTotal = checkedAdd(delayTotal, write.AfterDelayMs, hardErrors);

        if (!cadReported && IsCtrlAltDelete(pressed))
        {
            cadReported = true;
            hardErrors.Add(new ValidationIssue
            {
                Code = ValidationCodes.UnsupportedKeyCombination,
                FieldPath = "sequence.events",
                Message = "不支持 Ctrl+Alt+Delete 组合键（包括已按住的键）",
            });
        }

        for (var i = 0; i < events.Count; i++)
        {
            var ev = events[i] ?? new KeyboardEvent();
            var path = $"sequence.events[{i.ToString(CultureInfo.InvariantCulture)}]";

            ValidateDelay(ev.DelayBeforeMs, $"{path}.delayBeforeMs", "等待", hardErrors, eventNumber: i + 1);
            delayTotal = checkedAdd(delayTotal, ev.DelayBeforeMs, hardErrors);

            if (string.IsNullOrEmpty(ev.EventId))
            {
                hardErrors.Add(new ValidationIssue
                {
                    Code = ValidationCodes.InvalidConfiguration,
                    FieldPath = $"{path}.eventId",
                    EventIndex = i,
                    Message = $"第 {i + 1} 条事件缺少标识",
                });
            }
            else if (!seenIds.Add(ev.EventId))
            {
                hardErrors.Add(new ValidationIssue
                {
                    Code = ValidationCodes.DuplicateEventId,
                    FieldPath = $"{path}.eventId",
                    EventIndex = i,
                    Message = $"第 {i + 1} 条事件的标识重复",
                });
            }

            var typeOk = IsKnownEventType(ev.Type);
            if (!typeOk)
            {
                hardErrors.Add(new ValidationIssue
                {
                    Code = ValidationCodes.UnknownEventType,
                    FieldPath = $"{path}.type",
                    EventIndex = i,
                    Message = $"第 {i + 1} 条事件的类型无效",
                });
            }

            var keyOk = KeyTokenCatalog.Contains(ev.Key);
            if (!keyOk)
            {
                hardErrors.Add(new ValidationIssue
                {
                    Code = ValidationCodes.UnsupportedKey,
                    FieldPath = $"{path}.key",
                    Key = ev.Key,
                    EventIndex = i,
                    Message = $"第 {i + 1} 条事件的按键无效",
                });
            }

            if (!typeOk || !keyOk)
            {
                continue;
            }

            if (string.Equals(ev.Type, KeySenderConst.EventTypeKeyDown, StringComparison.Ordinal))
            {
                if (pressed.Contains(ev.Key))
                {
                    warnings.Add(new ValidationIssue
                    {
                        Code = ValidationCodes.RepeatedKeyDown,
                        Key = ev.Key,
                        EventIndex = i,
                        FieldPath = path,
                        Message = $"Key '{ev.Key}' is already down (repeated keyDown).",
                    });
                }
                else
                {
                    pressed.Add(ev.Key);
                }

                // Last Down that leaves the key held (including repeats).
                lastDownIndex[ev.Key] = i;
            }
            else
            {
                if (!pressed.Remove(ev.Key))
                {
                    warnings.Add(new ValidationIssue
                    {
                        Code = ValidationCodes.UnmatchedKeyUp,
                        Key = ev.Key,
                        EventIndex = i,
                        FieldPath = path,
                        Message = $"Key '{ev.Key}' keyUp has no matching local keyDown.",
                    });
                }
            }

            if (!cadReported && IsCtrlAltDelete(pressed))
            {
                cadReported = true;
                hardErrors.Add(new ValidationIssue
                {
                    Code = ValidationCodes.UnsupportedKeyCombination,
                    FieldPath = path,
                    EventIndex = i,
                    Message = "不支持 Ctrl+Alt+Delete 组合键",
                });
            }
        }

        foreach (var key in pressed.OrderBy(k => lastDownIndex.TryGetValue(k, out var idx) ? idx : int.MaxValue)
                     .ThenBy(k => k, StringComparer.Ordinal))
        {
            var index = lastDownIndex.TryGetValue(key, out var idx) ? idx : (int?)null;
            warnings.Add(new ValidationIssue
            {
                Code = ValidationCodes.UnclosedKeyDown,
                Key = key,
                EventIndex = index,
                FieldPath = index is int i
                    ? $"sequence.events[{i.ToString(CultureInfo.InvariantCulture)}]"
                    : "sequence.events",
                Message = $"Key '{key}' remains down at end of sequence.",
            });
        }

        var fingerprint = ComputeWarningFingerprint(write.BeforeDelayMs, write.AfterDelayMs, events);

        return new SequenceAnalysisResult
        {
            HardErrors = hardErrors,
            Warnings = warnings,
            WarningFingerprint = fingerprint,
        };
    }

    public static string ComputeWarningFingerprint(int beforeDelayMs, int afterDelayMs, IReadOnlyList<KeyboardEvent> events)
    {
        var json = BuildSemanticJson(beforeDelayMs, afterDelayMs, events ?? []);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Package-level warning fingerprint: ordered semantic node configs (no name/description).
    /// Returns null when no node has warnings.
    /// </summary>
    public static string? ComputePackageWarningFingerprint(IReadOnlyList<NodeWrite> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var anyWarning = false;
        var parts = new string[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            var write = nodes[i] ?? new NodeWrite();
            var analysis = AnalyzeSequence(write);
            if (analysis.HasWarnings)
            {
                anyWarning = true;
            }

            parts[i] = BuildSemanticJson(write.BeforeDelayMs, write.AfterDelayMs, write.Sequence?.Events ?? []);
        }

        if (!anyWarning)
        {
            return null;
        }

        var json = "[" + string.Join(",", parts) + "]";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string BuildSemanticJson(int beforeDelayMs, int afterDelayMs, IReadOnlyList<KeyboardEvent> events)
    {
        var sb = new StringBuilder(64 + events.Count * 48);
        sb.Append("{\"beforeDelayMs\":");
        sb.Append(beforeDelayMs.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"afterDelayMs\":");
        sb.Append(afterDelayMs.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"events\":[");

        for (var i = 0; i < events.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            var ev = events[i] ?? new KeyboardEvent();
            sb.Append("{\"type\":");
            AppendJsonString(sb, ev.Type ?? "");
            sb.Append(",\"key\":");
            AppendJsonString(sb, ev.Key ?? "");
            sb.Append(",\"delayBeforeMs\":");
            sb.Append(ev.DelayBeforeMs.ToString(CultureInfo.InvariantCulture));
            sb.Append('}');
        }

        sb.Append("]}");
        return sb.ToString();
    }

    private static void AppendJsonString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (ch < 0x20)
                    {
                        sb.Append("\\u");
                        sb.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(ch);
                    }

                    break;
            }
        }

        sb.Append('"');
    }

    private static bool IsKnownEventType(string? type)
        => string.Equals(type, KeySenderConst.EventTypeKeyDown, StringComparison.Ordinal)
           || string.Equals(type, KeySenderConst.EventTypeKeyUp, StringComparison.Ordinal);

    private static bool IsCtrlAltDelete(HashSet<string> pressed)
    {
        if (!pressed.Contains(DeleteToken))
        {
            return false;
        }

        var hasCtrl = false;
        foreach (var ctrl in CtrlTokens)
        {
            if (pressed.Contains(ctrl))
            {
                hasCtrl = true;
                break;
            }
        }

        if (!hasCtrl)
        {
            return false;
        }

        foreach (var alt in AltTokens)
        {
            if (pressed.Contains(alt))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateName(string? name, List<ValidationIssue> hardErrors)
    {
        var message = name?.Contains('\0') == true
            ? "名称含有无效字符"
            : (name?.Trim().Length ?? 0) switch
            {
                < KeySenderConst.NameMinLength => "名称不能为空",
                > KeySenderConst.NameMaxLength => $"名称不能超过 {KeySenderConst.NameMaxLength} 个字符",
                _ => null,
            };
        if (message is not null)
        {
            hardErrors.Add(new ValidationIssue
            {
                Code = ValidationCodes.InvalidName,
                FieldPath = "name",
                Message = message,
            });
        }
    }

    private static void ValidateDescription(string? description, List<ValidationIssue> hardErrors)
    {
        if (description is null || description.IndexOf('\0') >= 0)
        {
            hardErrors.Add(new ValidationIssue
            {
                Code = ValidationCodes.InvalidDescription,
                FieldPath = "description",
                Message = description is null ? "缺少描述字段" : "描述含有无效字符",
            });
            return;
        }

        if (description.Length > KeySenderConst.DescriptionMaxLength)
        {
            hardErrors.Add(new ValidationIssue
            {
                Code = ValidationCodes.InvalidDescription,
                FieldPath = "description",
                Message = $"描述不能超过 {KeySenderConst.DescriptionMaxLength} 个字符",
            });
        }
    }

    private static void ValidateDelay(
        int delayMs, string fieldPath, string label, List<ValidationIssue> hardErrors, int? eventNumber = null)
    {
        if (delayMs < KeySenderConst.DelayMsMin || delayMs > KeySenderConst.DelayMsMax)
        {
            var fieldLabel = eventNumber is int number ? $"第 {number} 条事件的{label}" : label;
            hardErrors.Add(new ValidationIssue
            {
                Code = ValidationCodes.InvalidDelay,
                FieldPath = fieldPath,
                Message = $"{fieldLabel}必须在 {KeySenderConst.DelayMsMin}–{KeySenderConst.DelayMsMax} 毫秒之间",
            });
        }
    }

    private static long checkedAdd(long total, int value, List<ValidationIssue> hardErrors)
    {
        try
        {
            return checked(total + value);
        }
        catch (OverflowException)
        {
            if (hardErrors.All(e => e.Code != ValidationCodes.DelayTotalOverflow))
            {
                hardErrors.Add(new ValidationIssue
                {
                    Code = ValidationCodes.DelayTotalOverflow,
                    FieldPath = "sequence",
                    Message = "总等待时间超过可处理范围",
                });
            }

            return total;
        }
    }
}
