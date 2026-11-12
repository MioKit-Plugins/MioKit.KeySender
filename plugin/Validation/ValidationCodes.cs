namespace KeySender.Validation;

/// <summary>
/// Stable validation / warning codes used by AnalyzeSequence (design 04 §9).
/// </summary>
public static class ValidationCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string InvalidConfiguration = "invalid_configuration";
    public const string UnsupportedSequenceVersion = "unsupported_sequence_version";
    public const string UnsupportedKey = "unsupported_key";
    public const string UnsupportedKeyCombination = "unsupported_key_combination";
    public const string EmptySequence = "empty_sequence";
    public const string UnknownEventType = "unknown_event_type";
    public const string DuplicateEventId = "duplicate_event_id";
    public const string InvalidDelay = "invalid_delay";
    public const string EventCountOutOfRange = "event_count_out_of_range";
    public const string InvalidName = "invalid_name";
    public const string InvalidDescription = "invalid_description";
    public const string DelayTotalOverflow = "delay_total_overflow";

    public const string UnclosedKeyDown = "unclosed_keydown";
    public const string UnmatchedKeyUp = "unmatched_keyup";
    public const string RepeatedKeyDown = "repeated_keydown";

    public const string BuiltinReadOnly = "builtin_read_only";
    public const string BuiltinIdentityConflict = "builtin_identity_conflict";
    public const string CategoryNotFound = "category_not_found";
    public const string CategoryReadOnly = "category_read_only";
    public const string InvalidCategory = "invalid_category";
    public const string NodeNotFound = "node_not_found";
    public const string NodeNotReady = "node_not_ready";
    public const string NodeInUse = "node_in_use";
    public const string PersistenceFailed = "persistence_failed";
    public const string DeleteIncomplete = "delete_incomplete";
    public const string ExecutionNotReady = "execution_not_ready";
    public const string WindowSessionClosed = "window_session_closed";
    public const string Cancelled = "cancelled";

    public const string QueueFull = "queue_full";
    public const string TargetMissing = "target_missing";
    public const string TargetInvalid = "target_invalid";
    public const string TargetFocusFailed = "target_focus_failed";
    public const string OutputHotkeyConflict = "output_hotkey_conflict";
    public const string InputSendFailed = "input_send_failed";
    public const string PluginStopping = "plugin_stopping";
    public const string RecordingBusy = "recording_busy";
    public const string ExecutionBusy = "execution_busy";
    public const string RecordingUnavailable = "recording_unavailable";
    public const string RecordingNotReady = "recording_not_ready";

    public const string RecordingInterrupted = "recording_interrupted";
    public const string RecordingLimitReached = "recording_limit_reached";

    public const string InvalidFormat = "invalid_format";
    public const string UnsupportedFormatVersion = "unsupported_format_version";
    public const string FileTooLarge = "file_too_large";
    public const string ImportLimitExceeded = "import_limit_exceeded";
    public const string ExportLimitExceeded = "export_limit_exceeded";
    public const string ImportPreviewExpired = "import_preview_expired";
    public const string ImportInProgress = "import_in_progress";
    public const string ImportChanged = "import_changed";
    public const string ImportInvalid = "import_invalid";
    public const string FileAccessFailed = "file_access_failed";
}
