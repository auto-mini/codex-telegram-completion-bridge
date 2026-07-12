using CodexTelegramCommon;
using Microsoft.Data.Sqlite;

namespace CodexTelegramIntegrationTests;

public sealed class QueueStoreIntegrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "CodexTelegramIntegration", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Deduplicates_and_completes_live_event()
    {
        var queue = CreateQueue();
        var item = CreateEvent(CaptureMode.Live);

        Assert.Equal(InsertOutcome.Inserted, queue.TryInsert(item));
        Assert.Equal(InsertOutcome.Duplicate, queue.TryInsert(item));

        var acquired = queue.AcquireNextDue(item.ObservedAtUtc.AddSeconds(1), networkDeliveryAllowed: true)!;
        Assert.Equal(EventState.Inflight, acquired.State);
        queue.SaveEnvelopeForDelivery(item.EventId, [1, 2, 3], item.ObservedAtUtc.AddSeconds(1));
        queue.MarkSent(item.EventId, item.ObservedAtUtc.AddSeconds(2));

        var stored = queue.GetEvent(item.EventId)!;
        Assert.Equal(EventState.Sent, stored.State);
        Assert.Equal([1, 2, 3], stored.DeliveryEnvelopeDpapi);
        Assert.Equal(1, queue.GetCounts().Sent);
        Assert.Equal("ok", queue.QuickCheck());
    }

    [Fact]
    public void Shadow_event_is_terminal_and_never_reacquired()
    {
        var queue = CreateQueue();
        var item = CreateEvent(CaptureMode.Shadow);
        queue.TryInsert(item);
        queue.AcquireNextDue(item.ObservedAtUtc.AddSeconds(1), networkDeliveryAllowed: false);

        queue.MarkShadow(item.EventId, [9, 8, 7], item.ObservedAtUtc.AddSeconds(1));

        Assert.Null(queue.AcquireNextDue(item.ObservedAtUtc.AddDays(1), networkDeliveryAllowed: true));
        Assert.Single(queue.ListShadow());
        Assert.Equal([9, 8, 7], queue.GetShadowEnvelope(1));
    }

    [Fact]
    public void Recovers_expired_lease()
    {
        var queue = CreateQueue();
        var item = CreateEvent(CaptureMode.Live);
        queue.TryInsert(item);
        queue.AcquireNextDue(item.ObservedAtUtc.AddSeconds(1), networkDeliveryAllowed: true);

        Assert.Equal(1, queue.RecoverExpiredLeases(item.ObservedAtUtc + BridgeConstants.WorkerLeaseDuration + TimeSpan.FromSeconds(2)));
        Assert.Equal(EventState.Pending, queue.GetEvent(item.EventId)!.State);
    }

    [Fact]
    public void Extends_global_telegram_cooldown_without_shortening_it()
    {
        var queue = CreateQueue();
        var item = CreateEvent(CaptureMode.Live);
        queue.TryInsert(item);
        queue.AcquireNextDue(item.ObservedAtUtc.AddSeconds(1), networkDeliveryAllowed: true);
        queue.SaveEnvelopeForDelivery(item.EventId, [1], item.ObservedAtUtc.AddSeconds(1));
        var firstDeadline = item.ObservedAtUtc.AddMinutes(5);
        queue.RescheduleDelivery(item.EventId, firstDeadline, "HTTP_429", item.ObservedAtUtc, firstDeadline);

        queue.AcquireNextDue(firstDeadline.AddSeconds(1), networkDeliveryAllowed: true);
        var shorterDeadline = item.ObservedAtUtc.AddMinutes(2);
        queue.RescheduleDelivery(item.EventId, firstDeadline.AddMinutes(1), "HTTP_429", item.ObservedAtUtc.AddSeconds(1), shorterDeadline);

        Assert.Equal(firstDeadline, queue.GetTelegramNotBeforeUtc());
        Assert.Contains(queue.GetHealthConditions(), condition => condition.ConditionCode == HealthCodes.TelegramRetrying);
    }

    [Fact]
    public void Busy_database_falls_back_to_spool_and_imports_after_release()
    {
        var queue = CreateQueue();
        var spool = new EmergencySpool(Path.Combine(root, "spool"));
        var item = CreateEvent(CaptureMode.Shadow);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = queue.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString();

        using (var blocker = new SqliteConnection(connectionString))
        {
            blocker.Open();
            using var transaction = blocker.BeginTransaction(deferred: false);
            Assert.Equal(InsertOutcome.Busy, queue.TryInsert(item, busyTimeoutMilliseconds: 10));
            Assert.True(spool.TryWrite(item));
            transaction.Rollback();
        }

        var result = spool.ImportAll(queue, item.ObservedAtUtc.AddSeconds(1));
        Assert.Equal(1, result.Imported);
        Assert.Equal(0, spool.CountPending());
        Assert.NotNull(queue.GetEvent(item.EventId));
    }

    [Fact]
    public void Corrupt_spool_is_preserved_and_degrades_health()
    {
        var queue = CreateQueue();
        var spool = new EmergencySpool(Path.Combine(root, "spool"));
        Directory.CreateDirectory(spool.SpoolDirectory);
        File.WriteAllText(Path.Combine(spool.SpoolDirectory, $"{new string('a', 64)}.json"), "{not-json}");

        var result = spool.ImportAll(queue, DateTimeOffset.UtcNow);

        Assert.Equal(1, result.Corrupt);
        Assert.Equal(1, spool.CountCorrupt());
        Assert.Contains(queue.GetHealthConditions(), condition => condition.ConditionCode == HealthCodes.SpoolCorrupt);
    }

    [Fact]
    public async Task Concurrent_duplicate_inserts_create_one_row()
    {
        var queue = CreateQueue();
        var item = CreateEvent(CaptureMode.Live);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => queue.TryInsert(item, 5_000))));

        Assert.Single(outcomes, outcome => outcome == InsertOutcome.Inserted);
        Assert.Equal(19, outcomes.Count(outcome => outcome == InsertOutcome.Duplicate));
        Assert.Equal(1, queue.GetCounts().Pending);
    }

    [Fact]
    public void Newer_or_unversioned_unknown_schema_is_never_downgraded()
    {
        Directory.CreateDirectory(root);
        var newerPath = Path.Combine(root, "newer.sqlite");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = newerPath, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version=2;";
            command.ExecuteNonQuery();
        }

        Assert.Throws<InvalidDataException>(() => new QueueStore(newerPath).Initialize());
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = newerPath, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            Assert.Equal(2L, (long)command.ExecuteScalar()!);
        }

        var unknownPath = Path.Combine(root, "unknown.sqlite");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = unknownPath, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE unknown(value TEXT);";
            command.ExecuteNonQuery();
        }

        Assert.Throws<InvalidDataException>(() => new QueueStore(unknownPath).Initialize());
    }

    [Fact]
    public void Null_identity_spool_is_quarantined_instead_of_crashing_import()
    {
        var queue = CreateQueue();
        var spool = new EmergencySpool(Path.Combine(root, "null-spool"));
        Directory.CreateDirectory(spool.SpoolDirectory);
        File.WriteAllText(
            Path.Combine(spool.SpoolDirectory, $"{new string('b', 64)}.json"),
            "{\"schema_version\":1,\"event_id\":null,\"machine_id\":null,\"thread_id\":null,\"turn_id\":null,\"observed_at_utc\":\"2026-07-11T00:00:00+00:00\",\"ingest_mode\":\"shadow\"}");

        var result = spool.ImportAll(queue, DateTimeOffset.UtcNow);

        Assert.Equal(1, result.Corrupt);
        Assert.Equal(1, spool.CountCorrupt());
    }

    [Fact]
    public void Daily_quick_check_marker_blocks_until_explicit_verified_clear()
    {
        var layout = new InstallationLayout(Path.Combine(root, "maintenance"));
        layout.EnsureMutableDirectories();
        var queue = new QueueStore(layout.DatabasePath);
        queue.Initialize();
        var now = DateTimeOffset.UtcNow;

        Assert.True(LocalStateMaintenance.CheckIfDue(layout, queue, now));
        Assert.True(File.Exists(layout.QuickCheckStampPath));
        LocalStateMaintenance.MarkBlocked(layout, now.AddMinutes(1));
        Assert.False(LocalStateMaintenance.CheckIfDue(layout, queue, now.AddMinutes(2)));
        Assert.True(File.Exists(layout.LocalStateBlockedMarkerPath));
        Assert.NotEmpty(Directory.EnumerateDirectories(layout.BackupsDirectory, "local-state-diagnostic-*"));

        LocalStateMaintenance.ClearAfterVerifiedRepair(layout);
        Assert.False(File.Exists(layout.LocalStateBlockedMarkerPath));
        Assert.True(LocalStateMaintenance.CheckIfDue(layout, queue, now.AddMinutes(3)));
    }

    [Fact]
    public void Pruning_removes_only_expired_terminal_rows()
    {
        var queue = CreateQueue();
        var now = DateTimeOffset.UtcNow;
        var oldSent = CreateEvent(CaptureMode.Live) with { ObservedAtUtc = now.AddDays(-40) };
        var oldShadow = CreateEvent(CaptureMode.Shadow) with { ObservedAtUtc = now.AddDays(-8) };
        var pending = CreateEvent(CaptureMode.Live) with { ObservedAtUtc = now.AddDays(-100) };
        queue.TryInsert(oldSent);
        queue.AcquireNextDue(now, true);
        queue.MarkSent(oldSent.EventId, now.AddDays(-31));
        queue.TryInsert(oldShadow);
        var acquiredShadow = queue.AcquireNextDue(now, false)!;
        Assert.Equal(oldShadow.EventId, acquiredShadow.EventId);
        queue.MarkShadow(oldShadow.EventId, [1], now.AddDays(-8));
        queue.TryInsert(pending);

        var removed = queue.PruneTerminal(now);

        Assert.Equal(2, removed);
        Assert.Null(queue.GetEvent(oldSent.EventId));
        Assert.Null(queue.GetEvent(oldShadow.EventId));
        Assert.NotNull(queue.GetEvent(pending.EventId));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private QueueStore CreateQueue()
    {
        Directory.CreateDirectory(root);
        var queue = new QueueStore(Path.Combine(root, "bridge-state.sqlite"));
        queue.Initialize();
        return queue;
    }

    private static MinimalEvent CreateEvent(CaptureMode mode)
    {
        var machineId = Guid.NewGuid().ToString("D");
        var threadId = Guid.NewGuid().ToString("D");
        var turnId = Guid.NewGuid().ToString("D");
        return new MinimalEvent(
            BridgeConstants.SchemaVersion,
            Hashing.EventId(machineId, threadId, turnId),
            machineId,
            threadId,
            turnId,
            DateTimeOffset.UtcNow,
            mode);
    }
}
