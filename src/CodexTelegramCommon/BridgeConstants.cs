namespace CodexTelegramCommon;

public static class BridgeConstants
{
    public const int SchemaVersion = 1;
    public const int MaxNotifyPayloadUtf16Length = 262_144;
    public const int MaxOpaqueIdUtf16Length = 256;
    public const int MaxPcNameGraphemes = 64;
    public const int MaxTitleGraphemes = 160;
    public const int MaxTelegramTitleGraphemes = 32;
    public const int MinTitleVerificationPrefixGraphemes = 12;
    public const int MaxPcNameUtf16Length = 512;
    public const int MaxTitleUtf16Length = 3_400;
    public const int MaxTelegramTextUtf16Length = 4_096;
    public const int RuntimeTelegramResponseLimitBytes = 64 * 1024;
    public const int EventIdLogPrefixLength = 12;
    public const string NotifyEventType = "agent-turn-complete";
    public const string CompletionLine = "✅ Codex 응답 완료";
    public const string LegacyRootSource = "vscode";
    public const string VendorExecutableName = "codex-computer-use.exe";
    public const string VendorArgument = "turn-ended";
    public const string VendorPreviousNotifyArgument = "--previous-notify";
    public const int MaxPreviousNotifyUtf16Length = 32_768;
    public static readonly TimeSpan HookDatabaseBusyTimeout = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan WorkerLeaseDuration = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ResolverQuarantineAge = TimeSpan.FromHours(24);
}

public enum CaptureMode
{
    Shadow,
    Live,
}

public enum EventState
{
    Pending,
    Inflight,
    Sent,
    Shadow,
    Suppressed,
    Quarantine,
}

public enum ResolutionKind
{
    RootReady,
    Subagent,
    NotReady,
    Unsupported,
}

public enum HealthSeverity
{
    Ok,
    Degraded,
    Blocked,
}

public static class HealthCodes
{
    public const string AuthBlocked = "AUTH_BLOCKED";
    public const string ChatBlocked = "CHAT_BLOCKED";
    public const string TelegramApiBlocked = "TELEGRAM_API_BLOCKED";
    public const string TelegramRetrying = "TELEGRAM_RETRYING";
    public const string ConfigConflict = "CONFIG_CONFLICT";
    public const string CodexHomeConflict = "CODEX_HOME_CONFLICT";
    public const string InstallAclBlocked = "INSTALL_ACL_BLOCKED";
    public const string RepairPending = "REPAIR_PENDING";
    public const string UpstreamBlocked = "UPSTREAM_BLOCKED";
    public const string StateSchemaBlocked = "STATE_SCHEMA_BLOCKED";
    public const string EventQuarantined = "EVENT_QUARANTINED";
    public const string SpoolCorrupt = "SPOOL_CORRUPT";
    public const string LocalStateBlocked = "LOCAL_STATE_BLOCKED";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        AuthBlocked,
        ChatBlocked,
        TelegramApiBlocked,
        TelegramRetrying,
        ConfigConflict,
        CodexHomeConflict,
        InstallAclBlocked,
        RepairPending,
        UpstreamBlocked,
        StateSchemaBlocked,
        EventQuarantined,
        SpoolCorrupt,
        LocalStateBlocked,
    };

    public static readonly IReadOnlySet<string> Blocking = new HashSet<string>(StringComparer.Ordinal)
    {
        AuthBlocked,
        ChatBlocked,
        TelegramApiBlocked,
        ConfigConflict,
        CodexHomeConflict,
        InstallAclBlocked,
        RepairPending,
        UpstreamBlocked,
        LocalStateBlocked,
    };

    public static readonly IReadOnlySet<string> NetworkBlocking = new HashSet<string>(StringComparer.Ordinal)
    {
        AuthBlocked,
        ChatBlocked,
        TelegramApiBlocked,
        InstallAclBlocked,
        LocalStateBlocked,
    };
}
