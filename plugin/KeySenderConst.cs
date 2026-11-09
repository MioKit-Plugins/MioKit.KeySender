namespace KeySender;

/// <summary>
/// Fixed type, property, and built-in instance identities from design doc 02 §10.
/// Do not regenerate these GUIDs.
/// </summary>
public static class KeySenderConst
{
    public const string PluginId = "com.tinyisle.plugin.key-sender";

    public const string PluginTypeId = "52DC58C9-C304-45ED-A5B8-38B57E626519";
    public static readonly Guid PluginTypeGuid = Guid.Parse(PluginTypeId);

    /// <summary>Alias for existing plugin entry MioType mapping.</summary>
    public static readonly Guid PluginType = PluginTypeGuid;

    public const string SearchGroupTypeId = "D3244F5B-5AE1-4BA0-9BED-F8A2766BC71E";
    public static readonly Guid SearchGroupTypeGuid = Guid.Parse(SearchGroupTypeId);

    /// <summary>Legacy manager-entry type id; node removed — kept for identity stability / cleanup.</summary>
    public const string ManagerNodeTypeId = "DDE52F58-EBCE-4212-80F9-B317AC4F6850";
    public static readonly Guid ManagerNodeTypeGuid = Guid.Parse(ManagerNodeTypeId);

    public const string KeySenderNodeTypeId = "52E4A1BC-D0D3-470B-88C8-4CEE269B5046";
    public static readonly Guid KeySenderNodeTypeGuid = Guid.Parse(KeySenderNodeTypeId);

    public const string CategoryTypeId = "9BEBDDC0-80B1-46FA-A8A7-DDF919C09352";
    public static readonly Guid CategoryTypeGuid = Guid.Parse(CategoryTypeId);

    public const string BeforeDelayMsPropertyId = "0474A0C5-AFD1-4D10-A469-11DAC00B32E5";
    public static readonly Guid BeforeDelayMsPropertyGuid = Guid.Parse(BeforeDelayMsPropertyId);

    public const string AfterDelayMsPropertyId = "7CF666D4-FDB3-4EC1-BC20-BFB429C51BE8";
    public static readonly Guid AfterDelayMsPropertyGuid = Guid.Parse(AfterDelayMsPropertyId);

    public const string SequencePropertyId = "E13906DF-FD5B-47C2-AD6D-83763C93212B";
    public static readonly Guid SequencePropertyGuid = Guid.Parse(SequencePropertyId);

    public const string BuiltInCopyNodeId = "E32C3712-9407-4A41-8DD8-134810F5F93D";
    public const string BuiltInCutNodeId = "00C1CF3E-39C4-464F-8D37-A80C47B414F3";
    public const string BuiltInPasteNodeId = "C13771F3-65A4-4F0B-A2B9-11798D8AFA24";

    /// <summary>Fixed search-group instance id for <c>EnsureTreeLoadedAsync</c>.</summary>
    public const string SearchGroupId = "key-sender-search-group";

    /// <summary>Legacy manager-entry instance id; cleaned up on start.</summary>
    public const string ManagerNodeId = "key-sender-manager-node";

    public const string CopyNameSuffix = " 副本";

    public const string ListScopeAll = "all";
    public const string ListScopeBuiltin = "builtin";
    public const string ListScopeCategory = "category";

    public const int SequenceVersion = 2;
    public const int NameMinLength = 1;
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;
    public const int EventCountMin = 1;
    public const int EventCountMax = 10000;
    public const int DelayMsMin = 0;
    public const int DelayMsMax = int.MaxValue;

    public const string EventTypeKeyDown = "keyDown";
    public const string EventTypeKeyUp = "keyUp";

    public const string TransferFormat = "miokit.key-sender";
    public const int TransferFormatVersion = 2;
    public const int TransferMaxBytes = 5 * 1024 * 1024;
    public const int TransferMaxNodes = 500;
    public const int TransferMaxJsonDepth = 16;
    public const int TransferPreviewTtlSeconds = 5 * 60;

    public const int RecordingEventCap = 10000;
    public const int RecordingDrainTimeoutMs = 2000;
    public const int RecordingBaselineTimeoutMs = 2000;
}
