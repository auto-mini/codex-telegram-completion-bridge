using System.Globalization;

namespace CodexTelegramCommon;

public static class LocalStateMaintenance
{
    public static bool CheckIfDue(InstallationLayout layout, QueueStore queue, DateTimeOffset nowUtc)
    {
        if (File.Exists(layout.LocalStateBlockedMarkerPath))
        {
            return false;
        }

        if (File.Exists(layout.QuickCheckStampPath) &&
            DateTimeOffset.TryParseExact(
                File.ReadAllText(layout.QuickCheckStampPath),
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var last) &&
            nowUtc - last < TimeSpan.FromDays(1))
        {
            return true;
        }

        try
        {
            if (!string.Equals(queue.QuickCheck(), "ok", StringComparison.OrdinalIgnoreCase))
            {
                MarkBlocked(layout, nowUtc);
                TrySetHealth(queue, nowUtc);
                return false;
            }

            AtomicFile.WriteUtf8(layout.QuickCheckStampPath, nowUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {
            MarkBlocked(layout, nowUtc);
            TrySetHealth(queue, nowUtc);
            return false;
        }
    }

    public static void MarkBlocked(InstallationLayout layout, DateTimeOffset nowUtc)
    {
        try
        {
            layout.EnsureMutableDirectories();
            BackupDatabaseFiles(layout, nowUtc);
            AtomicFile.WriteUtf8(layout.LocalStateBlockedMarkerPath, $"{HealthCodes.LocalStateBlocked}\n");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static void ClearAfterVerifiedRepair(InstallationLayout layout)
    {
        var queue = new QueueStore(layout.DatabasePath);
        queue.ValidateExistingSchema();
        if (!string.Equals(queue.QuickCheck(), "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("LOCAL_STATE_QUICK_CHECK_FAILED");
        }

        queue.ClearHealth(HealthCodes.LocalStateBlocked);
        if (File.Exists(layout.LocalStateBlockedMarkerPath))
        {
            File.Delete(layout.LocalStateBlockedMarkerPath);
        }

        AtomicFile.WriteUtf8(layout.QuickCheckStampPath, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
    }

    private static void BackupDatabaseFiles(InstallationLayout layout, DateTimeOffset nowUtc)
    {
        var destination = Path.Combine(layout.BackupsDirectory, $"local-state-diagnostic-{nowUtc:yyyyMMddTHHmmssZ}");
        Directory.CreateDirectory(destination);
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var source = layout.DatabasePath + suffix;
            if (File.Exists(source))
            {
                var target = Path.Combine(destination, Path.GetFileName(source));
                using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough);
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }
        }
    }

    private static void TrySetHealth(QueueStore queue, DateTimeOffset nowUtc)
    {
        try
        {
            queue.UpsertHealth(HealthCodes.LocalStateBlocked, nowUtc);
        }
        catch (Exception)
        {
        }
    }
}
