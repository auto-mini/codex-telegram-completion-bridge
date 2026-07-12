using System.Text;
using CodexTelegramCommon;

namespace CodexTelegramUnitTests;

public sealed class ConfigurationAndProtectionTests
{
    [Fact]
    public void Runtime_config_round_trips_atomically()
    {
        using var temp = new TempDirectory();
        var path = System.IO.Path.Combine(temp.Path, "bridge.json");
        var store = new RuntimeConfigStore(path);
        var expected = new RuntimeConfig
        {
            MachineId = Guid.NewGuid().ToString("D"),
            CodexHome = System.IO.Path.Combine(temp.Path, ".codex"),
            PcAlias = "테스트 PC",
            CaptureMode = CaptureMode.Shadow,
        };

        store.Save(expected);
        var actual = store.Load();

        Assert.Equal(expected.MachineId, actual.MachineId);
        Assert.Equal(expected.CodexHome, actual.CodexHome);
        Assert.Equal(expected.PcAlias, actual.PcAlias);
        Assert.DoesNotContain(".tmp", Directory.EnumerateFiles(temp.Path).Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_config_rejects_empty_alias_after_normalization()
    {
        var config = new RuntimeConfig
        {
            MachineId = Guid.NewGuid().ToString("D"),
            CodexHome = "C:\\Users\\test\\.codex",
            PcAlias = "\r\n",
        };

        Assert.Throws<InvalidDataException>(config.Validate);
    }

    [Fact]
    public void Protected_store_never_writes_plaintext()
    {
        using var temp = new TempDirectory();
        var path = System.IO.Path.Combine(temp.Path, "credentials.dpapi");
        var store = new ProtectedJsonStore<TelegramCredentials>(path, new ReversingProtector());
        var expected = new TelegramCredentials(1, "123456:super-secret-token", 123456, 42, "private");

        store.Save(expected);

        var bytes = File.ReadAllBytes(path);
        Assert.DoesNotContain("super-secret-token", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        Assert.Equal(expected, store.Load());
    }

    [Fact]
    public void Operational_log_keeps_only_redacted_shape()
    {
        using var temp = new TempDirectory();
        var path = System.IO.Path.Combine(temp.Path, "bridge.log");
        var log = new OperationalLog(path);

        log.Write("ERROR", "SEND_FAILED", new string('a', 64), 3, new InvalidOperationException("secret title and token"));
        var text = File.ReadAllText(path);

        Assert.Contains("SEND_FAILED", text, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret title", text, StringComparison.Ordinal);
        Assert.DoesNotContain("token", text, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('a', 64), text, StringComparison.Ordinal);
        Assert.Contains(new string('a', BridgeConstants.EventIdLogPrefixLength), text, StringComparison.Ordinal);
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
