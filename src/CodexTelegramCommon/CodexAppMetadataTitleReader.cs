using System.Buffers;
using System.Text.Json;

namespace CodexTelegramCommon;

internal static class CodexAppMetadataTitleReader
{
    private const string GlobalStateFileName = ".codex-global-state.json";
    private const string AtomStatePropertyName = "electron-persisted-atom-state";
    private const string ThreadDescriptionsPropertyName = "thread-descriptions-v1";
    private const int MaxGlobalStateBytes = 32 * 1024 * 1024;

    public static AppMetadataTitleResult Read(string codexHome, string threadId)
    {
        var path = Path.Combine(codexHome, GlobalStateFileName);
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
            var length = stream.Length;
            if (length <= 0)
            {
                return AppMetadataTitleResult.Retry("APP_METADATA_RETRY");
            }

            if (length > MaxGlobalStateBytes)
            {
                return AppMetadataTitleResult.Unsupported("APP_METADATA_TOO_LARGE");
            }

            var byteCount = checked((int)length);
            buffer = ArrayPool<byte>.Shared.Rent(byteCount);
            var offset = 0;
            while (offset < byteCount)
            {
                var read = stream.Read(buffer, offset, byteCount - offset);
                if (read == 0)
                {
                    return AppMetadataTitleResult.Retry("APP_METADATA_RETRY");
                }

                offset += read;
            }

            if (stream.ReadByte() != -1)
            {
                return AppMetadataTitleResult.Retry("APP_METADATA_RETRY");
            }

            return Parse(buffer.AsSpan(0, byteCount), threadId);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return File.Exists(path + ".bak")
                ? AppMetadataTitleResult.Retry("APP_METADATA_RETRY")
                : AppMetadataTitleResult.Absent();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return AppMetadataTitleResult.Retry("APP_METADATA_RETRY");
        }
        finally
        {
            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }
    }

    private static AppMetadataTitleResult Parse(ReadOnlySpan<byte> json, string threadId)
    {
        try
        {
            var reader = new Utf8JsonReader(json, new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64,
            });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return AppMetadataTitleResult.Retry("APP_METADATA_RETRY");
            }

            var atomStateSeen = false;
            var result = AppMetadataTitleResult.Absent();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return reader.Read()
                        ? AppMetadataTitleResult.Retry("APP_METADATA_RETRY")
                        : result;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    return AppMetadataTitleResult.Retry("APP_METADATA_RETRY");
                }

                var isAtomState = reader.ValueTextEquals(AtomStatePropertyName);
                if (!reader.Read())
                {
                    return AppMetadataTitleResult.Retry("APP_METADATA_RETRY");
                }

                if (!isAtomState)
                {
                    reader.Skip();
                    continue;
                }

                if (atomStateSeen || reader.TokenType != JsonTokenType.StartObject)
                {
                    return AppMetadataTitleResult.Retry("APP_METADATA_RETRY");
                }

                atomStateSeen = true;
                result = ParseAtomState(ref reader, threadId);
                if (result.Kind == AppMetadataTitleKind.Retry)
                {
                    return result;
                }
            }

            return AppMetadataTitleResult.Retry("APP_METADATA_RETRY");
        }
        catch (JsonException)
        {
            return AppMetadataTitleResult.Retry("APP_METADATA_RETRY");
        }
    }

    private static AppMetadataTitleResult ParseAtomState(ref Utf8JsonReader reader, string threadId)
    {
        var descriptionsSeen = false;
        var result = AppMetadataTitleResult.Absent();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return result;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                return AppMetadataTitleResult.Retry("APP_METADATA_RETRY");
            }

            var isDescriptions = reader.ValueTextEquals(ThreadDescriptionsPropertyName);
            if (!reader.Read())
            {
                return AppMetadataTitleResult.Retry("APP_METADATA_RETRY");
            }

            if (!isDescriptions)
            {
                reader.Skip();
                continue;
            }

            if (descriptionsSeen || reader.TokenType != JsonTokenType.StartObject)
            {
                return AppMetadataTitleResult.Retry("APP_METADATA_RETRY");
            }

            descriptionsSeen = true;
            result = ParseThreadDescriptions(ref reader, threadId);
            if (result.Kind == AppMetadataTitleKind.Retry)
            {
                return result;
            }
        }

        return AppMetadataTitleResult.Retry("APP_METADATA_RETRY");
    }

    private static AppMetadataTitleResult ParseThreadDescriptions(ref Utf8JsonReader reader, string threadId)
    {
        var threadSeen = false;
        string? title = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return threadSeen
                    ? AppMetadataTitleResult.Present(title)
                    : AppMetadataTitleResult.Absent();
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                return AppMetadataTitleResult.Retry("APP_METADATA_RETRY");
            }

            var isThread = reader.ValueTextEquals(threadId);
            if (!reader.Read())
            {
                return AppMetadataTitleResult.Retry("APP_METADATA_RETRY");
            }

            if (!isThread)
            {
                reader.Skip();
                continue;
            }

            if (threadSeen || reader.TokenType != JsonTokenType.String)
            {
                return AppMetadataTitleResult.Retry("APP_METADATA_RETRY");
            }

            threadSeen = true;
            title = reader.GetString();
        }

        return AppMetadataTitleResult.Retry("APP_METADATA_RETRY");
    }
}

internal enum AppMetadataTitleKind
{
    Absent,
    Present,
    Retry,
    Unsupported,
}

internal sealed record AppMetadataTitleResult(AppMetadataTitleKind Kind, string? Title = null, string? ErrorCode = null)
{
    public static AppMetadataTitleResult Absent() => new(AppMetadataTitleKind.Absent);

    public static AppMetadataTitleResult Present(string? title) => new(AppMetadataTitleKind.Present, title);

    public static AppMetadataTitleResult Retry(string errorCode) => new(AppMetadataTitleKind.Retry, ErrorCode: errorCode);

    public static AppMetadataTitleResult Unsupported(string errorCode) => new(AppMetadataTitleKind.Unsupported, ErrorCode: errorCode);
}
