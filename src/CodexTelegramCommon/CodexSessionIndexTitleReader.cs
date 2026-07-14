using System.Buffers;
using System.Text.Json;

namespace CodexTelegramCommon;

internal static class CodexSessionIndexTitleReader
{
    private const string SessionIndexFileName = "session_index.jsonl";
    private const int MaxSessionIndexBytes = 64 * 1024 * 1024;
    private static ReadOnlySpan<byte> Utf8Bom => [0xef, 0xbb, 0xbf];

    public static SessionIndexTitleResult Read(string codexHome, string threadId)
    {
        var path = Path.Combine(codexHome, SessionIndexFileName);
        byte[]? buffer = null;
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 16 * 1024,
                FileOptions.SequentialScan);

            var initialPathState = ReadPathState(path);
            var length = stream.Length;
            if (!initialPathState.Exists || initialPathState.Length != length)
            {
                return SessionIndexTitleResult.Retry("SESSION_INDEX_RETRY");
            }

            if (length > MaxSessionIndexBytes)
            {
                return SessionIndexTitleResult.Unsupported("SESSION_INDEX_TOO_LARGE");
            }

            if (length == 0)
            {
                return IsStablePath(path, initialPathState)
                    ? SessionIndexTitleResult.Absent()
                    : SessionIndexTitleResult.Retry("SESSION_INDEX_RETRY");
            }

            var byteCount = checked((int)length);
            buffer = ArrayPool<byte>.Shared.Rent(byteCount);
            var offset = 0;
            while (offset < byteCount)
            {
                var read = stream.Read(buffer, offset, byteCount - offset);
                if (read == 0)
                {
                    return SessionIndexTitleResult.Retry("SESSION_INDEX_RETRY");
                }

                offset += read;
            }

            if (stream.ReadByte() != -1 || !IsStablePath(path, initialPathState))
            {
                return SessionIndexTitleResult.Retry("SESSION_INDEX_RETRY");
            }

            return Parse(buffer.AsSpan(0, byteCount), threadId);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return SessionIndexTitleResult.Absent();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return SessionIndexTitleResult.Retry("SESSION_INDEX_RETRY");
        }
        finally
        {
            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }
    }

    private static SessionIndexTitleResult Parse(ReadOnlySpan<byte> jsonLines, string threadId)
    {
        var end = jsonLines.Length;
        while (end > 0)
        {
            var previousNewline = jsonLines[..end].LastIndexOf((byte)'\n');
            var start = previousNewline + 1;
            var line = TrimAsciiWhitespace(jsonLines[start..end]);
            if (start == 0 && line.StartsWith(Utf8Bom))
            {
                line = line[Utf8Bom.Length..];
            }

            if (!line.IsEmpty && TryReadMatchingTitle(line, threadId, out var title))
            {
                return SessionIndexTitleResult.Present(title);
            }

            if (start == 0)
            {
                break;
            }

            end = previousNewline;
        }

        return SessionIndexTitleResult.Absent();
    }

    private static bool TryReadMatchingTitle(ReadOnlySpan<byte> json, string threadId, out string? title)
    {
        title = null;
        try
        {
            var reader = CreateReader(json);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            var idSeen = false;
            var idMatches = false;
            var titleSeen = false;
            var updatedAtSeen = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    if (reader.Read() || !idSeen || !titleSeen || !updatedAtSeen || !idMatches)
                    {
                        return false;
                    }

                    title = ExtractThreadName(json);
                    return title is not null;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    return false;
                }

                var isId = reader.ValueTextEquals("id");
                var isTitle = reader.ValueTextEquals("thread_name");
                var isUpdatedAt = reader.ValueTextEquals("updated_at");
                if (!reader.Read())
                {
                    return false;
                }

                if (isId)
                {
                    if (idSeen || reader.TokenType != JsonTokenType.String)
                    {
                        return false;
                    }

                    idSeen = true;
                    idMatches = reader.ValueTextEquals(threadId);
                }
                else if (isTitle)
                {
                    if (titleSeen || reader.TokenType != JsonTokenType.String)
                    {
                        return false;
                    }

                    titleSeen = true;
                }
                else if (isUpdatedAt)
                {
                    if (updatedAtSeen || reader.TokenType != JsonTokenType.String)
                    {
                        return false;
                    }

                    updatedAtSeen = true;
                }
                else
                {
                    reader.Skip();
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static string? ExtractThreadName(ReadOnlySpan<byte> json)
    {
        var reader = CreateReader(json);
        if (!reader.Read())
        {
            return null;
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return null;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                return null;
            }

            var isTitle = reader.ValueTextEquals("thread_name");
            if (!reader.Read())
            {
                return null;
            }

            if (isTitle)
            {
                return reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            }

            reader.Skip();
        }

        return null;
    }

    private static Utf8JsonReader CreateReader(ReadOnlySpan<byte> json) => new(json, new JsonReaderOptions
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 16,
    });

    private static ReadOnlySpan<byte> TrimAsciiWhitespace(ReadOnlySpan<byte> value)
    {
        var start = 0;
        while (start < value.Length && IsAsciiWhitespace(value[start]))
        {
            start++;
        }

        var end = value.Length;
        while (end > start && IsAsciiWhitespace(value[end - 1]))
        {
            end--;
        }

        return value[start..end];
    }

    private static bool IsAsciiWhitespace(byte value) => value is (byte)' ' or (byte)'\t' or (byte)'\r';

    private static PathState ReadPathState(string path)
    {
        var info = new FileInfo(path);
        info.Refresh();
        return new PathState(info.Exists, info.Exists ? info.Length : 0, info.Exists ? info.LastWriteTimeUtc : default);
    }

    private static bool IsStablePath(string path, PathState expected)
    {
        var current = ReadPathState(path);
        return current == expected;
    }

    private sealed record PathState(bool Exists, long Length, DateTime LastWriteTimeUtc);
}

internal enum SessionIndexTitleKind
{
    Absent,
    Present,
    Retry,
    Unsupported,
}

internal sealed record SessionIndexTitleResult(SessionIndexTitleKind Kind, string? Title = null, string? ErrorCode = null)
{
    public static SessionIndexTitleResult Absent() => new(SessionIndexTitleKind.Absent);

    public static SessionIndexTitleResult Present(string? title) => new(SessionIndexTitleKind.Present, title);

    public static SessionIndexTitleResult Retry(string errorCode) => new(SessionIndexTitleKind.Retry, ErrorCode: errorCode);

    public static SessionIndexTitleResult Unsupported(string errorCode) => new(SessionIndexTitleKind.Unsupported, ErrorCode: errorCode);
}
