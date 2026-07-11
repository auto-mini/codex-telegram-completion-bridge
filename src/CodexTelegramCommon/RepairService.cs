using System.Security.Cryptography;
using System.Text.Json;

namespace CodexTelegramCommon;

public enum RepairOutcome
{
    Healthy,
    Repaired,
    Pending,
    Conflict,
    Blocked,
}

public sealed record RepairResult(RepairOutcome Outcome, string OperationCode);

public sealed class RepairService(
    ISecretProtector protector,
    VendorExecutableValidator vendorValidator,
    Func<DateTimeOffset> utcNow,
    Func<string> currentSid,
    Func<IReadOnlyList<string>> runningDesktopProcesses,
    Action<string> signalWorker,
    Action<string> phaseFault)
{
    private static readonly IReadOnlyList<string> Phases =
    [
        "REPAIR_PLANNED", "UPSTREAM_CAPTURED", "CONFIG_PENDING", "CONFIG_COMMITTED", "COMMITTED",
    ];

    public static RepairService CreateProduction() => new(
        new DpapiSecretProtector(),
        VendorExecutableValidator.ForCurrentUser(),
        () => DateTimeOffset.UtcNow,
        () => CurrentUserContext.Sid,
        DesktopProcessGuard.FindRunning,
        WorkerCoordination.SignalExistingOrCreate,
        _ => { });

    public RepairResult Run(InstallationLayout layout)
    {
        using var mutationLock = new InstallationMutationLock(currentSid());
        if (!mutationLock.TryAcquire())
        {
            return new RepairResult(RepairOutcome.Pending, "INSTALL_MUTATION_BUSY");
        }

        var queue = new QueueStore(layout.DatabasePath);
        var acl = WindowsAclManager.VerifyTree(layout.Root, currentSid());
        if (!acl.IsValid)
        {
            TrySetHealth(queue, HealthCodes.InstallAclBlocked);
            return new RepairResult(RepairOutcome.Blocked, HealthCodes.InstallAclBlocked);
        }

        queue.ClearHealth(HealthCodes.InstallAclBlocked);
        if (File.Exists(layout.WorkerStopMarkerPath) && !File.Exists(layout.ActiveJournalPath))
        {
            BridgeProcessGuard.ClearStopRequest(layout);
        }

        if (File.Exists(layout.ActiveJournalPath))
        {
            return RecoverUnfinished(layout, queue);
        }

        RuntimeConfig runtime;
        try
        {
            runtime = new RuntimeConfigStore(layout.RuntimeConfigPath).Load();
            var installation = JsonSerializer.Deserialize<InstallationRecord>(AtomicFile.ReadUtf8(layout.TransactionRecordPath), JsonDefaults.Options)
                               ?? throw new InvalidDataException("Installation record is empty.");
            installation.Validate();
            if (installation.State != InstallationState.Active || !runtime.AutoRepairVendorNotify)
            {
                return new RepairResult(RepairOutcome.Blocked, "REPAIR_NOT_ACTIVE");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            TrySetHealth(queue, HealthCodes.LocalStateBlocked);
            return new RepairResult(RepairOutcome.Blocked, HealthCodes.LocalStateBlocked);
        }

        var configPath = Path.Combine(runtime.CodexHome, "config.toml");
        byte[] configBytes;
        CodexConfigDocument document;
        try
        {
            configBytes = File.ReadAllBytes(configPath);
            document = CodexConfigDocument.Parse(configBytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            TrySetHealth(queue, HealthCodes.ConfigConflict);
            return new RepairResult(RepairOutcome.Conflict, HealthCodes.ConfigConflict);
        }

        var expectedBridge = Path.Combine(layout.Bin, "CodexTelegramBridge.exe");
        var match = BridgeNotifyCommand.Match(document.NotifyArgv, expectedBridge);
        if (match.Shape == BridgeNotifyShape.Direct)
        {
            queue.ClearHealth(HealthCodes.RepairPending, HealthCodes.ConfigConflict);
            return new RepairResult(RepairOutcome.Healthy, "BRIDGE_ALREADY_ACTIVE");
        }

        var configHash = Hashing.Sha256Hex(configBytes);
        var preserveWrappedBridge = match.Shape == BridgeNotifyShape.VendorWrapped;
        var validation = vendorValidator.ValidateArgv(
            preserveWrappedBridge ? match.OuterVendorArgv ?? [] : document.NotifyArgv ?? [],
            configHash,
            utcNow());
        if (!validation.IsValid || validation.Record is null)
        {
            TrySetHealth(queue, HealthCodes.ConfigConflict);
            return new RepairResult(RepairOutcome.Conflict, HealthCodes.ConfigConflict);
        }

        if (preserveWrappedBridge && MatchesStoredUpstream(layout, validation.Record))
        {
            queue.ClearHealth(HealthCodes.RepairPending, HealthCodes.ConfigConflict, HealthCodes.UpstreamBlocked);
            return new RepairResult(RepairOutcome.Healthy, "BRIDGE_ALREADY_ACTIVE_WRAPPED");
        }

        if (runningDesktopProcesses().Count > 0)
        {
            TrySetHealth(queue, HealthCodes.RepairPending);
            return new RepairResult(RepairOutcome.Pending, HealthCodes.RepairPending);
        }

        var transactionId = Guid.NewGuid().ToString("D");
        var backupPath = Path.Combine(layout.BackupsDirectory, $"repair-{utcNow():yyyyMMddTHHmmssZ}-{configHash[..12]}.dpapi");
        var journal = new TransactionJournal(
            BridgeConstants.SchemaVersion,
            transactionId,
            JournalKind.Repair,
            "REPAIR_PLANNED",
            new string('0', 64),
            configHash,
            null,
            layout.Root,
            backupPath,
            utcNow());
        var journalStore = new TransactionJournalStore(layout.ActiveJournalPath, currentSid());
        var configTransaction = new ConfigFileTransaction(protector);
        var runtimeBytes = File.ReadAllBytes(layout.RuntimeConfigPath);
        var upstreamBytes = File.ReadAllBytes(layout.UpstreamPath);
        ConfigEditResult? edit = null;

        void Advance(string phase, string? after = null)
        {
            if (!Phases.Contains(phase, StringComparer.Ordinal))
            {
                throw new ArgumentOutOfRangeException(nameof(phase));
            }

            journal = journal with { Phase = phase, ConfigAfterSha256 = after ?? journal.ConfigAfterSha256, UpdatedAtUtc = utcNow() };
            journalStore.Save(journal);
            phaseFault(phase);
        }

        try
        {
            new RuntimeConfigStore(layout.RuntimeConfigPath).Save(CopyRuntime(runtime, deliveryPaused: true));
            signalWorker(runtime.MachineId);
            Advance("REPAIR_PLANNED");
            configTransaction.CreateBackup(configPath, configHash, backupPath, utcNow());
            new ProtectedJsonStore<UpstreamRecord>(layout.UpstreamPath, protector).Save(validation.Record);
            Advance("UPSTREAM_CAPTURED");
            EnsureDesktopClosed();
            EnsureHash(configPath, configHash);
            Advance("CONFIG_PENDING");
            if (!preserveWrappedBridge)
            {
                edit = configTransaction.ReplaceNotify(configPath, configHash, [expectedBridge, "hook"]);
            }
            EnsureDesktopClosed();
            Advance("CONFIG_COMMITTED", edit?.AfterSha256 ?? configHash);
            new RuntimeConfigStore(layout.RuntimeConfigPath).Save(runtime);
            queue.ClearHealth(HealthCodes.RepairPending, HealthCodes.ConfigConflict, HealthCodes.UpstreamBlocked);
            Advance("COMMITTED");
            File.Delete(layout.ActiveJournalPath);
            return new RepairResult(RepairOutcome.Repaired, "REPAIR_COMPLETE");
        }
        catch
        {
            try
            {
                if (File.Exists(backupPath))
                {
                    var currentBytes = File.Exists(configPath) ? File.ReadAllBytes(configPath) : [];
                    var currentHash = Hashing.Sha256Hex(currentBytes);
                    if (!string.Equals(currentHash, configHash, StringComparison.Ordinal))
                    {
                        var currentNotify = CodexConfigDocument.Parse(currentBytes).NotifyArgv;
                        if (!IsActiveBridgeNotify(currentNotify, expectedBridge, currentHash))
                        {
                            throw new InvalidOperationException("CONFIG_RESTORE_CONFLICT");
                        }

                        configTransaction.RestoreBackup(backupPath, currentHash);
                    }
                }

                AtomicFile.WriteBytes(layout.RuntimeConfigPath, runtimeBytes);
                AtomicFile.WriteBytes(layout.UpstreamPath, upstreamBytes);
                queue.UpsertHealth(HealthCodes.RepairPending, utcNow());
                if (File.Exists(layout.ActiveJournalPath))
                {
                    File.Delete(layout.ActiveJournalPath);
                }
            }
            catch
            {
                queue.UpsertHealth(HealthCodes.LocalStateBlocked, utcNow());
            }

            return new RepairResult(RepairOutcome.Blocked, "REPAIR_FAILED");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(runtimeBytes);
            CryptographicOperations.ZeroMemory(upstreamBytes);
        }
    }

    private RepairResult RecoverUnfinished(InstallationLayout layout, QueueStore queue)
    {
        TransactionJournal journal;
        try
        {
            journal = new TransactionJournalStore(layout.ActiveJournalPath, currentSid()).Load();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            TrySetHealth(queue, HealthCodes.LocalStateBlocked);
            return new RepairResult(RepairOutcome.Blocked, "JOURNAL_INVALID");
        }

        if (journal.Kind != JournalKind.Repair)
        {
            TrySetHealth(queue, HealthCodes.RepairPending);
            return new RepairResult(RepairOutcome.Pending, "JOURNAL_RECOVERY_REQUIRED");
        }

        if (runningDesktopProcesses().Count > 0)
        {
            TrySetHealth(queue, HealthCodes.RepairPending);
            return new RepairResult(RepairOutcome.Pending, HealthCodes.RepairPending);
        }

        try
        {
            if (string.Equals(journal.Phase, "COMMITTED", StringComparison.Ordinal))
            {
                File.Delete(layout.ActiveJournalPath);
                queue.ClearHealth(HealthCodes.RepairPending, HealthCodes.ConfigConflict);
                return new RepairResult(RepairOutcome.Healthy, "REPAIR_COMMIT_RECOVERED");
            }

            if (File.Exists(journal.BackupPath))
            {
                var backup = new ProtectedJsonStore<ConfigBackupRecord>(journal.BackupPath, protector).Load();
                backup.Validate();
                var currentBytes = File.Exists(backup.ConfigPath) ? File.ReadAllBytes(backup.ConfigPath) : [];
                var currentHash = Hashing.Sha256Hex(currentBytes);
                if (!string.Equals(currentHash, journal.ConfigBeforeSha256, StringComparison.Ordinal))
                {
                    var runtime = new RuntimeConfigStore(layout.RuntimeConfigPath).Load();
                    var notify = CodexConfigDocument.Parse(currentBytes).NotifyArgv;
                    if (!IsActiveBridgeNotify(
                            notify,
                            Path.Combine(layout.Bin, "CodexTelegramBridge.exe"),
                            currentHash))
                    {
                        throw new InvalidOperationException("JOURNAL_CONFIG_CONFLICT");
                    }

                    new ConfigFileTransaction(protector).RestoreBackup(journal.BackupPath, currentHash);
                    signalWorker(runtime.MachineId);
                }
            }

            File.Delete(layout.ActiveJournalPath);
            queue.UpsertHealth(HealthCodes.RepairPending, utcNow());
            return new RepairResult(RepairOutcome.Pending, "REPAIR_ROLLBACK_RECOVERED");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or System.Security.Cryptography.CryptographicException or JsonException)
        {
            TrySetHealth(queue, HealthCodes.LocalStateBlocked);
            return new RepairResult(RepairOutcome.Blocked, "REPAIR_RECOVERY_FAILED");
        }
    }

    private bool MatchesStoredUpstream(InstallationLayout layout, UpstreamRecord candidate)
    {
        try
        {
            var stored = new ProtectedJsonStore<UpstreamRecord>(layout.UpstreamPath, protector).Load();
            if (!vendorValidator.ValidateCaptured(stored).IsValid)
            {
                return false;
            }

            return stored.Kind == candidate.Kind &&
                   stored.Argv.Count == 2 &&
                   candidate.Argv.Count == 2 &&
                   string.Equals(stored.Argv[0], candidate.Argv[0], StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(stored.Argv[1], candidate.Argv[1], StringComparison.Ordinal) &&
                   string.Equals(stored.ExecutableSha256, candidate.ExecutableSha256, StringComparison.Ordinal) &&
                   stored.ExecutableSizeBytes == candidate.ExecutableSizeBytes;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }

    private bool IsActiveBridgeNotify(IReadOnlyList<string>? notify, string expectedBridge, string configHash)
    {
        var match = BridgeNotifyCommand.Match(notify, expectedBridge);
        return match.Shape == BridgeNotifyShape.Direct ||
               match.Shape == BridgeNotifyShape.VendorWrapped &&
               vendorValidator.ValidateArgv(match.OuterVendorArgv ?? [], configHash, utcNow()).IsValid;
    }

    private static RuntimeConfig CopyRuntime(RuntimeConfig value, bool deliveryPaused) => new()
    {
        MachineId = value.MachineId,
        PcAlias = value.PcAlias,
        CodexHome = value.CodexHome,
        CaptureMode = value.CaptureMode,
        DeliveryPaused = deliveryPaused,
        AutoRepairVendorNotify = value.AutoRepairVendorNotify,
    };

    private void EnsureDesktopClosed()
    {
        if (runningDesktopProcesses().Count > 0)
        {
            throw new InvalidOperationException("DESKTOP_PROCESSES_RUNNING");
        }
    }

    private static void EnsureHash(string path, string expected)
    {
        if (!string.Equals(Hashing.Sha256File(path), expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("CONFIG_HASH_CHANGED");
        }
    }

    private void TrySetHealth(QueueStore queue, string code)
    {
        try
        {
            queue.UpsertHealth(code, utcNow());
        }
        catch (Exception)
        {
        }
    }
}
