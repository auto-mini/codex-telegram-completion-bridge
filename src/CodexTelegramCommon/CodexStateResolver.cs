using System.Text.Json;
using System.Text.RegularExpressions;
using System.Numerics;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace CodexTelegramCommon;

public interface IStateResolver
{
    StateResolution Resolve(string threadId);
}

public sealed partial class CodexStateResolver(string codexHome) : IStateResolver
{
    private static readonly HashSet<string> RequiredColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "id",
        "title",
        "source",
        "thread_source",
    };

    public string CodexHome { get; } = Path.GetFullPath(codexHome);

    public StateResolution Resolve(string threadId)
    {
        if (!NotifyPayloadParser.IsValidOpaqueId(threadId))
        {
            return new StateResolution(ResolutionKind.Unsupported, ErrorCode: "THREAD_ID_INVALID");
        }

        if (!Directory.Exists(CodexHome))
        {
            return new StateResolution(ResolutionKind.NotReady, ErrorCode: "CODEX_HOME_NOT_READY");
        }

        IReadOnlyList<StateDatabaseCandidate> candidates;
        try
        {
            candidates = EnumerateCandidates();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new StateResolution(ResolutionKind.NotReady, ErrorCode: "STATE_DISCOVERY_RETRY");
        }

        foreach (var candidate in candidates)
        {
            try
            {
                using var connection = OpenReadOnly(candidate.Path);
                if (!HasCompatibleSchema(connection))
                {
                    return new StateResolution(ResolutionKind.Unsupported, ErrorCode: "STATE_SCHEMA_UNSUPPORTED");
                }

                var row = QueryThread(connection, threadId);
                if (row is null)
                {
                    continue;
                }

                var sourceClassification = ClassifySource(row);
                if (sourceClassification is not null)
                {
                    return sourceClassification;
                }

                var sessionIndex = CodexSessionIndexTitleReader.Read(CodexHome, threadId);
                if (sessionIndex.Kind == SessionIndexTitleKind.Retry)
                {
                    return new StateResolution(ResolutionKind.NotReady, ErrorCode: sessionIndex.ErrorCode);
                }

                if (sessionIndex.Kind == SessionIndexTitleKind.Unsupported)
                {
                    return new StateResolution(ResolutionKind.Unsupported, ErrorCode: sessionIndex.ErrorCode);
                }

                var title = TextNormalizer.NormalizeTitle(
                    sessionIndex.Kind == SessionIndexTitleKind.Present ? sessionIndex.Title : row.Title);
                return title is null
                    ? new StateResolution(ResolutionKind.NotReady, ErrorCode: "THREAD_TITLE_NOT_READY")
                    : new StateResolution(ResolutionKind.RootReady, title);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
            {
                return new StateResolution(ResolutionKind.NotReady, ErrorCode: "STATE_DATABASE_BUSY");
            }
            catch (SqliteException)
            {
                return new StateResolution(ResolutionKind.Unsupported, ErrorCode: "STATE_DATABASE_ERROR");
            }
            catch (IOException)
            {
                return new StateResolution(ResolutionKind.NotReady, ErrorCode: "STATE_DATABASE_IO_RETRY");
            }
            catch (Exception exception) when (exception is InvalidCastException or ArgumentException)
            {
                return new StateResolution(ResolutionKind.Unsupported, ErrorCode: "STATE_ROW_MALFORMED");
            }
        }

        return new StateResolution(ResolutionKind.NotReady, ErrorCode: "THREAD_NOT_PERSISTED");
    }

    public bool HasAnyCompatibleDatabase()
    {
        try
        {
            return EnumerateCandidates().Any(candidate =>
            {
                try
                {
                    using var connection = OpenReadOnly(candidate.Path);
                    return HasCompatibleSchema(connection);
                }
                catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
                {
                    return false;
                }
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool HasCompatibleTitleIndex()
    {
        if (!Directory.Exists(CodexHome))
        {
            return false;
        }

        var result = CodexSessionIndexTitleReader.Read(CodexHome, Guid.Empty.ToString("D"));
        return result.Kind is SessionIndexTitleKind.Absent or SessionIndexTitleKind.Present;
    }

    internal IReadOnlyList<StateDatabaseCandidate> EnumerateCandidates()
    {
        if (!Directory.Exists(CodexHome))
        {
            return [];
        }

        return Directory.EnumerateFiles(CodexHome, "state_*.sqlite", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                var match = StateFileRegex().Match(Path.GetFileName(path));
                return match.Success && BigInteger.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var suffix)
                    ? new StateDatabaseCandidate(path, suffix, File.GetLastWriteTimeUtc(path))
                    : null;
            })
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .OrderByDescending(candidate => candidate.NumericSuffix)
            .ThenByDescending(candidate => candidate.LastWriteTimeUtc)
            .ToArray();
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 1,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=250;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static bool HasCompatibleSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(threads);";
        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return RequiredColumns.IsSubsetOf(columns);
    }

    private static ThreadRow? QueryThread(SqliteConnection connection, string threadId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT title, source, thread_source FROM threads WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", threadId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new ThreadRow(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private static StateResolution? ClassifySource(ThreadRow row)
    {
        var structuredSource = ParseStructuredSource(row.Source);
        if (structuredSource.Malformed)
        {
            return new StateResolution(ResolutionKind.Unsupported, ErrorCode: "THREAD_SOURCE_MALFORMED");
        }

        if (string.Equals(row.ThreadSource, "subagent", StringComparison.Ordinal) || structuredSource.HasSubagent)
        {
            return new StateResolution(ResolutionKind.Subagent, ErrorCode: "VERIFIED_SUBAGENT");
        }

        var isRoot = string.Equals(row.ThreadSource, "user", StringComparison.Ordinal) ||
                     row.ThreadSource is null && string.Equals(row.Source, BridgeConstants.LegacyRootSource, StringComparison.Ordinal);
        if (!isRoot)
        {
            return new StateResolution(ResolutionKind.Unsupported, ErrorCode: "THREAD_SOURCE_UNSUPPORTED");
        }

        return null;
    }

    private static StructuredSourceResult ParseStructuredSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source) || !source.TrimStart().StartsWith('{'))
        {
            return new StructuredSourceResult(false, false);
        }

        if (source.Length > 64 * 1024)
        {
            return new StructuredSourceResult(false, true);
        }

        try
        {
            using var document = JsonDocument.Parse(source);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? new StructuredSourceResult(document.RootElement.TryGetProperty("subagent", out _), false)
                : new StructuredSourceResult(false, true);
        }
        catch (JsonException)
        {
            return new StructuredSourceResult(false, true);
        }
    }

    [GeneratedRegex("^state_(\\d+)\\.sqlite$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex StateFileRegex();

    private sealed record ThreadRow(string? Title, string? Source, string? ThreadSource);

    private sealed record StructuredSourceResult(bool HasSubagent, bool Malformed);
}

public sealed record StateDatabaseCandidate(string Path, BigInteger NumericSuffix, DateTime LastWriteTimeUtc);
