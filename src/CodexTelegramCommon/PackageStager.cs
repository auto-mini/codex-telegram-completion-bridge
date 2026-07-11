namespace CodexTelegramCommon;

public sealed record PackageRollbackEntry(string RelativePath, bool Existed);

public sealed record PackageRollbackRecord(int SchemaVersion, string TransactionId, IReadOnlyList<PackageRollbackEntry> Entries, bool ManifestExisted);

public sealed record InstallationSnapshotRecord(int SchemaVersion, bool RuntimeExisted, bool UpstreamExisted, bool InstallationRecordExisted);

public sealed class PackageRollback(InstallationLayout layout, string transactionId, IReadOnlyList<PackageRollbackEntry> entries, bool manifestExisted)
{
    public string Root { get; } = Path.Combine(layout.BackupsDirectory, $"package-rollback-{transactionId}");
    public IReadOnlyList<PackageRollbackEntry> Entries => entries;

    public static PackageRollback? TryLoad(InstallationLayout layout, string transactionId)
    {
        var root = Path.Combine(layout.BackupsDirectory, $"package-rollback-{transactionId}");
        var recordPath = Path.Combine(root, "rollback.json");
        if (!File.Exists(recordPath))
        {
            return null;
        }

        var record = System.Text.Json.JsonSerializer.Deserialize<PackageRollbackRecord>(AtomicFile.ReadUtf8(recordPath), JsonDefaults.Options)
                     ?? throw new InvalidDataException("Package rollback record is empty.");
        if (record.SchemaVersion != BridgeConstants.SchemaVersion ||
            !string.Equals(record.TransactionId, transactionId, StringComparison.Ordinal) ||
            record.Entries.Any(entry => string.IsNullOrWhiteSpace(entry.RelativePath)))
        {
            throw new InvalidDataException("Package rollback record is invalid.");
        }

        return new PackageRollback(layout, transactionId, record.Entries, record.ManifestExisted);
    }

    public void Restore()
    {
        foreach (var entry in entries)
        {
            var destination = ResolveContained(layout.Root, entry.RelativePath);
            if (entry.Existed)
            {
                var backup = ResolveContained(Root, entry.RelativePath);
                if (!File.Exists(backup))
                {
                    throw new InvalidDataException("Package rollback file is missing.");
                }

                PackageStager.CopyDurable(backup, destination, replace: File.Exists(destination));
            }
            else if (File.Exists(destination))
            {
                File.Delete(destination);
            }
        }

        var manifestBackup = Path.Combine(Root, "manifest.sha256.previous");
        if (manifestExisted)
        {
            PackageStager.CopyDurable(manifestBackup, layout.ManifestPath, replace: File.Exists(layout.ManifestPath));
        }
        else if (File.Exists(layout.ManifestPath))
        {
            File.Delete(layout.ManifestPath);
        }
    }

    public void StoreInstallationSnapshots(byte[]? runtime, byte[]? upstream, byte[]? installationRecord)
    {
        WriteOptional("runtime.previous", runtime);
        WriteOptional("upstream.previous", upstream);
        WriteOptional("transaction-final.previous", installationRecord);
        var record = new InstallationSnapshotRecord(
            BridgeConstants.SchemaVersion,
            runtime is not null,
            upstream is not null,
            installationRecord is not null);
        AtomicFile.WriteUtf8(Path.Combine(Root, "installation-snapshots.json"),
            System.Text.Json.JsonSerializer.Serialize(record, JsonDefaults.Options));
    }

    public bool RestoreInstallationSnapshots()
    {
        var path = Path.Combine(Root, "installation-snapshots.json");
        if (!File.Exists(path))
        {
            return false;
        }

        var record = System.Text.Json.JsonSerializer.Deserialize<InstallationSnapshotRecord>(AtomicFile.ReadUtf8(path), JsonDefaults.Options)
                     ?? throw new InvalidDataException("Installation snapshot record is empty.");
        if (record.SchemaVersion != BridgeConstants.SchemaVersion)
        {
            throw new InvalidDataException("Installation snapshot record is invalid.");
        }

        RestoreOptional(layout.RuntimeConfigPath, "runtime.previous", record.RuntimeExisted);
        RestoreOptional(layout.UpstreamPath, "upstream.previous", record.UpstreamExisted);
        RestoreOptional(layout.TransactionRecordPath, "transaction-final.previous", record.InstallationRecordExisted);
        return record.InstallationRecordExisted;
    }

    public void Cleanup()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private void WriteOptional(string name, byte[]? content)
    {
        if (content is not null)
        {
            AtomicFile.WriteBytes(Path.Combine(Root, name), content);
        }
    }

    private void RestoreOptional(string destination, string name, bool existed)
    {
        var source = Path.Combine(Root, name);
        if (existed)
        {
            if (!File.Exists(source))
            {
                throw new InvalidDataException("Installation snapshot file is missing.");
            }

            PackageStager.CopyDurable(source, destination, replace: File.Exists(destination));
        }
        else if (File.Exists(destination))
        {
            File.Delete(destination);
        }
    }

    private static string ResolveContained(string root, string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Package rollback path escapes its root.");
        }

        return full;
    }
}

public sealed class PackageStager
{
    private static readonly IReadOnlySet<string> MutableTopLevelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "config",
        "state",
        "logs",
        "backups",
        "transaction-final.json",
    };

    public PackageRollback StageAndCommit(
        PackageManifest package,
        InstallationLayout layout,
        string transactionId,
        byte[]? previousRuntime = null,
        byte[]? previousUpstream = null,
        byte[]? previousInstallationRecord = null)
    {
        var staging = Path.Combine(layout.Root, $".staging-{transactionId}");
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        Directory.CreateDirectory(staging);
        var rollback = PrepareRollback(package, layout, transactionId);
        rollback.StoreInstallationSnapshots(previousRuntime, previousUpstream, previousInstallationRecord);
        try
        {
            var packageFinalRoot = VendorExecutableValidator.ResolveFinalPath(package.PackageRoot, isDirectory: true);
            var packagePrefix = Path.TrimEndingDirectorySeparator(packageFinalRoot) + Path.DirectorySeparatorChar;
            foreach (var entry in package.Entries)
            {
                RejectMutableDestination(entry.RelativePath);
                var source = package.ResolveContainedPath(entry.RelativePath);
                var sourceFinal = VendorExecutableValidator.ResolveFinalPath(source, isDirectory: false);
                if (!sourceFinal.StartsWith(packagePrefix, StringComparison.OrdinalIgnoreCase) ||
                    (File.GetAttributes(sourceFinal) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("Package entry escapes through a reparse point.");
                }

                var staged = ResolveContained(staging, entry.RelativePath);
                CopyDurable(sourceFinal, staged, replace: false);
            }

            CopyDurable(Path.Combine(package.PackageRoot, "manifest.sha256"), Path.Combine(staging, "manifest.sha256"), replace: false);
            var stagedManifest = PackageManifest.LoadAndVerify(staging);
            if (!string.Equals(stagedManifest.ManifestSha256, package.ManifestSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Staged package manifest changed.");
            }

            foreach (var entry in stagedManifest.Entries)
            {
                var source = stagedManifest.ResolveContainedPath(entry.RelativePath);
                var destination = ResolveContained(layout.Root, entry.RelativePath);
                CopyDurable(source, destination, replace: File.Exists(destination));
            }

            var newPaths = stagedManifest.Entries.Select(entry => entry.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var previous in rollback.Entries.Where(entry => entry.Existed && !newPaths.Contains(entry.RelativePath)))
            {
                var obsolete = ResolveContained(layout.Root, previous.RelativePath);
                if (File.Exists(obsolete))
                {
                    File.Delete(obsolete);
                }
            }

            CopyDurable(Path.Combine(staging, "manifest.sha256"), layout.ManifestPath, replace: File.Exists(layout.ManifestPath));
            var installed = PackageManifest.LoadAndVerify(layout.Root, allowInstalledMutableFiles: true);
            if (!string.Equals(installed.ManifestSha256, package.ManifestSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Installed package manifest changed.");
            }

            return rollback;
        }
        catch
        {
            rollback.Restore();
            throw;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static PackageRollback PrepareRollback(PackageManifest package, InstallationLayout layout, string transactionId)
    {
        var paths = package.Entries.Select(entry => entry.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(layout.ManifestPath))
        {
            foreach (var previous in PackageManifest.LoadAndVerify(layout.Root, allowInstalledMutableFiles: true).Entries)
            {
                paths.Add(previous.RelativePath);
            }
        }

        var entries = paths.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(path => new PackageRollbackEntry(path, File.Exists(ResolveContained(layout.Root, path))))
            .ToArray();
        var manifestExisted = File.Exists(layout.ManifestPath);
        var rollback = new PackageRollback(layout, transactionId, entries, manifestExisted);
        Directory.CreateDirectory(rollback.Root);
        foreach (var entry in entries.Where(entry => entry.Existed))
        {
            CopyDurable(
                ResolveContained(layout.Root, entry.RelativePath),
                ResolveContained(rollback.Root, entry.RelativePath),
                replace: false);
        }

        if (manifestExisted)
        {
            CopyDurable(layout.ManifestPath, Path.Combine(rollback.Root, "manifest.sha256.previous"), replace: false);
        }

        var record = new PackageRollbackRecord(BridgeConstants.SchemaVersion, transactionId, entries, manifestExisted);
        AtomicFile.WriteUtf8(Path.Combine(rollback.Root, "rollback.json"), System.Text.Json.JsonSerializer.Serialize(record, JsonDefaults.Options));
        return rollback;
    }

    private static void RejectMutableDestination(string relativePath)
    {
        var top = relativePath.Split('/')[0];
        if (MutableTopLevelNames.Contains(top) || top.StartsWith(".staging-", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Package manifest targets mutable installation state.");
        }
    }

    internal static string ResolveContained(string root, string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Package destination escapes installation root.");
        }

        return full;
    }

    internal static void CopyDurable(string source, string destination, bool replace)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            {
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }

            if (replace)
            {
                File.Replace(temporary, destination, null, ignoreMetadataErrors: false);
            }
            else
            {
                File.Move(temporary, destination);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
