using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace CodexTelegramCommon;

public sealed record CleanupRequest(
    int SchemaVersion,
    string InstallationRoot,
    bool PurgeState,
    IReadOnlyList<string> ManifestEntries)
{
    public void Validate()
    {
        if (SchemaVersion != BridgeConstants.SchemaVersion ||
            !Path.IsPathFullyQualified(InstallationRoot) ||
            ManifestEntries.Any(path => string.IsNullOrWhiteSpace(path) || Path.IsPathFullyQualified(path) ||
                                        path.Replace('\\', '/').Split('/').Any(component => component is "" or "." or "..")))
        {
            throw new InvalidDataException("Cleanup request is invalid.");
        }
    }
}

public enum UninstallOutcome
{
    CleanupRequired,
    Conflict,
}

public sealed record UninstallResult(UninstallOutcome Outcome, string OperationCode, string? CleanupRequestPath);

public sealed class UninstallService(
    ISecretProtector protector,
    VendorExecutableValidator vendorValidator,
    IScheduledTaskManager tasks,
    Func<DateTimeOffset> utcNow,
    Func<string> currentSid,
    Func<IReadOnlyList<string>> runningDesktopProcesses,
    Action<string>? signalWorker = null,
    Func<InstallationLayout, string, TimeSpan, bool>? waitForWorkers = null)
{
    public static UninstallService CreateProduction() => new(
        new DpapiSecretProtector(),
        VendorExecutableValidator.ForCurrentUser(),
        new WindowsScheduledTaskManager(),
        () => DateTimeOffset.UtcNow,
        () => CurrentUserContext.Sid,
        DesktopProcessGuard.FindRunning,
        WorkerCoordination.SignalExistingOrCreate,
        BridgeProcessGuard.RequestStopAndWait);

    public UninstallResult Run(InstallationLayout layout, bool keepConfigConflict, bool purgeState)
    {
        using var mutationLock = new InstallationMutationLock(currentSid());
        if (!mutationLock.TryAcquire())
        {
            throw new InvalidOperationException("INSTALL_MUTATION_BUSY");
        }

        EnsureDesktopClosed();
        var acl = WindowsAclManager.VerifyTree(layout.Root, currentSid());
        if (!acl.IsValid)
        {
            throw new UnauthorizedAccessException(HealthCodes.InstallAclBlocked);
        }

        var runtimeStore = new RuntimeConfigStore(layout.RuntimeConfigPath);
        if (File.Exists(layout.ActiveJournalPath))
        {
            return RecoverUnfinished(layout);
        }

        var runtime = runtimeStore.Load();
        var configPath = Path.Combine(runtime.CodexHome, "config.toml");
        var configBytes = File.ReadAllBytes(configPath);
        var configHash = Hashing.Sha256Hex(configBytes);
        var document = CodexConfigDocument.Parse(configBytes);
        var expectedBridge = Path.Combine(layout.Bin, "CodexTelegramBridge.exe");
        var match = BridgeNotifyCommand.Match(document.NotifyArgv, expectedBridge);
        var wrappedVendorValidation = match.Shape == BridgeNotifyShape.VendorWrapped
            ? vendorValidator.ValidateArgv(match.OuterVendorArgv ?? [], configHash, utcNow())
            : null;
        var pointsToBridge = match.Shape == BridgeNotifyShape.Direct || wrappedVendorValidation?.IsValid == true;
        if (!pointsToBridge)
        {
            var referencesAnyBridge = BridgeNotifyCommand.ReferencesBridge(document.NotifyArgv, expectedBridge);
            if (!keepConfigConflict || referencesAnyBridge)
            {
                return new UninstallResult(UninstallOutcome.Conflict, HealthCodes.ConfigConflict, null);
            }
        }

        var package = PackageManifest.LoadAndVerify(layout.Root, allowInstalledMutableFiles: true);
        var runtimeBytes = File.ReadAllBytes(layout.RuntimeConfigPath);
        ConfigEditResult? edit = null;
        var backupPath = Path.Combine(layout.BackupsDirectory, $"uninstall-{utcNow():yyyyMMddTHHmmssZ}-{configHash[..12]}.dpapi");
        var journal = new TransactionJournal(
            BridgeConstants.SchemaVersion,
            Guid.NewGuid().ToString("D"),
            JournalKind.Uninstall,
            "UNINSTALL_PLANNED",
            new string('0', 64),
            configHash,
            null,
            layout.Root,
            backupPath,
            utcNow());
        var journalStore = new TransactionJournalStore(layout.ActiveJournalPath, currentSid());
        try
        {
            tasks.DisableAll();
            runtimeStore.Save(CopyRuntime(runtime, deliveryPaused: true));
            (signalWorker ?? WorkerCoordination.SignalExistingOrCreate)(runtime.MachineId);
            if (!(waitForWorkers ?? BridgeProcessGuard.RequestStopAndWait)(layout, runtime.MachineId, TimeSpan.FromSeconds(25)))
            {
                throw new InvalidOperationException("WORKER_STILL_RUNNING");
            }

            journalStore.Save(journal);

            if (pointsToBridge)
            {
                IReadOnlyList<string>? desired;
                if (match.Shape == BridgeNotifyShape.VendorWrapped)
                {
                    if (wrappedVendorValidation?.Record is null)
                    {
                        throw new InvalidOperationException(HealthCodes.UpstreamBlocked);
                    }

                    desired = wrappedVendorValidation.Record.Argv;
                }
                else
                {
                    var upstream = new ProtectedJsonStore<UpstreamRecord>(layout.UpstreamPath, protector).Load();
                    var validation = vendorValidator.ValidateCaptured(upstream);
                    if (!validation.IsValid)
                    {
                        throw new InvalidOperationException(HealthCodes.UpstreamBlocked);
                    }

                    desired = upstream.Kind == UpstreamKind.Absent ? null : upstream.Argv;
                }

                var transaction = new ConfigFileTransaction(protector);
                transaction.CreateBackup(configPath, configHash, backupPath, utcNow());
                EnsureDesktopClosed();
                journal = journal with { Phase = "CONFIG_PENDING", UpdatedAtUtc = utcNow() };
                journalStore.Save(journal);
                edit = transaction.ReplaceNotifyValue(configPath, configHash, desired);
                EnsureDesktopClosed();
                journal = journal with { Phase = "CONFIG_RESTORED", ConfigAfterSha256 = edit.AfterSha256, UpdatedAtUtc = utcNow() };
                journalStore.Save(journal);
            }

            tasks.RemoveAll();
            journal = journal with { Phase = "TASKS_REMOVED", UpdatedAtUtc = utcNow() };
            journalStore.Save(journal);

            var record = JsonSerializer.Deserialize<InstallationRecord>(AtomicFile.ReadUtf8(layout.TransactionRecordPath), JsonDefaults.Options)
                         ?? throw new InvalidDataException("Installation record is empty.");
            record.Validate();
            var rolledBack = record with { State = InstallationState.RolledBack };
            AtomicFile.WriteUtf8(layout.TransactionRecordPath, JsonSerializer.Serialize(rolledBack, JsonDefaults.Options));
            var cleanup = new CleanupRequest(
                BridgeConstants.SchemaVersion,
                layout.Root,
                purgeState,
                package.Entries.Select(entry => entry.RelativePath).ToArray());
            cleanup.Validate();
            var cleanupPath = Path.Combine(layout.StateDirectory, "uninstall-cleanup.dpapi");
            new ProtectedJsonStore<CleanupRequest>(cleanupPath, protector).Save(cleanup);
            journal = journal with { Phase = "COMMITTED", UpdatedAtUtc = utcNow() };
            journalStore.Save(journal);
            File.Delete(layout.ActiveJournalPath);
            return new UninstallResult(UninstallOutcome.CleanupRequired, "UNINSTALL_CONFIG_RESTORED", cleanupPath);
        }
        catch
        {
            try
            {
                if (edit is not null)
                {
                    new ConfigFileTransaction(protector).RestoreBackup(backupPath, edit.AfterSha256);
                }

                AtomicFile.WriteBytes(layout.RuntimeConfigPath, runtimeBytes);
                tasks.EnableAll();
                if (File.Exists(layout.ActiveJournalPath))
                {
                    File.Delete(layout.ActiveJournalPath);
                }
            }
            catch
            {
            }

            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(runtimeBytes);
        }
    }

    private UninstallResult RecoverUnfinished(InstallationLayout layout)
    {
        var journal = new TransactionJournalStore(layout.ActiveJournalPath, currentSid()).Load();
        if (journal.Kind != JournalKind.Uninstall)
        {
            throw new InvalidOperationException("JOURNAL_RECOVERY_REQUIRED");
        }

        var cleanupPath = Path.Combine(layout.StateDirectory, "uninstall-cleanup.dpapi");
        if (string.Equals(journal.Phase, "COMMITTED", StringComparison.Ordinal) && File.Exists(cleanupPath))
        {
            File.Delete(layout.ActiveJournalPath);
            return new UninstallResult(UninstallOutcome.CleanupRequired, "UNINSTALL_COMMIT_RECOVERED", cleanupPath);
        }

        if (File.Exists(journal.BackupPath))
        {
            var backup = new ProtectedJsonStore<ConfigBackupRecord>(journal.BackupPath, protector).Load();
            backup.Validate();
            var currentBytes = File.Exists(backup.ConfigPath) ? File.ReadAllBytes(backup.ConfigPath) : [];
            var currentHash = Hashing.Sha256Hex(currentBytes);
            if (!string.Equals(currentHash, journal.ConfigBeforeSha256, StringComparison.Ordinal))
            {
                var safeAfter = journal.ConfigAfterSha256 is not null && string.Equals(currentHash, journal.ConfigAfterSha256, StringComparison.Ordinal);
                if (!safeAfter)
                {
                    var upstream = new ProtectedJsonStore<UpstreamRecord>(layout.UpstreamPath, protector).Load();
                    upstream.ValidateShape();
                    var currentNotify = CodexConfigDocument.Parse(currentBytes).NotifyArgv;
                    var expectedNotify = upstream.Kind == UpstreamKind.Absent ? null : upstream.Argv;
                    safeAfter = SequenceEqual(currentNotify, expectedNotify);
                }

                if (!safeAfter)
                {
                    throw new InvalidOperationException("JOURNAL_CONFIG_CONFLICT");
                }

                new ConfigFileTransaction(protector).RestoreBackup(journal.BackupPath, currentHash);
            }
        }

        tasks.StageDisabled(layout, currentSid(), utcNow());
        tasks.EnableAll();
        File.Delete(layout.ActiveJournalPath);
        return new UninstallResult(UninstallOutcome.Conflict, "UNINSTALL_ROLLBACK_RECOVERED", null);
    }

    private void EnsureDesktopClosed()
    {
        if (runningDesktopProcesses().Count > 0)
        {
            throw new InvalidOperationException("DESKTOP_PROCESSES_RUNNING");
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

    private static bool SequenceEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right) =>
        left is null ? right is null : right is not null && left.SequenceEqual(right, StringComparer.Ordinal);
}

public static class CleanupExecutor
{
    private const uint MoveFileDelayUntilReboot = 0x00000004;

    public static int Execute(string requestPath, int parentProcessId, string? cleanupExecutablePath = null)
    {
        try
        {
            WaitForParent(parentProcessId);
            var request = new ProtectedJsonStore<CleanupRequest>(requestPath, new DpapiSecretProtector()).Load();
            request.Validate();
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.InstallationRoot));
            if (!Directory.Exists(root) || root.Length <= (Path.GetPathRoot(root)?.Length ?? 0) + 8 ||
                (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                return 3;
            }

            if (request.PurgeState)
            {
                Directory.Delete(root, recursive: true);
            }
            else
            {
                foreach (var relative in request.ManifestEntries)
                {
                    var path = ResolveContained(root, relative);
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }

                var manifest = Path.Combine(root, "manifest.sha256");
                if (File.Exists(manifest))
                {
                    File.Delete(manifest);
                }

                if (File.Exists(requestPath))
                {
                    File.Delete(requestPath);
                }

                RemoveEmptyDirectories(root);
            }

            if (!string.IsNullOrWhiteSpace(cleanupExecutablePath))
            {
                MoveFileEx(cleanupExecutablePath, null, MoveFileDelayUntilReboot);
            }

            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or CryptographicException or JsonException or ArgumentException)
        {
            return 3;
        }
    }

    private static void WaitForParent(int processId)
    {
        try
        {
            using var parent = Process.GetProcessById(processId);
            parent.WaitForExit(30_000);
        }
        catch (ArgumentException)
        {
        }
    }

    private static string ResolveContained(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Cleanup path escapes installation root.");
        }

        return path;
    }

    private static void RemoveEmptyDirectories(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, uint flags);
}
