using System.Text.Json;

namespace CodexTelegramCommon;

public sealed class EmergencySpool(string spoolDirectory)
{
    public string SpoolDirectory { get; } = Path.GetFullPath(spoolDirectory);

    public string CorruptDirectory => Path.Combine(SpoolDirectory, "corrupt");

    public bool TryWrite(MinimalEvent item)
    {
        if (!IsValid(item))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(SpoolDirectory);
            var path = Path.Combine(SpoolDirectory, $"{item.EventId}.json");
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
            JsonSerializer.Serialize(stream, item, JsonDefaults.Options);
            stream.Flush(flushToDisk: true);
            return true;
        }
        catch (IOException) when (File.Exists(Path.Combine(SpoolDirectory, $"{item.EventId}.json")))
        {
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public SpoolImportResult ImportAll(QueueStore queue, DateTimeOffset nowUtc)
    {
        Directory.CreateDirectory(SpoolDirectory);
        Directory.CreateDirectory(CorruptDirectory);
        var imported = 0;
        var duplicates = 0;
        var corrupt = 0;
        var busy = 0;

        foreach (var path in Directory.EnumerateFiles(SpoolDirectory, "*.json", SearchOption.TopDirectoryOnly).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var item = JsonSerializer.Deserialize<MinimalEvent>(bytes, JsonDefaults.Options)
                           ?? throw new InvalidDataException("Empty spool record.");
                ValidateFileName(path, item);
                var outcome = queue.TryInsert(item, busyTimeoutMilliseconds: 5_000);
                if (outcome == InsertOutcome.Busy)
                {
                    busy++;
                    continue;
                }

                File.Delete(path);
                if (outcome == InsertOutcome.Inserted)
                {
                    imported++;
                }
                else
                {
                    duplicates++;
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException)
            {
                MoveToCorrupt(path);
                queue.UpsertHealth(HealthCodes.SpoolCorrupt, nowUtc);
                corrupt++;
            }
        }

        return new SpoolImportResult(imported, duplicates, corrupt, busy);
    }

    public long CountPending() => Directory.Exists(SpoolDirectory)
        ? Directory.EnumerateFiles(SpoolDirectory, "*.json", SearchOption.TopDirectoryOnly).LongCount()
        : 0;

    public long CountCorrupt() => Directory.Exists(CorruptDirectory)
        ? Directory.EnumerateFiles(CorruptDirectory, "*.json", SearchOption.TopDirectoryOnly).LongCount()
        : 0;

    private static void ValidateFileName(string path, MinimalEvent item)
    {
        if (!IsValid(item))
        {
            throw new InvalidDataException("Spool event shape is invalid.");
        }

        var fileName = Path.GetFileNameWithoutExtension(path);
        var expected = Hashing.EventId(item.MachineId, item.ThreadId, item.TurnId);
        if (item.SchemaVersion != BridgeConstants.SchemaVersion ||
            !string.Equals(fileName, item.EventId, StringComparison.Ordinal) ||
            !string.Equals(expected, item.EventId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Spool event identity mismatch.");
        }
    }

    private static bool IsValid(MinimalEvent item) =>
        item.SchemaVersion == BridgeConstants.SchemaVersion &&
        item.EventId is { Length: 64 } &&
        item.EventId.All(Uri.IsHexDigit) &&
        Guid.TryParseExact(item.MachineId, "D", out _) &&
        NotifyPayloadParser.IsValidOpaqueId(item.ThreadId) &&
        NotifyPayloadParser.IsValidOpaqueId(item.TurnId) &&
        string.Equals(Hashing.EventId(item.MachineId, item.ThreadId, item.TurnId), item.EventId, StringComparison.Ordinal);

    private void MoveToCorrupt(string path)
    {
        var destination = Path.Combine(CorruptDirectory, Path.GetFileName(path));
        if (File.Exists(destination))
        {
            destination = Path.Combine(CorruptDirectory, $"{Path.GetFileNameWithoutExtension(path)}.{Guid.NewGuid():N}.json");
        }

        File.Move(path, destination);
    }
}

public sealed record SpoolImportResult(int Imported, int Duplicates, int Corrupt, int Busy);
