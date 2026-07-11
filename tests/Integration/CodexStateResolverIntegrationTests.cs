using CodexTelegramCommon;
using Microsoft.Data.Sqlite;

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
}
