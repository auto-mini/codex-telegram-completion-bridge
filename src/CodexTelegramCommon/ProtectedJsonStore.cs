using System.Security.Cryptography;
using System.Text.Json;

namespace CodexTelegramCommon;

public interface ISecretProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext);

    byte[] Unprotect(ReadOnlySpan<byte> ciphertext);
}

public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "CodexTelegramBridge:v1"u8.ToArray();

    public byte[] Protect(ReadOnlySpan<byte> plaintext) =>
        ProtectedData.Protect(plaintext.ToArray(), Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(ReadOnlySpan<byte> ciphertext) =>
        ProtectedData.Unprotect(ciphertext.ToArray(), Entropy, DataProtectionScope.CurrentUser);
}

public sealed class ProtectedJsonStore<T>(string path, ISecretProtector protector)
{
    public bool Exists => File.Exists(path);

    public void Save(T value)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(value, JsonDefaults.Options);
        try
        {
            var ciphertext = protector.Protect(plaintext);
            AtomicFile.WriteBytes(path, ciphertext);
            CryptographicOperations.ZeroMemory(ciphertext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public T Load()
    {
        var ciphertext = File.ReadAllBytes(path);
        var plaintext = protector.Unprotect(ciphertext);
        try
        {
            return JsonSerializer.Deserialize<T>(plaintext, JsonDefaults.Options)
                   ?? throw new InvalidDataException("Protected JSON is empty.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(ciphertext);
        }
    }
}
