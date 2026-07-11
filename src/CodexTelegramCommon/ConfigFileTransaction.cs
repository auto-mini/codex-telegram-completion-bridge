using System.Security.AccessControl;

namespace CodexTelegramCommon;

public sealed record ConfigEditResult(string BeforeSha256, string AfterSha256, byte[] EditedBytes);

public sealed class ConfigFileTransaction(ISecretProtector protector)
{
    public ConfigEditResult BackupAndReplace(
        string configPath,
        string expectedSha256,
        IReadOnlyList<string> notifyArgv,
        string backupPath,
        DateTimeOffset nowUtc)
    {
        CreateBackup(configPath, expectedSha256, backupPath, nowUtc);
        return ReplaceNotify(configPath, expectedSha256, notifyArgv);
    }

    public ConfigBackupRecord CreateBackup(string configPath, string expectedSha256, string backupPath, DateTimeOffset nowUtc)
    {
        var existed = File.Exists(configPath);
        var before = existed ? File.ReadAllBytes(configPath) : [];
        var actualHash = Hashing.Sha256Hex(before);
        if (!string.Equals(actualHash, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("CONFIG_HASH_CHANGED");
        }

        var backup = new ConfigBackupRecord(
            BridgeConstants.SchemaVersion,
            Path.GetFullPath(configPath),
            existed,
            actualHash,
            before,
            nowUtc);
        backup.Validate();
        new ProtectedJsonStore<ConfigBackupRecord>(backupPath, protector).Save(backup);
        var verified = new ProtectedJsonStore<ConfigBackupRecord>(backupPath, protector).Load();
        verified.Validate();
        return backup;
    }

    public ConfigEditResult ReplaceNotify(string configPath, string expectedSha256, IReadOnlyList<string> notifyArgv)
    {
        if (notifyArgv.Count != 2 || !Path.IsPathFullyQualified(notifyArgv[0]) || !string.Equals(notifyArgv[1], "hook", StringComparison.Ordinal))
        {
            throw new ArgumentException("Bridge notify argv is invalid.", nameof(notifyArgv));
        }

        var result = ReplaceNotifyValue(configPath, expectedSha256, notifyArgv);
        if (!InstallPlanner.IsExactBridgeArgv(CodexConfigDocument.Parse(result.EditedBytes).NotifyArgv ?? [], notifyArgv[0]))
        {
            throw new InvalidDataException("CONFIG_POST_WRITE_VERIFY_FAILED");
        }

        return result;
    }

    public ConfigEditResult ReplaceNotifyValue(string configPath, string expectedSha256, IReadOnlyList<string>? notifyArgv)
    {
        var existed = File.Exists(configPath);
        var before = existed ? File.ReadAllBytes(configPath) : [];
        var actualHash = Hashing.Sha256Hex(before);
        if (!string.Equals(actualHash, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("CONFIG_HASH_CHANGED");
        }

        var document = CodexConfigDocument.Parse(before);
        var edited = document.RenderWithNotify(notifyArgv);
        var afterHash = Hashing.Sha256Hex(edited);
        ReplaceConfig(configPath, actualHash, edited, existed);
        var verified = File.Exists(configPath) ? File.ReadAllBytes(configPath) : [];
        var parsed = CodexConfigDocument.Parse(verified).NotifyArgv;
        if (!string.Equals(Hashing.Sha256Hex(verified), afterHash, StringComparison.Ordinal) || !SequenceEqual(parsed, notifyArgv))
        {
            throw new InvalidDataException("CONFIG_POST_WRITE_VERIFY_FAILED");
        }

        return new ConfigEditResult(actualHash, afterHash, edited);
    }

    public void RestoreBackup(string backupPath, string expectedCurrentSha256)
    {
        var backup = new ProtectedJsonStore<ConfigBackupRecord>(backupPath, protector).Load();
        backup.Validate();
        var current = File.Exists(backup.ConfigPath) ? File.ReadAllBytes(backup.ConfigPath) : [];
        if (!string.Equals(Hashing.Sha256Hex(current), expectedCurrentSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("CONFIG_RESTORE_CONFLICT");
        }

        if (backup.Existed)
        {
            ReplaceConfig(backup.ConfigPath, expectedCurrentSha256, backup.Content, File.Exists(backup.ConfigPath));
        }
        else if (File.Exists(backup.ConfigPath))
        {
            File.Delete(backup.ConfigPath);
        }

        var restored = File.Exists(backup.ConfigPath) ? File.ReadAllBytes(backup.ConfigPath) : [];
        if (!string.Equals(Hashing.Sha256Hex(restored), backup.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("CONFIG_RESTORE_VERIFY_FAILED");
        }
    }

    private static void ReplaceConfig(string path, string expectedSha256, ReadOnlySpan<byte> content, bool existed)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("Config path has no parent.");
        Directory.CreateDirectory(directory);
        var current = File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : [];
        if (!string.Equals(Hashing.Sha256Hex(current), expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("CONFIG_HASH_CHANGED");
        }

        FileSecurity? security = null;
        FileAttributes? attributes = null;
        if (existed && File.Exists(fullPath))
        {
            security = new FileInfo(fullPath).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner | AccessControlSections.Group);
            attributes = File.GetAttributes(fullPath);
            if ((attributes.Value & FileAttributes.ReadOnly) != 0)
            {
                throw new UnauthorizedAccessException("Codex config is read-only.");
            }
        }

        AtomicFile.WriteBytes(fullPath, content, temporary =>
        {
            if (security is not null)
            {
                new FileInfo(temporary).SetAccessControl(security);
            }

            if (attributes is not null)
            {
                File.SetAttributes(temporary, attributes.Value & ~FileAttributes.ReparsePoint);
            }
        });
    }

    private static bool SequenceEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right) =>
        left is null ? right is null : right is not null && left.SequenceEqual(right, StringComparer.Ordinal);
}

public sealed class TransactionJournalStore(string path, string userSid)
{
    public void Save(TransactionJournal journal)
    {
        journal.Validate();
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(journal, JsonDefaults.Options);
        AtomicFile.WriteBytes(path, bytes, temporary => WindowsAclManager.ProtectFile(temporary, userSid));
    }

    public TransactionJournal Load()
    {
        var journal = System.Text.Json.JsonSerializer.Deserialize<TransactionJournal>(AtomicFile.ReadUtf8(path), JsonDefaults.Options)
                      ?? throw new InvalidDataException("Transaction journal is empty.");
        journal.Validate();
        return journal;
    }
}
