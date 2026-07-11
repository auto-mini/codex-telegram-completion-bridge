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

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        var input = plaintext.ToArray();
        try
        {
            return ProtectedData.Protect(input, Entropy, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    public byte[] Unprotect(ReadOnlySpan<byte> ciphertext)
    {
        var input = ciphertext.ToArray();
        try
        {
            return ProtectedData.Unprotect(input, Entropy, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }
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
            try
            {
                AtomicFile.WriteBytes(path, ciphertext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(ciphertext);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public T Load()
    {
        var ciphertext = File.ReadAllBytes(path);
        try
        {
            var plaintext = protector.Unprotect(ciphertext);
            try
            {
                return JsonSerializer.Deserialize<T>(plaintext, JsonDefaults.Options)
                       ?? throw new InvalidDataException("Protected JSON is empty.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ciphertext);
        }
    }
}

public static class ProtectedJsonCodec
{
    public static byte[] Protect<T>(T value, ISecretProtector protector)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(value, JsonDefaults.Options);
        try
        {
            return protector.Protect(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static T Unprotect<T>(ReadOnlySpan<byte> ciphertext, ISecretProtector protector)
    {
        var plaintext = protector.Unprotect(ciphertext);
        try
        {
            return JsonSerializer.Deserialize<T>(plaintext, JsonDefaults.Options)
                   ?? throw new InvalidDataException("Protected JSON is empty.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
