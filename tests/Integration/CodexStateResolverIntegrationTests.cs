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
    public void Persisted_app_title_overrides_stale_database_after_resolver_restart()
    {
        var id = Guid.NewGuid().ToString("D");
        CreateDatabase(5, compatible: true, (id, "test", "{}", "user"));
        WriteAppMetadata((id, "Codex Telegram Bridge PC2 설정 검증"));

        var beforeRestart = new CodexStateResolver(root).Resolve(id);
        var afterRestart = new CodexStateResolver(root).Resolve(id);

        Assert.Equal(ResolutionKind.RootReady, beforeRestart.Kind);
        Assert.Equal("Codex Telegram Bridge PC2 설정 검증", beforeRestart.NormalizedTitle);
        Assert.Equal(beforeRestart, afterRestart);
    }

    [Fact]
    public void Missing_app_title_for_thread_falls_back_to_database_title()
    {
        var id = Guid.NewGuid().ToString("D");
        CreateDatabase(5, compatible: true, (id, "Database title", "{}", "user"));
        WriteAppMetadata((Guid.NewGuid().ToString("D"), "Different thread"));

        var result = new CodexStateResolver(root).Resolve(id);

        Assert.Equal(ResolutionKind.RootReady, result.Kind);
        Assert.Equal("Database title", result.NormalizedTitle);
    }

    [Fact]
    public void Blank_persisted_app_title_does_not_fall_back_to_stale_database_title()
    {
        var id = Guid.NewGuid().ToString("D");
        CreateDatabase(5, compatible: true, (id, "Stale title", "{}", "user"));
        WriteAppMetadata((id, "\r\n"));

        var result = new CodexStateResolver(root).Resolve(id);

        Assert.Equal(ResolutionKind.NotReady, result.Kind);
        Assert.Equal("THREAD_TITLE_NOT_READY", result.ErrorCode);
    }

    [Fact]
    public void Malformed_app_metadata_retries_instead_of_using_stale_database_title()
    {
        var id = Guid.NewGuid().ToString("D");
        CreateDatabase(5, compatible: true, (id, "Stale title", "{}", "user"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".codex-global-state.json"), "{\"electron-persisted-atom-state\":");

        var result = new CodexStateResolver(root).Resolve(id);

        Assert.Equal(ResolutionKind.NotReady, result.Kind);
        Assert.Equal("APP_METADATA_RETRY", result.ErrorCode);
    }

    [Fact]
    public void Temporarily_missing_app_metadata_with_backup_retries_instead_of_using_stale_database_title()
    {
        var id = Guid.NewGuid().ToString("D");
        CreateDatabase(5, compatible: true, (id, "Stale title", "{}", "user"));
        File.WriteAllText(Path.Combine(root, ".codex-global-state.json.bak"), "{}");

        var result = new CodexStateResolver(root).Resolve(id);

        Assert.Equal(ResolutionKind.NotReady, result.Kind);
        Assert.Equal("APP_METADATA_RETRY", result.ErrorCode);
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

    private void WriteAppMetadata(params (string Id, string Title)[] descriptions)
    {
        Directory.CreateDirectory(root);
        var values = descriptions.ToDictionary(item => item.Id, item => item.Title, StringComparer.Ordinal);
        var state = new Dictionary<string, object?>
        {
            ["unrelated"] = new { nested = new[] { 1, 2, 3 } },
            ["electron-persisted-atom-state"] = new Dictionary<string, object?>
            {
                ["unrelated-atom"] = true,
                ["thread-descriptions-v1"] = values,
            },
        };
        File.WriteAllText(
            Path.Combine(root, ".codex-global-state.json"),
            JsonSerializer.Serialize(state));
    }
}
