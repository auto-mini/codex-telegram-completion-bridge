using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CodexTelegramCommon;

public sealed record DoctorTaskStatus(string Name, bool Exists, bool Enabled, int? LastResult);

public sealed record DoctorReport(
    string Overall,
    IReadOnlyList<string> Conditions,
    IReadOnlyDictionary<string, string> Checks,
    CaptureMode? CaptureMode,
    bool? DeliveryPaused,
    QueueCounts? Queue,
    long EmergencySpool,
    long CorruptSpool,
    long? OldestPendingAgeSeconds,
    DateTimeOffset? LastSuccessfulSendUtc,
    IReadOnlyList<DoctorTaskStatus> Tasks);

public sealed class DoctorService(
    ISecretProtector protector,
    VendorExecutableValidator vendorValidator,
    IScheduledTaskManager taskManager,
    Func<string, ITelegramBotClient> telegramFactory,
    Func<DateTimeOffset> utcNow,
    Func<string> currentSid,
    Func<string> currentCodexHome)
{
    public static DoctorService CreateProduction() => new(
        new DpapiSecretProtector(),
        VendorExecutableValidator.ForCurrentUser(),
        new WindowsScheduledTaskManager(),
        token => new TelegramBotClient(token),
        () => DateTimeOffset.UtcNow,
        () => CurrentUserContext.Sid,
        CurrentUserContext.ResolveCodexHome);

    public async Task<DoctorReport> RunAsync(InstallationLayout layout, bool online, CancellationToken cancellationToken)
    {
        var conditions = new HashSet<string>(StringComparer.Ordinal);
        var checks = new SortedDictionary<string, string>(StringComparer.Ordinal);
        RuntimeConfig? runtime = null;
        QueueCounts? counts = null;
        long spoolCount = 0;
        long corruptCount = 0;
        DateTimeOffset? oldest = null;
        DateTimeOffset? lastSent = null;
        TelegramCredentials? credentials = null;

        var acl = WindowsAclManager.VerifyTree(layout.Root, currentSid());
        checks["installation_acl"] = acl.OperationCode;
        if (!acl.IsValid)
        {
            conditions.Add(HealthCodes.InstallAclBlocked);
        }

        if (File.Exists(layout.LocalStateBlockedMarkerPath))
        {
            checks["local_state_marker"] = "BLOCKED";
            conditions.Add(HealthCodes.LocalStateBlocked);
        }
        else
        {
            checks["local_state_marker"] = "CLEAR";
        }

        try
        {
            var manifest = PackageManifest.LoadAndVerify(layout.Root, allowInstalledMutableFiles: true);
            checks["package_manifest"] = $"OK_{manifest.Entries.Count}";
            checks["package_authenticode"] = "UNSIGNED";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            checks["package_manifest"] = "PACKAGE_MANIFEST_INVALID";
            checks["package_authenticode"] = "INVALID_OR_UNREADABLE";
            conditions.Add(HealthCodes.LocalStateBlocked);
        }

        try
        {
            var record = JsonSerializer.Deserialize<InstallationRecord>(AtomicFile.ReadUtf8(layout.TransactionRecordPath), JsonDefaults.Options)
                         ?? throw new InvalidDataException("Empty record.");
            record.Validate();
            checks["installation_record"] = record.State == InstallationState.Active ? "ACTIVE" : "ROLLED_BACK";
            if (record.State != InstallationState.Active || !string.Equals(record.UserSid, currentSid(), StringComparison.Ordinal))
            {
                conditions.Add(HealthCodes.ConfigConflict);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            checks["installation_record"] = "INVALID";
            conditions.Add(HealthCodes.ConfigConflict);
        }

        try
        {
            runtime = new RuntimeConfigStore(layout.RuntimeConfigPath).Load();
            checks["runtime_config"] = "OK";
            if (!string.Equals(Path.GetFullPath(runtime.CodexHome), Path.GetFullPath(currentCodexHome()), StringComparison.OrdinalIgnoreCase))
            {
                conditions.Add(HealthCodes.CodexHomeConflict);
            }

            var configPath = Path.Combine(runtime.CodexHome, "config.toml");
            var configBytes = File.ReadAllBytes(configPath);
            var notify = CodexConfigDocument.Parse(configBytes).NotifyArgv;
            var match = BridgeNotifyCommand.Match(notify, Path.Combine(layout.Bin, "CodexTelegramBridge.exe"));
            var notifyActive = match.Shape == BridgeNotifyShape.Direct;
            if (match.Shape == BridgeNotifyShape.VendorWrapped)
            {
                notifyActive = vendorValidator.ValidateArgv(
                    match.OuterVendorArgv ?? [],
                    Hashing.Sha256Hex(configBytes),
                    utcNow()).IsValid;
            }

            checks["codex_notify"] = notifyActive
                ? match.Shape == BridgeNotifyShape.VendorWrapped ? "BRIDGE_ACTIVE_WRAPPED" : "BRIDGE_ACTIVE"
                : "CONFIG_CONFLICT";
            if (!notifyActive)
            {
                conditions.Add(HealthCodes.ConfigConflict);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            checks["runtime_config"] = "INVALID";
            conditions.Add(HealthCodes.LocalStateBlocked);
        }

        try
        {
            var upstream = new ProtectedJsonStore<UpstreamRecord>(layout.UpstreamPath, protector).Load();
            var result = vendorValidator.ValidateCaptured(upstream);
            checks["upstream"] = result.OperationCode;
            if (!result.IsValid)
            {
                conditions.Add(HealthCodes.UpstreamBlocked);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or JsonException or InvalidDataException)
        {
            checks["upstream"] = "UPSTREAM_STATE_INVALID";
            conditions.Add(HealthCodes.UpstreamBlocked);
        }

        try
        {
            var queue = new QueueStore(layout.DatabasePath);
            queue.ValidateExistingSchema();
            if (!string.Equals(queue.QuickCheck(), "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("quick_check failed.");
            }

            checks["bridge_state"] = "OK";
            counts = queue.GetCounts();
            oldest = queue.GetOldestPendingUtc();
            lastSent = queue.GetLastSuccessfulSendUtc();
            foreach (var condition in queue.GetHealthConditions())
            {
                conditions.Add(condition.ConditionCode);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {
            checks["bridge_state"] = "INVALID";
            conditions.Add(HealthCodes.LocalStateBlocked);
        }

        try
        {
            var spool = new EmergencySpool(layout.SpoolDirectory);
            spoolCount = spool.CountPending();
            corruptCount = spool.CountCorrupt();
            checks["emergency_spool"] = corruptCount == 0 ? "OK" : "CORRUPT_PRESENT";
            if (corruptCount > 0)
            {
                conditions.Add(HealthCodes.SpoolCorrupt);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            checks["emergency_spool"] = "UNREADABLE";
            conditions.Add(HealthCodes.LocalStateBlocked);
        }

        if (runtime is not null)
        {
            var resolver = new CodexStateResolver(runtime.CodexHome);
            var resolution = resolver.Resolve(Guid.NewGuid().ToString("D"));
            var titleIndexCompatible = resolver.HasCompatibleTitleIndex();
            checks["codex_title_index"] = titleIndexCompatible ? "COMPATIBLE_OR_ABSENT" : "UNSUPPORTED_OR_BUSY";
            var compatible = resolver.HasAnyCompatibleDatabase() &&
                             titleIndexCompatible &&
                             resolution.Kind != ResolutionKind.Unsupported;
            checks["codex_state_schema"] = compatible ? "COMPATIBLE" : "UNSUPPORTED_OR_MISSING";
            if (!compatible)
            {
                conditions.Add(HealthCodes.StateSchemaBlocked);
            }
        }

        if (File.Exists(layout.TelegramCredentialsPath))
        {
            try
            {
                credentials = new ProtectedJsonStore<TelegramCredentials>(layout.TelegramCredentialsPath, protector).Load();
                credentials.Validate();
                checks["telegram_credentials"] = "OK";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or JsonException or InvalidDataException)
            {
                checks["telegram_credentials"] = "INVALID";
                conditions.Add(HealthCodes.AuthBlocked);
            }
        }
        else
        {
            checks["telegram_credentials"] = "NOT_CONFIGURED";
            if (runtime?.CaptureMode == CaptureMode.Live)
            {
                conditions.Add(HealthCodes.AuthBlocked);
            }
        }

        if (File.Exists(layout.ActiveJournalPath))
        {
            checks["active_journal"] = "RECOVERY_REQUIRED";
            conditions.Add(HealthCodes.RepairPending);
        }
        else
        {
            checks["active_journal"] = "NONE";
        }

        if (File.Exists(layout.WorkerStopMarkerPath))
        {
            checks["worker_stop_marker"] = "PRESENT";
            conditions.Add(HealthCodes.RepairPending);
        }
        else
        {
            checks["worker_stop_marker"] = "CLEAR";
        }
        IReadOnlyList<DoctorTaskStatus> taskStatuses;
        try
        {
            taskStatuses = taskManager.GetStatuses()
                .Select(status => new DoctorTaskStatus(status.Name, status.Exists, status.Enabled, status.LastTaskResult))
                .ToArray();
            checks["scheduled_tasks"] = taskStatuses.Count == 2 && taskStatuses.All(status => status.Exists && status.Enabled)
                ? "OK"
                : "INVALID";
            if (!string.Equals(checks["scheduled_tasks"], "OK", StringComparison.Ordinal))
            {
                conditions.Add(HealthCodes.LocalStateBlocked);
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            taskStatuses = [];
            checks["scheduled_tasks"] = "UNREADABLE";
            conditions.Add(HealthCodes.LocalStateBlocked);
        }

        if (online && credentials is not null && !conditions.Contains(HealthCodes.InstallAclBlocked))
        {
            await RunOnlineChecksAsync(credentials, checks, conditions, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            checks["telegram_online"] = online ? "SKIPPED_BLOCKED" : "NOT_REQUESTED";
        }

        var ordered = conditions.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var overall = ordered.Any(HealthCodes.Blocking.Contains)
            ? "BLOCKED"
            : ordered.Length > 0 ? "DEGRADED" : "OK";
        return new DoctorReport(
            overall,
            ordered,
            checks,
            runtime?.CaptureMode,
            runtime?.DeliveryPaused,
            counts,
            spoolCount,
            corruptCount,
            oldest is null ? null : Math.Max(0, (long)(utcNow() - oldest.Value).TotalSeconds),
            lastSent,
            taskStatuses);
    }

    private async Task RunOnlineChecksAsync(
        TelegramCredentials credentials,
        IDictionary<string, string> checks,
        ISet<string> conditions,
        CancellationToken cancellationToken)
    {
        using var client = telegramFactory(credentials.BotToken);
        var me = await client.GetMeAsync(cancellationToken).ConfigureAwait(false);
        if (!me.Call.IsSuccess || me.Value?.Id != credentials.BotUserId)
        {
            AddTelegramCondition(me.Call, conditions);
            checks["telegram_online"] = "GET_ME_FAILED";
            return;
        }

        var chat = await client.GetChatAsync(credentials.ChatId, cancellationToken).ConfigureAwait(false);
        if (!chat.Call.IsSuccess || chat.Value?.Id != credentials.ChatId || !string.Equals(chat.Value.Type, "private", StringComparison.Ordinal))
        {
            AddTelegramCondition(chat.Call, conditions);
            checks["telegram_online"] = "GET_CHAT_FAILED";
            return;
        }

        checks["telegram_online"] = "OK";
    }

    private static void AddTelegramCondition(TelegramCallResult call, ISet<string> conditions)
    {
        conditions.Add(call.Outcome switch
        {
            TelegramCallOutcome.AuthBlocked => HealthCodes.AuthBlocked,
            TelegramCallOutcome.ChatBlocked => HealthCodes.ChatBlocked,
            TelegramCallOutcome.ApiBlocked => HealthCodes.TelegramApiBlocked,
            _ => HealthCodes.TelegramRetrying,
        });
    }
}
