using System.Text;

namespace CodexTelegramCommon;

public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(false, true);

    public static void WriteUtf8(string path, string content) => WriteBytes(path, Utf8NoBom.GetBytes(content));

    public static void WriteBytes(string path, ReadOnlySpan<byte> content) => WriteBytes(path, content, null);

    public static void WriteBytes(string path, ReadOnlySpan<byte> content, Action<string>? prepareTemporary)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("Target has no directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            prepareTemporary?.Invoke(temporary);

            if (File.Exists(fullPath))
            {
                File.Replace(temporary, fullPath, null, ignoreMetadataErrors: false);
            }
            else
            {
                File.Move(temporary, fullPath);
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

    public static string ReadUtf8(string path) => File.ReadAllText(path, Utf8NoBom);
}
