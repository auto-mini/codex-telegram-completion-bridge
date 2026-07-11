using System.Text;
using CodexTelegramCommon;

namespace CodexTelegramIntegrationTests;

public sealed class ConfigFileTransactionIntegrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ConfigFileTransactionTests", Guid.NewGuid().ToString("N"));
    private readonly ReversingProtector protector = new();

    [Fact]
    public void Backup_replace_and_restore_are_hash_guarded()
    {
        Directory.CreateDirectory(root);
        var config = Path.Combine(root, "config.toml");
        var backup = Path.Combine(root, "backup.dpapi");
        var original = Encoding.UTF8.GetBytes("# keep\r\nmodel = \"gpt\"\r\n[features]\r\nflag = true\r\n");
        File.WriteAllBytes(config, original);
        var bridge = Path.Combine(root, "CodexTelegramBridge.exe");
        var transaction = new ConfigFileTransaction(protector);

        var result = transaction.BackupAndReplace(config, Hashing.Sha256Hex(original), [bridge, "hook"], backup, DateTimeOffset.UtcNow);

        Assert.NotEqual(result.BeforeSha256, result.AfterSha256);
        Assert.Equal([bridge, "hook"], CodexConfigDocument.Parse(File.ReadAllBytes(config)).NotifyArgv);
        Assert.DoesNotContain("model", Encoding.UTF8.GetString(File.ReadAllBytes(backup)), StringComparison.Ordinal);

        transaction.RestoreBackup(backup, result.AfterSha256);
        Assert.Equal(original, File.ReadAllBytes(config));
    }

    [Fact]
    public void Changed_config_blocks_replace_and_restore()
    {
        Directory.CreateDirectory(root);
        var config = Path.Combine(root, "config.toml");
        var backup = Path.Combine(root, "backup.dpapi");
        File.WriteAllText(config, "model = \"one\"\n");
        var transaction = new ConfigFileTransaction(protector);
        var originalHash = Hashing.Sha256File(config);
        File.WriteAllText(config, "model = \"changed\"\n");

        Assert.Throws<InvalidOperationException>(() =>
            transaction.BackupAndReplace(config, originalHash, [Path.Combine(root, "CodexTelegramBridge.exe"), "hook"], backup, DateTimeOffset.UtcNow));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ReversingProtector : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plaintext)
        {
            var bytes = plaintext.ToArray();
            Array.Reverse(bytes);
            return bytes;
        }

        public byte[] Unprotect(ReadOnlySpan<byte> ciphertext) => Protect(ciphertext);
    }
}
