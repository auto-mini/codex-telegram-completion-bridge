using System.Security.Cryptography;
using System.Text.Json;

namespace CodexTelegramCommon;

public sealed record InstallApplyResult(string MachineId, string InstallationRoot, string ConfigSha256, bool WasUpgrade);

public sealed class InstallApplyException(string operationCode, bool rolledBack, Exception innerException)
    : Exception(operationCode, innerException)
{
    public string OperationCode { get; } = operationCode;

    public bool RolledBack { get; } = rolledBack;
}

public sealed class InstallApplier(
    VendorExecutableValidator vendorValidator,
    ISecretProtector protector,
    IScheduledTaskManager tasks,
    Func<DateTimeOffset> utcNow,
    Func<string> currentSid,
    Func<string> currentCodexHome,
    Func<IReadOnlyList<string>> runningDesktopProcesses,
    Action<string> signalWorker,
    Func<InstallationLayout, string, TimeSpan, bool> waitForWorkers,
    Action ensureSupportedHost,
    Action<string> phaseFault)
{
    private static readonly IReadOnlyList<string> InstallPhases =
    [
        "PLAN_VALIDATED", "ROOT_READY", "STAGED", "BACKUP_READY", "CONFIG_PENDING",
        "CONFIG_COMMITTED", "TASKS_ENABLED", "COMMITTED",
    ];

    public static InstallApplier CreateProduction() => new(
        VendorExecutableValidator.ForCurrentUser(),
        new DpapiSecretProtector(),
        new WindowsScheduledTaskManager(),
        () => DateTimeOffset.UtcNow,
        () => CurrentUserContext.Sid,
        CurrentUserContext.ResolveCodexHome,
        DesktopProcessGuard.FindRunning,
        WorkerCoordination.SignalExistingOrCreate,
        BridgeProcessGuard.RequestStopAndWait,
        CurrentUserContext.EnsureSupportedHost,
        _ => { });

    public InstallApplyResult Apply(string planPath)
    {
        ensureSupportedHost();
        using var mutationLock = new InstallationMutationLock(currentSid());
        if (!mutationLock.TryAcquire())
        {
            throw new InvalidOperationException("INSTALL_MUTATION_BUSY");
        }

        var planBytes = File.ReadAllBytes(planPath);
        var plan = JsonSerializer.Deserialize<InstallPlan>(planBytes, JsonDefaults.Options)
                   ?? throw new InvalidDataException("Install plan is empty.");
        plan.Validate();
        var layout = new InstallationLayout(plan.InstallationRoot);
        if (File.Exists(layout.ActiveJournalPath))
        {
            RecoverUnfinished(layout);
        }

        ValidatePlanForApply(plan);
        var planHash = Hashing.Sha256Hex(planBytes);
        var transactionId = Guid.NewGuid().ToString("D");
        var backupPath = Path.Combine(layout.BackupsDirectory, plan.BackupFileName);
        var journal = new TransactionJournal(
            BridgeConstants.SchemaVersion,
            transactionId,
            JournalKind.Install,
            "PLAN_VALIDATED",
            planHash,
            plan.ConfigSha256,
            null,
            layout.Root,
            backupPath,
            utcNow());
        var journalStore = new TransactionJournalStore(layout.ActiveJournalPath, plan.UserSid);
        var configTransaction = new ConfigFileTransaction(protector);
        var rootExisted = Directory.Exists(layout.Root);
        byte[]? previousRuntime = File.Exists(layout.RuntimeConfigPath) ? File.ReadAllBytes(layout.RuntimeConfigPath) : null;
        byte[]? previousUpstream = File.Exists(layout.UpstreamPath) ? File.ReadAllBytes(layout.UpstreamPath) : null;
        byte[]? previousInstallationRecord = File.Exists(layout.TransactionRecordPath) ? File.ReadAllBytes(layout.TransactionRecordPath) : null;
        RuntimeConfig? previousRuntimeConfig = null;
        ConfigEditResult? configEdit = null;
        PackageRollback? packageRollback = null;
        var tasksStaged = false;
        var configCommitted = false;
        var rollbackSucceeded = false;

        void Advance(string phase, string? afterHash = null)
        {
            if (!InstallPhases.Contains(phase, StringComparer.Ordinal))
            {
                throw new ArgumentOutOfRangeException(nameof(phase));
            }

            journal = journal with
            {
                Phase = phase,
                ConfigAfterSha256 = afterHash ?? journal.ConfigAfterSha256,
                UpdatedAtUtc = utcNow(),
            };
            journalStore.Save(journal);
            phaseFault(phase);
        }

        try
        {
            Advance("PLAN_VALIDATED");

            if (!rootExisted)
            {
                WindowsAclManager.CreateProtectedRoot(layout.Root, plan.UserSid);
            }
            else
            {
                var acl = WindowsAclManager.VerifyRoot(layout.Root, plan.UserSid);
                if (!acl.IsValid)
                {
                    throw new UnauthorizedAccessException(acl.OperationCode);
                }
            }

            layout.EnsureMutableDirectories();
            Advance("ROOT_READY");

            if (plan.NotifyClassification == NotifyClassification.HealthyBridge)
            {
                previousRuntimeConfig = new RuntimeConfigStore(layout.RuntimeConfigPath).Load();
                new RuntimeConfigStore(layout.RuntimeConfigPath).Save(new RuntimeConfig
                {
                    MachineId = previousRuntimeConfig.MachineId,
                    CodexHome = previousRuntimeConfig.CodexHome,
                    PcAlias = previousRuntimeConfig.PcAlias,
                    CaptureMode = previousRuntimeConfig.CaptureMode,
                    DeliveryPaused = true,
                    AutoRepairVendorNotify = previousRuntimeConfig.AutoRepairVendorNotify,
                });
                tasks.DisableAll();
                tasksStaged = true;
                signalWorker(previousRuntimeConfig.MachineId);
                if (!waitForWorkers(layout, previousRuntimeConfig.MachineId, TimeSpan.FromSeconds(25)))
                {
                    throw new InvalidOperationException("WORKER_STILL_RUNNING");
                }
            }

            tasks.StageDisabled(layout, plan.UserSid, utcNow());
            tasksStaged = true;
            var package = PackageManifest.LoadAndVerify(plan.PackageRoot);
            packageRollback = new PackageStager().StageAndCommit(
                package,
                layout,
                transactionId,
                previousRuntime,
                previousUpstream,
                previousInstallationRecord);
            var queue = new QueueStore(layout.DatabasePath);
            queue.Initialize();
            if (!string.Equals(queue.QuickCheck(), "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("LOCAL_STATE_QUICK_CHECK_FAILED");
            }

            void EnsureInstallationAcl()
            {
                try
                {
                    NormalizeAndVerifyInstallationTree(layout, plan.UserSid);
                }
                catch (UnauthorizedAccessException)
                {
                    queue.UpsertHealth(HealthCodes.InstallAclBlocked, utcNow());
                    throw;
                }
            }

            Advance("STAGED");

            var configBytes = File.Exists(plan.ConfigPath) ? File.ReadAllBytes(plan.ConfigPath) : [];
            if (!string.Equals(Hashing.Sha256Hex(configBytes), plan.ConfigSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("CONFIG_HASH_CHANGED");
            }

            var document = CodexConfigDocument.Parse(configBytes);
            var upstream = CaptureUpstream(plan, document.NotifyArgv);
            RuntimeConfig runtime;
            if (plan.NotifyClassification == NotifyClassification.HealthyBridge)
            {
                var previous = previousRuntimeConfig ?? throw new InvalidDataException("Existing runtime config was not loaded.");
                runtime = new RuntimeConfig
                {
                    MachineId = previous.MachineId,
                    CodexHome = previous.CodexHome,
                    PcAlias = plan.PcAlias ?? previous.PcAlias,
                    CaptureMode = previous.CaptureMode,
                    DeliveryPaused = true,
                    AutoRepairVendorNotify = previous.AutoRepairVendorNotify,
                };
            }
            else
            {
                var retainedPause = false;
                if (previousRuntime is not null)
                {
                    try
                    {
                        retainedPause = JsonSerializer.Deserialize<RuntimeConfig>(previousRuntime, JsonDefaults.Options)?.DeliveryPaused == true;
                    }
                    catch (JsonException)
                    {
                    }
                }

                var retainedCounts = queue.GetCounts();
                var retainedBacklog = rootExisted && retainedCounts.Pending + retainedCounts.Inflight > 0;
                runtime = new RuntimeConfig
                {
                    MachineId = plan.MachineId,
                    CodexHome = plan.CodexHome,
                    PcAlias = plan.PcAlias,
                    CaptureMode = CaptureMode.Shadow,
                    DeliveryPaused = retainedPause || retainedBacklog,
                    AutoRepairVendorNotify = true,
                };
            }

            new RuntimeConfigStore(layout.RuntimeConfigPath).Save(runtime);
            new ProtectedJsonStore<UpstreamRecord>(layout.UpstreamPath, protector).Save(upstream);
            configTransaction.CreateBackup(plan.ConfigPath, plan.ConfigSha256, backupPath, utcNow());
            Advance("BACKUP_READY");

            EnsureDesktopClosed();
            EnsureConfigHash(plan.ConfigPath, plan.ConfigSha256);
            Advance("CONFIG_PENDING");
            configEdit = plan.NotifyClassification == NotifyClassification.HealthyBridge
                ? configTransaction.ReplaceNotifyValue(
                    plan.ConfigPath,
                    plan.ConfigSha256,
                    document.NotifyArgv ?? throw new InvalidDataException("Active bridge notify is missing."))
                : configTransaction.ReplaceNotify(plan.ConfigPath, plan.ConfigSha256, [plan.BridgeExecutablePath, "hook"]);
            configCommitted = true;
            EnsureDesktopClosed();
            Advance("CONFIG_COMMITTED", configEdit.AfterSha256);

            EnsureInstallationAcl();

            tasks.EnableAll();
            var statuses = tasks.GetStatuses();
            if (statuses.Count != 2 || statuses.Any(status => !status.Exists || !status.Enabled))
            {
                throw new InvalidOperationException("SCHEDULED_TASK_VERIFY_FAILED");
            }

            Advance("TASKS_ENABLED");

            if (previousRuntimeConfig is not null)
            {
                new RuntimeConfigStore(layout.RuntimeConfigPath).Save(new RuntimeConfig
                {
                    MachineId = previousRuntimeConfig.MachineId,
                    CodexHome = previousRuntimeConfig.CodexHome,
                    PcAlias = plan.PcAlias ?? previousRuntimeConfig.PcAlias,
                    CaptureMode = previousRuntimeConfig.CaptureMode,
                    DeliveryPaused = previousRuntimeConfig.DeliveryPaused,
                    AutoRepairVendorNotify = previousRuntimeConfig.AutoRepairVendorNotify,
                });
            }

            var installationRecord = new InstallationRecord(
                BridgeConstants.SchemaVersion,
                runtime.MachineId,
                plan.UserSid,
                package.ManifestSha256,
                plan.ConfigPath,
                utcNow(),
                InstallationState.Active);
            installationRecord.Validate();
            AtomicFile.WriteUtf8(layout.TransactionRecordPath, JsonSerializer.Serialize(installationRecord, JsonDefaults.Options));
            Advance("COMMITTED");
            EnsureInstallationAcl();
            packageRollback.Cleanup();
            File.Delete(layout.ActiveJournalPath);
            return new InstallApplyResult(runtime.MachineId, layout.Root, configEdit.AfterSha256, previousRuntimeConfig is not null);
        }
        catch (Exception exception)
        {
            rollbackSucceeded = TryRollback(
                plan,
                layout,
                backupPath,
                configEdit?.AfterSha256,
                configCommitted,
                tasksStaged,
                previousRuntime,
                previousUpstream,
                previousInstallationRecord,
                packageRollback ?? PackageRollback.TryLoad(layout, transactionId),
                rootExisted,
                plan.NotifyClassification == NotifyClassification.HealthyBridge);
            throw new InstallApplyException(OperationCode(exception), rollbackSucceeded, exception);
        }
        finally
        {
            if (previousRuntime is not null)
            {
                CryptographicOperations.ZeroMemory(previousRuntime);
            }

            if (previousUpstream is not null)
            {
                CryptographicOperations.ZeroMemory(previousUpstream);
            }

            if (previousInstallationRecord is not null)
            {
                CryptographicOperations.ZeroMemory(previousInstallationRecord);
            }
        }
    }

    public void RecoverUnfinished(InstallationLayout layout)
    {
        if (!File.Exists(layout.ActiveJournalPath))
        {
            return;
        }

        EnsureDesktopClosed();
        var journal = new TransactionJournalStore(layout.ActiveJournalPath, currentSid()).Load();
        if (journal.Kind != JournalKind.Install || !string.Equals(journal.InstallationRoot, layout.Root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("UNSUPPORTED_JOURNAL_RECOVERY");
        }

        if (string.Equals(journal.Phase, "COMMITTED", StringComparison.Ordinal))
        {
            var record = JsonSerializer.Deserialize<InstallationRecord>(AtomicFile.ReadUtf8(layout.TransactionRecordPath), JsonDefaults.Options)
                         ?? throw new InvalidDataException("Installation record is empty.");
            record.Validate();
            if (record.State != InstallationState.Active)
            {
                throw new InvalidDataException("COMMITTED_JOURNAL_RECORD_INVALID");
            }

            NormalizeAndVerifyInstallationTree(layout, currentSid());
            PackageRollback.TryLoad(layout, journal.TransactionId)?.Cleanup();
            BridgeProcessGuard.ClearStopRequest(layout);
            File.Delete(layout.ActiveJournalPath);
            return;
        }

        tasks.DisableAll();
        tasks.RemoveAll();
        var current = File.Exists(journal.BackupPath)
            ? new ProtectedJsonStore<ConfigBackupRecord>(journal.BackupPath, protector).Load()
            : null;
        if (current is not null)
        {
            current.Validate();
            var configBytes = File.Exists(current.ConfigPath) ? File.ReadAllBytes(current.ConfigPath) : [];
            var currentHash = Hashing.Sha256Hex(configBytes);
            if (string.Equals(currentHash, journal.ConfigBeforeSha256, StringComparison.Ordinal))
            {
                // The config mutation had not committed.
            }
            else if (journal.ConfigAfterSha256 is not null && string.Equals(currentHash, journal.ConfigAfterSha256, StringComparison.Ordinal))
            {
                new ConfigFileTransaction(protector).RestoreBackup(journal.BackupPath, currentHash);
            }
            else
            {
                var notify = CodexConfigDocument.Parse(configBytes).NotifyArgv;
                var expectedBridge = Path.Combine(layout.Bin, "CodexTelegramBridge.exe");
                if (IsActiveBridgeNotify(notify, expectedBridge, currentHash))
                {
                    new ConfigFileTransaction(protector).RestoreBackup(journal.BackupPath, currentHash);
                }
                else
                {
                    throw new InvalidOperationException("JOURNAL_CONFIG_CONFLICT");
                }
            }
        }

        PauseDeliveryIfPresent(layout);
        var packageRollback = PackageRollback.TryLoad(layout, journal.TransactionId);
        packageRollback?.Restore();
        var previousInstallationWasActive = packageRollback?.RestoreInstallationSnapshots() == true;
        if (!previousInstallationWasActive)
        {
            previousInstallationWasActive = IsStillHealthyActiveInstallation(layout);
        }

        packageRollback?.Cleanup();
        if (!previousInstallationWasActive && File.Exists(layout.TransactionRecordPath))
        {
            try
            {
                var record = JsonSerializer.Deserialize<InstallationRecord>(AtomicFile.ReadUtf8(layout.TransactionRecordPath), JsonDefaults.Options);
                if (record is not null)
                {
                    AtomicFile.WriteUtf8(layout.TransactionRecordPath, JsonSerializer.Serialize(record with { State = InstallationState.RolledBack }, JsonDefaults.Options));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            {
            }
        }
        if (Directory.Exists(layout.Root))
        {
            NormalizeAndVerifyInstallationTree(layout, currentSid());
        }
        if (previousInstallationWasActive)
        {
            tasks.StageDisabled(layout, currentSid(), utcNow());
            tasks.EnableAll();
        }
        BridgeProcessGuard.ClearStopRequest(layout);
        File.Delete(layout.ActiveJournalPath);
    }

    private bool IsStillHealthyActiveInstallation(InstallationLayout layout)
    {
        try
        {
            var record = JsonSerializer.Deserialize<InstallationRecord>(AtomicFile.ReadUtf8(layout.TransactionRecordPath), JsonDefaults.Options)
                         ?? throw new InvalidDataException("Installation record is empty.");
            record.Validate();
            var runtime = new RuntimeConfigStore(layout.RuntimeConfigPath).Load();
            var configBytes = File.ReadAllBytes(Path.Combine(runtime.CodexHome, "config.toml"));
            var notify = CodexConfigDocument.Parse(configBytes).NotifyArgv;
            var manifest = PackageManifest.LoadAndVerify(layout.Root, allowInstalledMutableFiles: true);
            return record.State == InstallationState.Active &&
                   IsActiveBridgeNotify(notify, Path.Combine(layout.Bin, "CodexTelegramBridge.exe"), Hashing.Sha256Hex(configBytes)) &&
                   string.Equals(record.ManifestSha256, manifest.ManifestSha256, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            return false;
        }
    }

    private void ValidatePlanForApply(InstallPlan plan)
    {
        var now = utcNow();
        if (now < plan.CreatedAtUtc - TimeSpan.FromMinutes(1) || now > plan.ExpiresAtUtc)
        {
            throw new InvalidOperationException("PLAN_EXPIRED");
        }

        if (!string.Equals(plan.UserSid, currentSid(), StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("PLAN_USER_CHANGED");
        }

        if (!string.Equals(Path.GetFullPath(plan.CodexHome), Path.GetFullPath(currentCodexHome()), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("CODEX_HOME_CONFLICT");
        }

        if (plan.NotifyClassification == NotifyClassification.Conflict)
        {
            throw new InvalidOperationException("CONFIG_CONFLICT");
        }

        if (File.Exists(plan.ConfigPath) != plan.ConfigExisted)
        {
            throw new InvalidOperationException("CONFIG_EXISTENCE_CHANGED");
        }

        var expectedLayout = new InstallationLayout(plan.InstallationRoot);
        if (!string.Equals(plan.BridgeExecutablePath, Path.Combine(expectedLayout.Bin, "CodexTelegramBridge.exe"), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plan.ControlExecutablePath, Path.Combine(expectedLayout.Bin, "CodexTelegramCtl.exe"), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("PLAN_TARGET_CHANGED");
        }


        EnsureOptionalFileHash(expectedLayout.TransactionRecordPath, plan.ExistingInstallationRecordSha256, "INSTALL_IDENTITY_CHANGED");
        EnsureOptionalFileHash(expectedLayout.RuntimeConfigPath, plan.ExistingRuntimeConfigSha256, "RUNTIME_CONFIG_CHANGED");
        EnsureOptionalFileHash(expectedLayout.UpstreamPath, plan.ExistingUpstreamSha256, "UPSTREAM_STATE_CHANGED");

        var package = PackageManifest.LoadAndVerify(plan.PackageRoot);
        if (!string.Equals(package.ManifestSha256, plan.ManifestSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("PACKAGE_CHANGED");
        }

        EnsureConfigHash(plan.ConfigPath, plan.ConfigSha256);
        EnsureDesktopClosed();
    }

    private UpstreamRecord CaptureUpstream(InstallPlan plan, IReadOnlyList<string>? currentNotify)
    {
        if (plan.NotifyClassification == NotifyClassification.Absent)
        {
            var absent = new UpstreamRecord(
                BridgeConstants.SchemaVersion,
                [],
                null,
                null,
                utcNow(),
                plan.ConfigSha256,
                UpstreamKind.Absent);
            absent.ValidateShape();
            return absent;
        }

        if (plan.NotifyClassification == NotifyClassification.HealthyBridge)
        {
            var match = BridgeNotifyCommand.Match(currentNotify, plan.BridgeExecutablePath);
            if (match.Shape == BridgeNotifyShape.VendorWrapped)
            {
                var currentVendor = vendorValidator.ValidateArgv(match.OuterVendorArgv ?? [], plan.ConfigSha256, utcNow());
                if (!currentVendor.IsValid || currentVendor.Record is null)
                {
                    throw new InvalidOperationException("CONFIG_CONFLICT");
                }

                return currentVendor.Record;
            }

            var retained = new ProtectedJsonStore<UpstreamRecord>(new InstallationLayout(plan.InstallationRoot).UpstreamPath, protector).Load();
            retained.ValidateShape();
            return retained;
        }

        var validation = vendorValidator.ValidateArgv(currentNotify ?? [], plan.ConfigSha256, utcNow());
        if (!validation.IsValid || validation.Record is null)
        {
            throw new InvalidOperationException("CONFIG_CONFLICT");
        }

        return validation.Record;
    }

    private bool IsActiveBridgeNotify(IReadOnlyList<string>? notify, string expectedBridge, string configHash)
    {
        var match = BridgeNotifyCommand.Match(notify, expectedBridge);
        return match.Shape == BridgeNotifyShape.Direct ||
               match.Shape == BridgeNotifyShape.VendorWrapped &&
               vendorValidator.ValidateArgv(match.OuterVendorArgv ?? [], configHash, utcNow()).IsValid;
    }

    private bool TryRollback(
        InstallPlan plan,
        InstallationLayout layout,
        string backupPath,
        string? afterHash,
        bool configCommitted,
        bool tasksStaged,
        byte[]? previousRuntime,
        byte[]? previousUpstream,
        byte[]? previousInstallationRecord,
        PackageRollback? packageRollback,
        bool rootExisted,
        bool wasUpgrade)
    {
        try
        {
            if (tasksStaged)
            {
                tasks.DisableAll();
                if (!wasUpgrade)
                {
                    tasks.RemoveAll();
                }
            }

            PauseDeliveryIfPresent(layout);
            if (configCommitted && afterHash is not null && File.Exists(backupPath))
            {
                new ConfigFileTransaction(protector).RestoreBackup(backupPath, afterHash);
            }

            RestoreOptionalFile(layout.RuntimeConfigPath, previousRuntime);
            RestoreOptionalFile(layout.UpstreamPath, previousUpstream);
            RestoreOptionalFile(layout.TransactionRecordPath, previousInstallationRecord);
            packageRollback?.Restore();
            packageRollback?.Cleanup();
            EnsureConfigHash(plan.ConfigPath, plan.ConfigSha256);
            if (!rootExisted)
            {
                var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(layout.Root));
                if (Directory.Exists(full) && full.Length > (Path.GetPathRoot(full)?.Length ?? 0) + 8 &&
                    (File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0)
                {
                    Directory.Delete(full, recursive: true);
                }
            }
            else
            {
                NormalizeAndVerifyInstallationTree(layout, plan.UserSid);
                if (tasksStaged && wasUpgrade)
                {
                    tasks.EnableAll();
                }
            }

            if (File.Exists(layout.ActiveJournalPath))
            {
                File.Delete(layout.ActiveJournalPath);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RestoreOptionalFile(string path, byte[]? content)
    {
        if (content is null)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        else
        {
            AtomicFile.WriteBytes(path, content);
        }
    }

    private static void NormalizeAndVerifyInstallationTree(InstallationLayout layout, string userSid)
    {
        WindowsAclManager.NormalizeTreeOwnership(layout.Root, userSid);
        var acl = WindowsAclManager.VerifyTree(layout.Root, userSid);
        if (!acl.IsValid)
        {
            throw new UnauthorizedAccessException(acl.OperationCode);
        }
    }

    private static void PauseDeliveryIfPresent(InstallationLayout layout)
    {
        try
        {
            var store = new RuntimeConfigStore(layout.RuntimeConfigPath);
            var config = store.Load();
            store.Save(new RuntimeConfig
            {
                MachineId = config.MachineId,
                PcAlias = config.PcAlias,
                CodexHome = config.CodexHome,
                CaptureMode = config.CaptureMode,
                DeliveryPaused = true,
                AutoRepairVendorNotify = config.AutoRepairVendorNotify,
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
        }
    }

    private void EnsureDesktopClosed()
    {
        if (runningDesktopProcesses().Count > 0)
        {
            throw new InvalidOperationException("DESKTOP_PROCESSES_RUNNING");
        }
    }

    private static void EnsureConfigHash(string path, string expected)
    {
        var bytes = File.Exists(path) ? File.ReadAllBytes(path) : [];
        if (!string.Equals(Hashing.Sha256Hex(bytes), expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("CONFIG_HASH_CHANGED");
        }
    }

    private static void EnsureOptionalFileHash(string path, string? expected, string operationCode)
    {
        if (File.Exists(path) != (expected is not null) ||
            expected is not null && !string.Equals(Hashing.Sha256File(path), expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(operationCode);
        }
    }

    private static string OperationCode(Exception exception) => exception switch
    {
        InvalidOperationException when IsSafeCode(exception.Message) => exception.Message,
        UnauthorizedAccessException when IsSafeCode(exception.Message) => exception.Message,
        InvalidDataException when IsSafeCode(exception.Message) => exception.Message,
        _ => "INSTALL_APPLY_FAILED",
    };

    private static bool IsSafeCode(string value) =>
        value.Length is > 0 and <= 80 && value.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');
}
