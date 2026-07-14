using CodexTelegramCommon;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace CodexTelegramIntegrationTests;

public sealed class CodexStateResolverIntegrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "CodexStateResolverTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolves_user_root_and_normalizes_title()
    {
        var id = Guid.NewGuid().ToString("D");
        CreateDatabase(5, compatible: true, (id, "  제목\n둘  ", "{}", "user"));

        var result = new CodexStateResolver(root).Resolve(id);

        Assert.Equal(ResolutionKind.RootReady, result.Kind);
        Assert.Equal("제목 둘", result.NormalizedTitle);
    }

    [Fact]
    public void Resolves_legacy_vscode_root()
    {
        var id = Guid.NewGuid().ToString("D");
        CreateDatabase(5, compatible: true, (id, "Legacy", "vscode", null));

        Assert.Equal(ResolutionKind.RootReady, new CodexStateResolver(root).Resolve(id).Kind);
    }

    [Theory]
    [InlineData("subagent", "vscode")]
    [InlineData("user", "{\"subagent\":{}}")]
    public void Subagent_signal_has_precedence(string? threadSource, string source)
    {
        var id = Guid.NewGuid().ToString("D");
        CreateDatabase(5, compatible: true, (id, "Internal", source, threadSource));

        Assert.Equal(ResolutionKind.Subagent, new CodexStateResolver(root).Resolve(id).Kind);
    }

    [Fact]
    public void Unknown_source_is_quarantined()
    {
        var id = Guid.NewGuid().ToString("D");
        CreateDatabase(5, compatible: true, (id, "Unknown", "new-client", null));

        Assert.Equal(ResolutionKind.Unsupported, new CodexStateResolver(root).Resolve(id).Kind);
    }

    [Fact]
    public void Blank_title_is_retryable()
    {
        var id = Guid.NewGuid().ToString("D");
        CreateDatabase(5, compatible: true, (id, "\r\n", "{}", "user"));

        Assert.Equal(ResolutionKind.NotReady, new CodexStateResolver(root).Resolve(id).Kind);
    }

    [Fact]
    public void Incompatible_newer_database_blocks_stale_older_match()
    {
        var id = Guid.NewGuid().ToString("D");
        CreateDatabase(5, compatible: true, (id, "Old title", "{}", "user"));
        CreateDatabase(6, compatible: false);

        var result = new CodexStateResolver(root).Resolve(id);

        Assert.Equal(ResolutionKind.Unsupported, result.Kind);
        Assert.Equal("STATE_SCHEMA_UNSUPPORTED", result.ErrorCode);
    }

    [Fact]
    public void Reads_wal_visible_uncheckpointed_row()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "state_5.sqlite");
        using var writer = OpenWritable(path);
        CreateSchema(writer, compatible: true);
        using var wal = writer.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;";
        wal.ExecuteNonQuery();
        var id = Guid.NewGuid().ToString("D");
        Insert(writer, id, "WAL title", "{}", "user");

        var result = new CodexStateResolver(root).Resolve(id);

        Assert.Equal(ResolutionKind.RootReady, result.Kind);
        Assert.Equal("WAL title", result.NormalizedTitle);
    }

    [Fact]
    public void Latest_session_index_name_overrides_stale_database_after_resolver_restart()
    {
        var id = Guid.NewGuid().ToString("D");
        CreateDatabase(5, compatible: true, (id, "test", "{}", "user"));
        WriteSessionIndex(
            (id, "General test", "2026-07-14T00:00:00Z"),
            (Guid.NewGuid().ToString("D"), "Different thread", "2026-07-14T00:00:01Z"),
            (id, "Codex Telegram Bridge PC2 설정 검증", "2026-07-14T00:00:02Z"));

        var beforeRestart = new CodexStateResolver(root).Resolve(id);
        var afterRestart = new CodexStateResolver(root).Resolve(id);

        Assert.Equal(ResolutionKind.RootReady, beforeRestart.Kind);
        Assert.Equal("Codex Telegram Bridge PC2 설정 검증", beforeRestart.NormalizedTitle);
        Assert.Equal(beforeRestart, afterRestart);
        Assert.True(new CodexStateResolver(root).HasCompatibleTitleIndex());
    }

    [Fact]
    public void Missing_session_index_name_for_thread_falls_back_to_database_title()
    {
        var id = Guid.NewGuid().ToString("D");
        CreateDatabase(5, compatible: true, (id, "Database title", "{}", "user"));
        WriteSessionIndex((Guid.NewGuid().ToString("D"), "Different thread", "2026-07-14T00:00:00Z"));

        var result = new CodexStateResolver(root).Resolve(id);

        Assert.Equal(ResolutionKind.RootReady, result.Kind);
        Assert.Equal("Database title", result.NormalizedTitle);
    }

    [Fact]
    public void Blank_latest_session_index_name_does_not_fall_back_to_stale_database_title()
    {
        var id = Guid.NewGuid().ToString("D");
        CreateDatabase(5, compatible: true, (id, "Stale title", "{}", "user"));
        WriteSessionIndex((id, "Previous name", "2026-07-14T00:00:00Z"), (id, "\r\n", "2026-07-14T00:00:01Z"));

        var result = new CodexStateResolver(root).Resolve(id);

        Assert.Equal(ResolutionKind.NotReady, result.Kind);
        Assert.Equal("THREAD_TITLE_NOT_READY", result.ErrorCode);
    }

    [Fact]
    public void Malformed_session_index_rows_are_skipped_like_codex_reverse_lookup()
    {
        var id = Guid.NewGuid().ToString("D");
        CreateDatabase(5, compatible: true, (id, "Stale title", "{}", "user"));
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "session_index.jsonl"),
            string.Join('\n',
                JsonSerializer.Serialize(new { id, thread_name = "Expected name", updated_at = "2026-07-14T00:00:00Z" }),
                "not-json",
                "{\"id\":\"unterminated\""));

        var result = new CodexStateResolver(root).Resolve(id);

        Assert.Equal(ResolutionKind.RootReady, result.Kind);
        Assert.Equal("Expected name", result.NormalizedTitle);
    }

    [Fact]
    public void Valid_final_session_index_row_does_not_require_trailing_newline()
    {
        var id = Guid.NewGuid().ToString("D");
        CreateDatabase(5, compatible: true, (id, "Stale title", "{}", "user"));
        WriteSessionIndex(appendFinalNewline: false, (id, "EOF title", "2026-07-14T00:00:00Z"));

        var result = new CodexStateResolver(root).Resolve(id);

        Assert.Equal(ResolutionKind.RootReady, result.Kind);
        Assert.Equal("EOF title", result.NormalizedTitle);
    }

    [Fact]
    public void Desktop_description_metadata_is_not_treated_as_the_task_title()
    {
        var id = Guid.NewGuid().ToString("D");
        CreateDatabase(5, compatible: true, (id, "Database title", "{}", "user"));
        WriteAppDescription(id, "General test");

        var result = new CodexStateResolver(root).Resolve(id);

        Assert.Equal(ResolutionKind.RootReady, result.Kind);
        Assert.Equal("Database title", result.NormalizedTitle);
    }

    [Fact]
    public void Utf8_bom_and_crlf_session_index_are_supported()
    {
        var id = Guid.NewGuid().ToString("D");
        CreateDatabase(5, compatible: true, (id, "Database title", "{}", "user"));
        Directory.CreateDirectory(root);
        var line = JsonSerializer.Serialize(new { id, thread_name = "한글 제목", updated_at = "2026-07-14T00:00:00Z" });
        File.WriteAllText(Path.Combine(root, "session_index.jsonl"), line + "\r\n", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var result = new CodexStateResolver(root).Resolve(id);

        Assert.Equal(ResolutionKind.RootReady, result.Kind);
        Assert.Equal("한글 제목", result.NormalizedTitle);
    }

    [Fact]
    public void Locked_session_index_retries_instead_of_using_the_stale_database_title()
    {
        var id = Guid.NewGuid().ToString("D");
        CreateDatabase(5, compatible: true, (id, "Stale title", "{}", "user"));
        WriteSessionIndex((id, "Current title", "2026-07-14T00:00:00Z"));
        using var locked = new FileStream(
            Path.Combine(root, "session_index.jsonl"),
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var result = new CodexStateResolver(root).Resolve(id);

        Assert.Equal(ResolutionKind.NotReady, result.Kind);
        Assert.Equal("SESSION_INDEX_RETRY", result.ErrorCode);
        Assert.False(new CodexStateResolver(root).HasCompatibleTitleIndex());
    }

    [Fact]
    public void Oversized_session_index_is_rejected_instead_of_being_loaded()
    {
        var id = Guid.NewGuid().ToString("D");
        CreateDatabase(5, compatible: true, (id, "Stale title", "{}", "user"));
        Directory.CreateDirectory(root);
        using (var oversized = new FileStream(
                   Path.Combine(root, "session_index.jsonl"),
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None))
        {
            oversized.SetLength((64L * 1024 * 1024) + 1);
        }

        var result = new CodexStateResolver(root).Resolve(id);

        Assert.Equal(ResolutionKind.Unsupported, result.Kind);
        Assert.Equal("SESSION_INDEX_TOO_LARGE", result.ErrorCode);
        Assert.False(new CodexStateResolver(root).HasCompatibleTitleIndex());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private void CreateDatabase(int suffix, bool compatible, params (string Id, string? Title, string? Source, string? ThreadSource)[] rows)
    {
        Directory.CreateDirectory(root);
        using var connection = OpenWritable(Path.Combine(root, $"state_{suffix}.sqlite"));
        CreateSchema(connection, compatible);
        if (!compatible)
        {
            return;
        }

        foreach (var row in rows)
        {
            Insert(connection, row.Id, row.Title, row.Source, row.ThreadSource);
        }
    }

    private static SqliteConnection OpenWritable(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void CreateSchema(SqliteConnection connection, bool compatible)
    {
        using var command = connection.CreateCommand();
        command.CommandText = compatible
            ? "CREATE TABLE threads (id TEXT PRIMARY KEY, title TEXT, source TEXT, thread_source TEXT);"
            : "CREATE TABLE threads (id TEXT PRIMARY KEY, title TEXT);";
        command.ExecuteNonQuery();
    }

    private static void Insert(SqliteConnection connection, string id, string? title, string? source, string? threadSource)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO threads (id, title, source, thread_source) VALUES ($id, $title, $source, $thread_source);";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$title", (object?)title ?? DBNull.Value);
        command.Parameters.AddWithValue("$source", (object?)source ?? DBNull.Value);
        command.Parameters.AddWithValue("$thread_source", (object?)threadSource ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private void WriteSessionIndex(params (string Id, string Title, string UpdatedAt)[] entries) =>
        WriteSessionIndex(appendFinalNewline: true, entries);

    private void WriteSessionIndex(bool appendFinalNewline, params (string Id, string Title, string UpdatedAt)[] entries)
    {
        Directory.CreateDirectory(root);
        var lines = entries.Select(entry => JsonSerializer.Serialize(new
        {
            id = entry.Id,
            thread_name = entry.Title,
            updated_at = entry.UpdatedAt,
        }));
        var content = string.Join('\n', lines) + (appendFinalNewline ? "\n" : string.Empty);
        File.WriteAllText(Path.Combine(root, "session_index.jsonl"), content, new System.Text.UTF8Encoding(false));
    }

    private void WriteAppDescription(string id, string description)
    {
        Directory.CreateDirectory(root);
        var state = new Dictionary<string, object?>
        {
            ["unrelated"] = new { nested = new[] { 1, 2, 3 } },
            ["electron-persisted-atom-state"] = new Dictionary<string, object?>
            {
                ["unrelated-atom"] = true,
                ["thread-descriptions-v1"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [id] = description,
                },
            },
        };
        File.WriteAllText(
            Path.Combine(root, ".codex-global-state.json"),
            JsonSerializer.Serialize(state));
    }
}
