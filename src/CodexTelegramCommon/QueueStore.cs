using System.Globalization;
using Microsoft.Data.Sqlite;

namespace CodexTelegramCommon;

public sealed class QueueStore(string databasePath)
{
    private const string TimestampFormat = "O";

    public string DatabasePath { get; } = Path.GetFullPath(databasePath);

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        using var connection = OpenConnection(readOnly: false, busyTimeoutMilliseconds: 5_000);
        ExecuteNonQuery(connection, "PRAGMA journal_mode=WAL;");
        ExecuteNonQuery(connection, "PRAGMA synchronous=FULL;");
        ExecuteNonQuery(connection, "PRAGMA foreign_keys=ON;");
        var version = ReadUserVersion(connection);
        if (version == 0)
        {
            if (HasUserTables(connection))
            {
                throw new InvalidDataException("Unversioned bridge database contains unknown tables.");
            }

            using var transaction = connection.BeginTransaction(deferred: false);
            ExecuteNonQuery(connection, SchemaSql, transaction);
            ExecuteNonQuery(connection, $"PRAGMA user_version={BridgeConstants.SchemaVersion};", transaction);
            transaction.Commit();
        }
        else if (version != BridgeConstants.SchemaVersion)
        {
            throw new InvalidDataException("Bridge database schema version is unsupported.");
        }

        ValidateSchema(connection);
    }

    public int GetSchemaVersion()
    {
        using var connection = OpenConnection(readOnly: true, busyTimeoutMilliseconds: 5_000);
        return ReadUserVersion(connection);
    }

    public void ValidateExistingSchema()
    {
        using var connection = OpenConnection(readOnly: true, busyTimeoutMilliseconds: 5_000);
        if (ReadUserVersion(connection) != BridgeConstants.SchemaVersion)
        {
            throw new InvalidDataException("Bridge database schema version is unsupported.");
        }

        ValidateSchema(connection);
    }

    public InsertOutcome TryInsert(MinimalEvent item, int? busyTimeoutMilliseconds = null)
    {
        ValidateMinimalEvent(item);
        try
        {
            using var connection = OpenConnection(
                readOnly: false,
                busyTimeoutMilliseconds ?? (int)BridgeConstants.HookDatabaseBusyTimeout.TotalMilliseconds);
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO events (
                    event_id, machine_id, thread_id, turn_id, observed_at_utc, ingest_mode, state,
                    resolution_attempt_count, delivery_attempt_count, next_attempt_at_utc)
                VALUES ($event_id, $machine_id, $thread_id, $turn_id, $observed, $mode, 'pending', 0, 0, $next);
                """;
            command.Parameters.AddWithValue("$event_id", item.EventId);
            command.Parameters.AddWithValue("$machine_id", item.MachineId);
            command.Parameters.AddWithValue("$thread_id", item.ThreadId);
            command.Parameters.AddWithValue("$turn_id", item.TurnId);
            command.Parameters.AddWithValue("$observed", Format(item.ObservedAtUtc));
            command.Parameters.AddWithValue("$mode", ToDatabase(item.IngestMode));
            command.Parameters.AddWithValue("$next", Format(item.ObservedAtUtc));
            return command.ExecuteNonQuery() == 1 ? InsertOutcome.Inserted : InsertOutcome.Duplicate;
        }
        catch (SqliteException exception) when (IsBusy(exception))
        {
            return InsertOutcome.Busy;
        }
    }

    public EventRecord? AcquireNextDue(DateTimeOffset nowUtc, bool networkDeliveryAllowed)
    {
        using var connection = OpenConnection(readOnly: false, busyTimeoutMilliseconds: 5_000);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = networkDeliveryAllowed
            ? """
              SELECT * FROM events
              WHERE state = 'pending' AND next_attempt_at_utc <= $now
              ORDER BY next_attempt_at_utc, observed_at_utc, event_id
              LIMIT 1;
              """
            : """
              SELECT * FROM events
              WHERE state = 'pending' AND delivery_envelope_dpapi IS NULL AND next_attempt_at_utc <= $now
              ORDER BY next_attempt_at_utc, observed_at_utc, event_id
              LIMIT 1;
              """;
        select.Parameters.AddWithValue("$now", Format(nowUtc));
        EventRecord? item;
        using (var reader = select.ExecuteReader())
        {
            item = reader.Read() ? ReadEvent(reader) : null;
        }

        if (item is null)
        {
            transaction.Commit();
            return null;
        }

        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE events
            SET state = 'inflight', lease_until_utc = $lease
            WHERE event_id = $event_id AND state = 'pending';
            """;
        update.Parameters.AddWithValue("$event_id", item.EventId);
        update.Parameters.AddWithValue("$lease", Format(nowUtc + BridgeConstants.WorkerLeaseDuration));
        if (update.ExecuteNonQuery() != 1)
        {
            transaction.Rollback();
            return null;
        }

        transaction.Commit();
        return item with { State = EventState.Inflight, LeaseUntilUtc = nowUtc + BridgeConstants.WorkerLeaseDuration };
    }

    public int RecoverExpiredLeases(DateTimeOffset nowUtc)
    {
        using var connection = OpenConnection(readOnly: false, busyTimeoutMilliseconds: 5_000);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE events
            SET state = 'pending', lease_until_utc = NULL, last_error_code = 'STALE_LEASE_RECOVERED'
            WHERE state = 'inflight' AND lease_until_utc <= $now;
            """;
        command.Parameters.AddWithValue("$now", Format(nowUtc));
        return command.ExecuteNonQuery();
    }

    public void RescheduleResolution(string eventId, DateTimeOffset nextAttemptUtc, string errorCode)
    {
        TransitionInflight(
            eventId,
            """
            state = 'pending', lease_until_utc = NULL, next_attempt_at_utc = $next,
            resolution_attempt_count = resolution_attempt_count + 1, last_error_code = $error
            """,
            command =>
            {
                command.Parameters.AddWithValue("$next", Format(nextAttemptUtc));
                command.Parameters.AddWithValue("$error", ValidateCode(errorCode));
            });
    }

    public void SaveEnvelopeForDelivery(string eventId, byte[] protectedEnvelope, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(protectedEnvelope);
        TransitionInflight(
            eventId,
            """
            delivery_envelope_dpapi = $envelope, next_attempt_at_utc = $next, last_error_code = NULL
            """,
            command =>
            {
                command.Parameters.Add("$envelope", SqliteType.Blob).Value = protectedEnvelope;
                command.Parameters.AddWithValue("$next", Format(nowUtc));
            });
    }

    public void ReleaseInflightForDelivery(string eventId, DateTimeOffset nextAttemptUtc, string? errorCode = null)
    {
        TransitionInflight(
            eventId,
            "state = 'pending', lease_until_utc = NULL, next_attempt_at_utc = $next, last_error_code = $error",
            command =>
            {
                command.Parameters.AddWithValue("$next", Format(nextAttemptUtc));
                command.Parameters.AddWithValue("$error", errorCode is null ? DBNull.Value : ValidateCode(errorCode));
            });
    }

    public void MarkShadow(string eventId, byte[] protectedEnvelope, DateTimeOffset completedUtc) =>
        Complete(eventId, EventState.Shadow, completedUtc, protectedEnvelope, null);

    public void MarkSuppressed(string eventId, DateTimeOffset completedUtc, string errorCode = "VERIFIED_SUBAGENT") =>
        Complete(eventId, EventState.Suppressed, completedUtc, null, errorCode);

    public void MarkQuarantine(string eventId, DateTimeOffset completedUtc, string errorCode)
    {
        using var connection = OpenConnection(readOnly: false, busyTimeoutMilliseconds: 5_000);
        using var transaction = connection.BeginTransaction(deferred: false);
        Complete(connection, transaction, eventId, EventState.Quarantine, completedUtc, null, errorCode);
        UpsertHealth(connection, transaction, HealthCodes.EventQuarantined, completedUtc, null);
        transaction.Commit();
    }

    public void MarkSent(string eventId, DateTimeOffset completedUtc)
    {
        using var connection = OpenConnection(readOnly: false, busyTimeoutMilliseconds: 5_000);
        using var transaction = connection.BeginTransaction(deferred: false);
        Complete(connection, transaction, eventId, EventState.Sent, completedUtc, null, null, preserveEnvelope: true);
        DeleteHealth(connection, transaction, HealthCodes.TelegramRetrying);
        transaction.Commit();
    }

    public void RescheduleDelivery(
        string eventId,
        DateTimeOffset nextAttemptUtc,
        string errorCode,
        DateTimeOffset observedUtc,
        DateTimeOffset? globalNotBeforeUtc = null)
    {
        using var connection = OpenConnection(readOnly: false, busyTimeoutMilliseconds: 5_000);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE events
            SET state = 'pending', lease_until_utc = NULL, next_attempt_at_utc = $next,
                delivery_attempt_count = delivery_attempt_count + 1, last_error_code = $error
            WHERE event_id = $event_id AND state = 'inflight';
            """;
        command.Parameters.AddWithValue("$event_id", eventId);
        command.Parameters.AddWithValue("$next", Format(nextAttemptUtc));
        command.Parameters.AddWithValue("$error", ValidateCode(errorCode));
        EnsureOne(command.ExecuteNonQuery(), eventId);
        UpsertHealth(connection, transaction, HealthCodes.TelegramRetrying, observedUtc, globalNotBeforeUtc);
        transaction.Commit();
    }

    public void BlockDelivery(string eventId, string conditionCode, DateTimeOffset observedUtc)
    {
        if (!HealthCodes.Blocking.Contains(conditionCode))
        {
            throw new ArgumentOutOfRangeException(nameof(conditionCode));
        }

        using var connection = OpenConnection(readOnly: false, busyTimeoutMilliseconds: 5_000);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE events
            SET state = 'pending', lease_until_utc = NULL,
                delivery_attempt_count = delivery_attempt_count + 1, last_error_code = $error
            WHERE event_id = $event_id AND state = 'inflight';
            """;
        command.Parameters.AddWithValue("$event_id", eventId);
        command.Parameters.AddWithValue("$error", conditionCode);
        EnsureOne(command.ExecuteNonQuery(), eventId);
        UpsertHealth(connection, transaction, conditionCode, observedUtc, null);
        transaction.Commit();
    }

    public void UpsertHealth(string conditionCode, DateTimeOffset observedUtc, DateTimeOffset? notBeforeUtc = null)
    {
        using var connection = OpenConnection(readOnly: false, busyTimeoutMilliseconds: 5_000);
        using var transaction = connection.BeginTransaction(deferred: false);
        UpsertHealth(connection, transaction, conditionCode, observedUtc, notBeforeUtc);
        transaction.Commit();
    }

    public void ClearHealth(params string[] conditionCodes)
    {
        using var connection = OpenConnection(readOnly: false, busyTimeoutMilliseconds: 5_000);
        using var transaction = connection.BeginTransaction(deferred: false);
        foreach (var code in conditionCodes)
        {
            DeleteHealth(connection, transaction, ValidateHealthCode(code));
        }

        transaction.Commit();
    }

    public IReadOnlyList<HealthConditionRecord> GetHealthConditions()
    {
        using var connection = OpenConnection(readOnly: true, busyTimeoutMilliseconds: 5_000);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT condition_code, first_observed_utc, last_observed_utc, not_before_utc FROM health_conditions ORDER BY condition_code;";
        using var reader = command.ExecuteReader();
        var items = new List<HealthConditionRecord>();
        while (reader.Read())
        {
            var code = reader.GetString(0);
            ValidateHealthCode(code);
            items.Add(new HealthConditionRecord(
                code,
                Parse(reader.GetString(1)),
                Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : Parse(reader.GetString(3))));
        }

        return items;
    }

    public bool HasBlockingHealthCondition() => GetHealthConditions().Any(item => HealthCodes.Blocking.Contains(item.ConditionCode));

    public bool HasNetworkBlockingHealthCondition() => GetHealthConditions().Any(item => HealthCodes.NetworkBlocking.Contains(item.ConditionCode));

    public DateTimeOffset? GetTelegramNotBeforeUtc() =>
        GetHealthConditions().FirstOrDefault(item => item.ConditionCode == HealthCodes.TelegramRetrying)?.NotBeforeUtc;

    public QueueCounts GetCounts()
    {
        using var connection = OpenConnection(readOnly: true, busyTimeoutMilliseconds: 5_000);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT state, COUNT(*) FROM events GROUP BY state;";
        using var reader = command.ExecuteReader();
        var values = new Dictionary<EventState, long>();
        while (reader.Read())
        {
            values[ParseState(reader.GetString(0))] = reader.GetInt64(1);
        }

        return new QueueCounts(
            Get(EventState.Pending),
            Get(EventState.Inflight),
            Get(EventState.Sent),
            Get(EventState.Shadow),
            Get(EventState.Suppressed),
            Get(EventState.Quarantine));

        long Get(EventState state) => values.GetValueOrDefault(state);
    }

    public DateTimeOffset? GetOldestPendingUtc()
    {
        using var connection = OpenConnection(readOnly: true, busyTimeoutMilliseconds: 5_000);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MIN(observed_at_utc) FROM events WHERE state IN ('pending', 'inflight');";
        var value = command.ExecuteScalar();
        return value is string text ? Parse(text) : null;
    }

    public DateTimeOffset? GetLastSuccessfulSendUtc()
    {
        using var connection = OpenConnection(readOnly: true, busyTimeoutMilliseconds: 5_000);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(completed_at_utc) FROM events WHERE state = 'sent';";
        var value = command.ExecuteScalar();
        return value is string text ? Parse(text) : null;
    }

    public DateTimeOffset? GetLastNetworkActivityUtc()
    {
        using var connection = OpenConnection(readOnly: true, busyTimeoutMilliseconds: 5_000);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MAX(value) FROM (
                SELECT MAX(completed_at_utc) AS value FROM events WHERE state = 'sent'
                UNION ALL
                SELECT MAX(last_observed_utc) AS value FROM health_conditions
                WHERE condition_code IN ('TELEGRAM_RETRYING', 'AUTH_BLOCKED', 'CHAT_BLOCKED', 'TELEGRAM_API_BLOCKED')
            );
            """;
        var value = command.ExecuteScalar();
        return value is string text ? Parse(text) : null;
    }

    public DateTimeOffset? GetNextPendingDueUtc(bool includeDeliveryReady)
    {
        using var connection = OpenConnection(readOnly: true, busyTimeoutMilliseconds: 5_000);
        using var command = connection.CreateCommand();
        command.CommandText = includeDeliveryReady
            ? "SELECT MIN(next_attempt_at_utc) FROM events WHERE state = 'pending';"
            : "SELECT MIN(next_attempt_at_utc) FROM events WHERE state = 'pending' AND delivery_envelope_dpapi IS NULL;";
        var value = command.ExecuteScalar();
        return value is string text ? Parse(text) : null;
    }

    public IReadOnlyList<ShadowRecord> ListShadow()
    {
        using var connection = OpenConnection(readOnly: true, busyTimeoutMilliseconds: 5_000);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT event_id, observed_at_utc FROM events WHERE state = 'shadow' ORDER BY observed_at_utc, event_id;";
        using var reader = command.ExecuteReader();
        var result = new List<ShadowRecord>();
        long sequence = 1;
        while (reader.Read())
        {
            result.Add(new ShadowRecord(sequence++, reader.GetString(0), Parse(reader.GetString(1))));
        }

        return result;
    }

    public IReadOnlyList<QuarantineRecord> ListQuarantine()
    {
        using var connection = OpenConnection(readOnly: true, busyTimeoutMilliseconds: 5_000);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT event_id, observed_at_utc, ingest_mode, last_error_code
            FROM events
            WHERE state = 'quarantine'
            ORDER BY observed_at_utc, event_id;
            """;
        using var reader = command.ExecuteReader();
        var result = new List<QuarantineRecord>();
        long sequence = 1;
        while (reader.Read())
        {
            var eventId = reader.GetString(0);
            var errorCode = reader.IsDBNull(3) ? null : reader.GetString(3);
            if (eventId.Length != 64 || !eventId.All(Uri.IsHexDigit) || errorCode is null)
            {
                throw new InvalidDataException("Quarantined event failed integrity validation.");
            }

            try
            {
                ValidateCode(errorCode);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new InvalidDataException("Quarantined event failed integrity validation.", exception);
            }

            result.Add(new QuarantineRecord(
                sequence++,
                eventId,
                Parse(reader.GetString(1)),
                ParseCaptureMode(reader.GetString(2)),
                errorCode));
        }

        return result;
    }

    public void AcknowledgeQuarantine(string eventId, DateTimeOffset completedUtc)
    {
        if (eventId.Length != 64 || !eventId.All(Uri.IsHexDigit))
        {
            throw new ArgumentOutOfRangeException(nameof(eventId));
        }

        using var connection = OpenConnection(readOnly: false, busyTimeoutMilliseconds: 5_000);
        using var transaction = connection.BeginTransaction(deferred: false);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE events
                SET state = 'suppressed', lease_until_utc = NULL, completed_at_utc = $completed,
                    last_error_code = $error, delivery_envelope_dpapi = NULL
                WHERE event_id = $event_id AND state = 'quarantine';
                """;
            command.Parameters.AddWithValue("$event_id", eventId);
            command.Parameters.AddWithValue("$completed", Format(completedUtc));
            command.Parameters.AddWithValue("$error", BridgeConstants.QuarantineAcknowledgedCode);
            EnsureOne(command.ExecuteNonQuery(), eventId);
        }

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(*) FROM events WHERE state = 'quarantine';";
            if ((long)command.ExecuteScalar()! == 0)
            {
                DeleteHealth(connection, transaction, HealthCodes.EventQuarantined);
            }
        }

        transaction.Commit();
    }

    public byte[]? GetShadowEnvelope(long sequence)
    {
        if (sequence < 1)
        {
            return null;
        }

        using var connection = OpenConnection(readOnly: true, busyTimeoutMilliseconds: 5_000);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT delivery_envelope_dpapi
            FROM events
            WHERE state = 'shadow'
            ORDER BY observed_at_utc, event_id
            LIMIT 1 OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$offset", sequence - 1);
        return command.ExecuteScalar() as byte[];
    }

    public string QuickCheck()
    {
        using var connection = OpenConnection(readOnly: true, busyTimeoutMilliseconds: 5_000);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "unknown";
    }

    public EventRecord? GetEvent(string eventId)
    {
        using var connection = OpenConnection(readOnly: true, busyTimeoutMilliseconds: 5_000);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM events WHERE event_id = $event_id;";
        command.Parameters.AddWithValue("$event_id", eventId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadEvent(reader) : null;
    }

    public int PruneTerminal(DateTimeOffset nowUtc)
    {
        using var connection = OpenConnection(readOnly: false, busyTimeoutMilliseconds: 5_000);
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM events
            WHERE (state = 'sent' AND completed_at_utc < $sent_before)
               OR (state IN ('shadow', 'suppressed') AND completed_at_utc < $short_before);
            """;
        command.Parameters.AddWithValue("$sent_before", Format(nowUtc - TimeSpan.FromDays(30)));
        command.Parameters.AddWithValue("$short_before", Format(nowUtc - TimeSpan.FromDays(7)));
        return command.ExecuteNonQuery();
    }

    private void Complete(string eventId, EventState state, DateTimeOffset completedUtc, byte[]? envelope, string? errorCode)
    {
        using var connection = OpenConnection(readOnly: false, busyTimeoutMilliseconds: 5_000);
        using var transaction = connection.BeginTransaction(deferred: false);
        Complete(connection, transaction, eventId, state, completedUtc, envelope, errorCode);
        transaction.Commit();
    }

    private static void Complete(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string eventId,
        EventState state,
        DateTimeOffset completedUtc,
        byte[]? envelope,
        string? errorCode,
        bool preserveEnvelope = false)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE events
            SET state = $state, lease_until_utc = NULL, completed_at_utc = $completed,
                last_error_code = $error{(preserveEnvelope ? string.Empty : ", delivery_envelope_dpapi = $envelope")}
            WHERE event_id = $event_id AND state = 'inflight';
            """;
        command.Parameters.AddWithValue("$event_id", eventId);
        command.Parameters.AddWithValue("$state", ToDatabase(state));
        command.Parameters.AddWithValue("$completed", Format(completedUtc));
        command.Parameters.AddWithValue("$error", (object?)errorCode ?? DBNull.Value);
        if (!preserveEnvelope)
        {
            command.Parameters.Add("$envelope", SqliteType.Blob).Value = (object?)envelope ?? DBNull.Value;
        }

        EnsureOne(command.ExecuteNonQuery(), eventId);
    }

    private void TransitionInflight(string eventId, string setClause, Action<SqliteCommand> addParameters)
    {
        using var connection = OpenConnection(readOnly: false, busyTimeoutMilliseconds: 5_000);
        using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE events SET {setClause} WHERE event_id = $event_id AND state = 'inflight';";
        command.Parameters.AddWithValue("$event_id", eventId);
        addParameters(command);
        EnsureOne(command.ExecuteNonQuery(), eventId);
    }

    private SqliteConnection OpenConnection(bool readOnly, int busyTimeoutMilliseconds)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = Math.Max(1, (int)Math.Ceiling(busyTimeoutMilliseconds / 1000d)),
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA busy_timeout={busyTimeoutMilliseconds};";
        command.ExecuteNonQuery();
        return connection;
    }

    private static EventRecord ReadEvent(SqliteDataReader reader)
    {
        var item = new EventRecord(
            reader.GetString(reader.GetOrdinal("event_id")),
            reader.GetString(reader.GetOrdinal("machine_id")),
            reader.GetString(reader.GetOrdinal("thread_id")),
            reader.GetString(reader.GetOrdinal("turn_id")),
            Parse(reader.GetString(reader.GetOrdinal("observed_at_utc"))),
            ParseCaptureMode(reader.GetString(reader.GetOrdinal("ingest_mode"))),
            ParseState(reader.GetString(reader.GetOrdinal("state"))),
            reader.GetInt32(reader.GetOrdinal("resolution_attempt_count")),
            reader.GetInt32(reader.GetOrdinal("delivery_attempt_count")),
            Parse(reader.GetString(reader.GetOrdinal("next_attempt_at_utc"))),
            ReadNullableTimestamp(reader, "lease_until_utc"),
            ReadNullableString(reader, "last_error_code"),
            ReadNullableTimestamp(reader, "completed_at_utc"),
            ReadNullableBlob(reader, "delivery_envelope_dpapi"));
        if (!Guid.TryParseExact(item.MachineId, "D", out _) ||
            item.EventId is not { Length: 64 } || !item.EventId.All(Uri.IsHexDigit) ||
            !NotifyPayloadParser.IsValidOpaqueId(item.ThreadId) ||
            !NotifyPayloadParser.IsValidOpaqueId(item.TurnId) ||
            !string.Equals(Hashing.EventId(item.MachineId, item.ThreadId, item.TurnId), item.EventId, StringComparison.Ordinal) ||
            item.ResolutionAttemptCount < 0 || item.DeliveryAttemptCount < 0)
        {
            throw new InvalidDataException("Stored event failed integrity validation.");
        }

        return item;
    }

    private static void UpsertHealth(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string conditionCode,
        DateTimeOffset observedUtc,
        DateTimeOffset? notBeforeUtc)
    {
        conditionCode = ValidateHealthCode(conditionCode);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO health_conditions (condition_code, first_observed_utc, last_observed_utc, not_before_utc)
            VALUES ($code, $observed, $observed, $not_before)
            ON CONFLICT(condition_code) DO UPDATE SET
                last_observed_utc = excluded.last_observed_utc,
                not_before_utc = CASE
                    WHEN excluded.not_before_utc IS NULL THEN health_conditions.not_before_utc
                    WHEN health_conditions.not_before_utc IS NULL THEN excluded.not_before_utc
                    WHEN excluded.not_before_utc > health_conditions.not_before_utc THEN excluded.not_before_utc
                    ELSE health_conditions.not_before_utc
                END;
            """;
        command.Parameters.AddWithValue("$code", conditionCode);
        command.Parameters.AddWithValue("$observed", Format(observedUtc));
        command.Parameters.AddWithValue("$not_before", notBeforeUtc is null ? DBNull.Value : Format(notBeforeUtc.Value));
        command.ExecuteNonQuery();
    }

    private static void DeleteHealth(SqliteConnection connection, SqliteTransaction transaction, string conditionCode)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM health_conditions WHERE condition_code = $code;";
        command.Parameters.AddWithValue("$code", conditionCode);
        command.ExecuteNonQuery();
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static int ReadUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static bool HasUserTables(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%');";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
    }

    private static void ValidateSchema(SqliteConnection connection)
    {
        ValidateColumns(connection, "events",
        [
            "event_id", "machine_id", "thread_id", "turn_id", "observed_at_utc", "ingest_mode", "state",
            "resolution_attempt_count", "delivery_attempt_count", "next_attempt_at_utc", "lease_until_utc",
            "last_error_code", "completed_at_utc", "delivery_envelope_dpapi",
        ]);
        ValidateColumns(connection, "health_conditions",
        [
            "condition_code", "first_observed_utc", "last_observed_utc", "not_before_utc",
        ]);
    }

    private static void ValidateColumns(SqliteConnection connection, string table, IEnumerable<string> expectedColumns)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        using var reader = command.ExecuteReader();
        var actual = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            actual.Add(reader.GetString(reader.GetOrdinal("name")));
        }

        if (!actual.SetEquals(expectedColumns))
        {
            throw new InvalidDataException($"Bridge database table {table} has an unsupported schema.");
        }
    }

    private static void ValidateMinimalEvent(MinimalEvent item)
    {
        if (item.SchemaVersion != BridgeConstants.SchemaVersion ||
            !Guid.TryParseExact(item.MachineId, "D", out _) ||
            item.EventId is not { Length: 64 } ||
            !item.EventId.All(Uri.IsHexDigit) ||
            !NotifyPayloadParser.IsValidOpaqueId(item.ThreadId) ||
            !NotifyPayloadParser.IsValidOpaqueId(item.TurnId) ||
            !string.Equals(Hashing.EventId(item.MachineId, item.ThreadId, item.TurnId), item.EventId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Minimal event failed validation.");
        }
    }

    private static string ValidateHealthCode(string value)
    {
        if (!HealthCodes.All.Contains(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return value;
    }

    private static string ValidateCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80 || value.Any(character => !(character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return value;
    }

    private static void EnsureOne(int affected, string eventId)
    {
        if (affected != 1)
        {
            throw new InvalidOperationException($"Event transition rejected for {eventId[..Math.Min(eventId.Length, BridgeConstants.EventIdLogPrefixLength)]}.");
        }
    }

    private static bool IsBusy(SqliteException exception) => exception.SqliteErrorCode is 5 or 6;

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) => DateTimeOffset.ParseExact(value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string ToDatabase(CaptureMode value) => value switch
    {
        CaptureMode.Shadow => "shadow",
        CaptureMode.Live => "live",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string ToDatabase(EventState value) => value switch
    {
        EventState.Pending => "pending",
        EventState.Inflight => "inflight",
        EventState.Sent => "sent",
        EventState.Shadow => "shadow",
        EventState.Suppressed => "suppressed",
        EventState.Quarantine => "quarantine",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static CaptureMode ParseCaptureMode(string value) => value switch
    {
        "shadow" => CaptureMode.Shadow,
        "live" => CaptureMode.Live,
        _ => throw new InvalidDataException("Invalid capture mode in database."),
    };

    private static EventState ParseState(string value) => value switch
    {
        "pending" => EventState.Pending,
        "inflight" => EventState.Inflight,
        "sent" => EventState.Sent,
        "shadow" => EventState.Shadow,
        "suppressed" => EventState.Suppressed,
        "quarantine" => EventState.Quarantine,
        _ => throw new InvalidDataException("Invalid event state in database."),
    };

    private static DateTimeOffset? ReadNullableTimestamp(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : Parse(reader.GetString(ordinal));
    }

    private static string? ReadNullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static byte[]? ReadNullableBlob(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : (byte[])reader.GetValue(ordinal);
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS events (
            event_id TEXT PRIMARY KEY CHECK(length(event_id) = 64),
            machine_id TEXT NOT NULL,
            thread_id TEXT NOT NULL,
            turn_id TEXT NOT NULL,
            observed_at_utc TEXT NOT NULL,
            ingest_mode TEXT NOT NULL CHECK(ingest_mode IN ('shadow', 'live')),
            state TEXT NOT NULL CHECK(state IN ('pending', 'inflight', 'sent', 'shadow', 'suppressed', 'quarantine')),
            resolution_attempt_count INTEGER NOT NULL CHECK(resolution_attempt_count >= 0),
            delivery_attempt_count INTEGER NOT NULL CHECK(delivery_attempt_count >= 0),
            next_attempt_at_utc TEXT NOT NULL,
            lease_until_utc TEXT NULL,
            last_error_code TEXT NULL,
            completed_at_utc TEXT NULL,
            delivery_envelope_dpapi BLOB NULL
        );
        CREATE INDEX IF NOT EXISTS ix_events_due ON events(state, next_attempt_at_utc, observed_at_utc);
        CREATE TABLE IF NOT EXISTS health_conditions (
            condition_code TEXT PRIMARY KEY,
            first_observed_utc TEXT NOT NULL,
            last_observed_utc TEXT NOT NULL,
            not_before_utc TEXT NULL
        );
        """;
}
