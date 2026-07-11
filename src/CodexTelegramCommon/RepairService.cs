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
        if (document.NotifyArgv is not null && InstallPlanner.IsExactBridgeArgv(document.NotifyArgv, expectedBridge))
        {
            queue.ClearHealth(HealthCodes.RepairPending, HealthCodes.ConfigConflict);
            return new RepairResult(RepairOutcome.Healthy, "BRIDGE_ALREADY_ACTIVE");
        }

        var configHash = Hashing.Sha256Hex(configBytes);
        var validation = vendorValidator.ValidateArgv(document.NotifyArgv ?? [], configHash, utcNow());
        if (!validation.IsValid || validation.Record is null)
        {
            TrySetHealth(queue, HealthCodes.ConfigConflict);
            return new RepairResult(RepairOutcome.Conflict, HealthCodes.ConfigConflict);
        }

        if (runningDesktopProcesses().Count > 0)
        {
            TrySetHealth(queue, HealthCodes.RepairPending);
            return new RepairResult(RepairOutcome.Pending, HealthCodes.RepairPending);
        }

        if (File.Exists(layout.ActiveJournalPath))
        {
            TrySetHealth(queue, HealthCodes.RepairPending);
            return new RepairResult(RepairOutcome.Pending, "JOURNAL_RECOVERY_REQUIRED");
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
            edit = configTransaction.ReplaceNotify(configPath, configHash, [expectedBridge, "hook"]);
            EnsureDesktopClosed();
            Advance("CONFIG_COMMITTED", edit.AfterSha256);
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
                        if (currentNotify is null || !InstallPlanner.IsExactBridgeArgv(currentNotify, expectedBridge))
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
