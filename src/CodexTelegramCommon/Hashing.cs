using System.Security.Cryptography;
using System.Text;

namespace CodexTelegramCommon;

public static class Hashing
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string EventId(string machineId, string threadId, string turnId)
    {
        var material = string.Concat(machineId, "\n", threadId, "\n", turnId);
        return Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(material))).ToLowerInvariant();
    }

    public static string Sha256Hex(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string Sha256File(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
