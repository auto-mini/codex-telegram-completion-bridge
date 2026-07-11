using System.Globalization;
using System.Text;

namespace CodexTelegramCommon;

public sealed class OperationalLog(string path)
{
    private const long RotationBytes = 5 * 1024 * 1024;
    private static readonly object Sync = new();

    public void Write(string severity, string operationCode, string? eventId = null, int? retryCount = null, Exception? exception = null)
    {
        var safeSeverity = SanitizeCode(severity);
        var safeOperation = SanitizeCode(operationCode);
        var prefix = eventId is { Length: >= BridgeConstants.EventIdLogPrefixLength }
            ? eventId[..BridgeConstants.EventIdLogPrefixLength]
            : "-";
        var exceptionClass = exception?.GetType().Name ?? "-";
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.UtcNow:O}\t{safeSeverity}\t{safeOperation}\tevent={prefix}\tretry={retryCount?.ToString(CultureInfo.InvariantCulture) ?? "-"}\tex={exceptionClass}{Environment.NewLine}");

        lock (Sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            RotateIfNeeded();
            File.AppendAllText(path, line, new UTF8Encoding(false));
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(path) || new FileInfo(path).Length < RotationBytes)
        {
            return;
        }

        for (var index = 4; index >= 1; index--)
        {
            var source = $"{path}.{index}";
            var destination = $"{path}.{index + 1}";
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            if (File.Exists(source))
            {
                File.Move(source, destination);
            }
        }

        File.Move(path, $"{path}.1", overwrite: true);
    }

    private static string SanitizeCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80)
        {
            return "INVALID_CODE";
        }

        foreach (var character in value)
        {
            if (!(character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_'))
            {
                return "INVALID_CODE";
            }
        }

        return value;
    }
}
