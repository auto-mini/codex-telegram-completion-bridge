using System.Buffers;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace CodexTelegramCommon;

public sealed class TelegramBotClient : ITelegramBotClient
{
    private const string ApiOrigin = "https://api.telegram.org";
    private const int SetupResponseLimitBytes = 1024 * 1024;
    private readonly string token;
    private readonly HttpClient client;

    public TelegramBotClient(string token)
        : this(token, CreateProductionHandler())
    {
    }

    internal TelegramBotClient(string token, HttpMessageHandler handler)
    {
        this.token = ValidateToken(token);
        client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
    }

    public async Task<TelegramCallResult> SendCompletionAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        var result = await CallAsync(
            "sendMessage",
            new Dictionary<string, object?>
            {
                ["chat_id"] = chatId,
                ["text"] = text,
                ["disable_notification"] = false,
                ["protect_content"] = true,
            },
            BridgeConstants.RuntimeTelegramResponseLimitBytes,
            cancellationToken).ConfigureAwait(false);
        return result.Call;
    }

    public async Task<TelegramCallResult> SendSetupTestAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        var result = await CallAsync(
            "sendMessage",
            new Dictionary<string, object?>
            {
                ["chat_id"] = chatId,
                ["text"] = text,
                ["disable_notification"] = false,
                ["protect_content"] = true,
            },
            BridgeConstants.RuntimeTelegramResponseLimitBytes,
            cancellationToken).ConfigureAwait(false);
        return result.Call;
    }

    public async Task<TelegramValueResult<TelegramBotIdentity>> GetMeAsync(CancellationToken cancellationToken)
    {
        var response = await CallAsync("getMe", null, SetupResponseLimitBytes, cancellationToken).ConfigureAwait(false);
        if (!response.Call.IsSuccess || response.Result is not { ValueKind: JsonValueKind.Object } result ||
            !result.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id))
        {
            return new TelegramValueResult<TelegramBotIdentity>(
                response.Call.IsSuccess ? ProtocolFailure("GET_ME_RESULT_INVALID") : response.Call,
                null);
        }

        var username = result.TryGetProperty("username", out var usernameElement) && usernameElement.ValueKind == JsonValueKind.String
            ? usernameElement.GetString()
            : null;
        return new TelegramValueResult<TelegramBotIdentity>(response.Call, new TelegramBotIdentity(id, username));
    }

    public async Task<TelegramValueResult<TelegramChat>> GetChatAsync(long chatId, CancellationToken cancellationToken)
    {
        var response = await CallAsync(
            "getChat",
            new Dictionary<string, object?> { ["chat_id"] = chatId },
            SetupResponseLimitBytes,
            cancellationToken).ConfigureAwait(false);
        if (!response.Call.IsSuccess || response.Result is not { ValueKind: JsonValueKind.Object } result ||
            !result.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id) ||
            !result.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            return new TelegramValueResult<TelegramChat>(
                response.Call.IsSuccess ? ProtocolFailure("GET_CHAT_RESULT_INVALID") : response.Call,
                null);
        }

        return new TelegramValueResult<TelegramChat>(response.Call, new TelegramChat(id, typeElement.GetString()!));
    }

    public async Task<TelegramValueResult<TelegramWebhookInfo>> GetWebhookInfoAsync(CancellationToken cancellationToken)
    {
        var response = await CallAsync("getWebhookInfo", null, SetupResponseLimitBytes, cancellationToken).ConfigureAwait(false);
        if (!response.Call.IsSuccess || response.Result is not { ValueKind: JsonValueKind.Object } result ||
            !result.TryGetProperty("url", out var urlElement) || urlElement.ValueKind != JsonValueKind.String)
        {
            return new TelegramValueResult<TelegramWebhookInfo>(
                response.Call.IsSuccess ? ProtocolFailure("GET_WEBHOOK_RESULT_INVALID") : response.Call,
                null);
        }

        return new TelegramValueResult<TelegramWebhookInfo>(response.Call, new TelegramWebhookInfo(urlElement.GetString()!));
    }

    public async Task<TelegramValueResult<IReadOnlyList<TelegramUpdate>>> GetUpdatesAsync(
        long? offset,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (timeoutSeconds is < 0 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        var body = new Dictionary<string, object?>
        {
            ["timeout"] = timeoutSeconds,
            ["allowed_updates"] = new[] { "message" },
        };
        if (offset is not null)
        {
            body["offset"] = offset.Value;
        }

        var response = await CallAsync("getUpdates", body, SetupResponseLimitBytes, cancellationToken).ConfigureAwait(false);
        if (!response.Call.IsSuccess || response.Result is not { ValueKind: JsonValueKind.Array } array)
        {
            return new TelegramValueResult<IReadOnlyList<TelegramUpdate>>(
                response.Call.IsSuccess ? ProtocolFailure("GET_UPDATES_RESULT_INVALID") : response.Call,
                null);
        }

        var updates = new List<TelegramUpdate>();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty("update_id", out var updateIdElement) || !updateIdElement.TryGetInt64(out var updateId))
            {
                return new TelegramValueResult<IReadOnlyList<TelegramUpdate>>(ProtocolFailure("GET_UPDATES_ITEM_INVALID"), null);
            }

            long? chatId = null;
            string? chatType = null;
            string? text = null;
            if (element.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
            {
                text = message.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String
                    ? textElement.GetString()
                    : null;
                if (message.TryGetProperty("chat", out var chat) && chat.ValueKind == JsonValueKind.Object)
                {
                    chatId = chat.TryGetProperty("id", out var chatIdElement) && chatIdElement.TryGetInt64(out var parsedChatId)
                        ? parsedChatId
                        : null;
                    chatType = chat.TryGetProperty("type", out var chatTypeElement) && chatTypeElement.ValueKind == JsonValueKind.String
                        ? chatTypeElement.GetString()
                        : null;
                }
            }

            updates.Add(new TelegramUpdate(updateId, chatId, chatType, text));
        }

        return new TelegramValueResult<IReadOnlyList<TelegramUpdate>>(response.Call, updates);
    }

    public void Dispose() => client.Dispose();

    private async Task<ApiResponse> CallAsync(
        string method,
        object? body,
        int responseLimitBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(method));
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: JsonDefaults.Options);
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength > responseLimitBytes)
            {
                return new ApiResponse(new TelegramCallResult(TelegramCallOutcome.Retry, "RESPONSE_TOO_LARGE"), null);
            }

            var bytes = await ReadBoundedAsync(response.Content, responseLimitBytes, cancellationToken).ConfigureAwait(false);
            if (bytes is null)
            {
                return new ApiResponse(new TelegramCallResult(TelegramCallOutcome.Retry, "RESPONSE_TOO_LARGE"), null);
            }

            try
            {
                using var document = TryParse(bytes);
                var hasValidRoot = document is not null && document.RootElement.ValueKind == JsonValueKind.Object;
                var retryAfter = hasValidRoot ? ReadRetryAfter(document!.RootElement) : null;
                if (retryAfter is not null)
                {
                    return new ApiResponse(new TelegramCallResult(TelegramCallOutcome.Retry, "HTTP_RETRY_AFTER", retryAfter), null);
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    return new ApiResponse(new TelegramCallResult(TelegramCallOutcome.Retry, "HTTP_429", TimeSpan.FromSeconds(60)), null);
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return new ApiResponse(new TelegramCallResult(TelegramCallOutcome.AuthBlocked, HealthCodes.AuthBlocked), null);
                }

                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    return new ApiResponse(new TelegramCallResult(TelegramCallOutcome.ChatBlocked, HealthCodes.ChatBlocked), null);
                }

                if ((int)response.StatusCode >= 500)
                {
                    return new ApiResponse(new TelegramCallResult(TelegramCallOutcome.Retry, "HTTP_5XX"), null);
                }

                if (!response.IsSuccessStatusCode)
                {
                    return new ApiResponse(new TelegramCallResult(TelegramCallOutcome.ApiBlocked, HealthCodes.TelegramApiBlocked), null);
                }

                if (!hasValidRoot ||
                    !document!.RootElement.TryGetProperty("ok", out var okElement) ||
                    okElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    return new ApiResponse(new TelegramCallResult(TelegramCallOutcome.Retry, "RESPONSE_PROTOCOL_INVALID"), null);
                }

                if (okElement.ValueKind != JsonValueKind.True)
                {
                    return new ApiResponse(new TelegramCallResult(TelegramCallOutcome.ApiBlocked, HealthCodes.TelegramApiBlocked), null);
                }

                return document.RootElement.TryGetProperty("result", out var result)
                    ? new ApiResponse(new TelegramCallResult(TelegramCallOutcome.Success, "TELEGRAM_OK"), result.Clone())
                    : new ApiResponse(new TelegramCallResult(TelegramCallOutcome.Success, "TELEGRAM_OK"), null);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ApiResponse(new TelegramCallResult(TelegramCallOutcome.Retry, "NETWORK_TIMEOUT"), null);
        }
        catch (HttpRequestException)
        {
            return new ApiResponse(new TelegramCallResult(TelegramCallOutcome.Retry, "NETWORK_ERROR"), null);
        }
        catch (IOException)
        {
            return new ApiResponse(new TelegramCallResult(TelegramCallOutcome.Retry, "RESPONSE_IO_ERROR"), null);
        }
    }

    private Uri BuildUri(string method) => new($"{ApiOrigin}/bot{token}/{method}", UriKind.Absolute);

    private static SocketsHttpHandler CreateProductionHandler() => new()
    {
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        UseCookies = false,
    };

    private static async Task<byte[]?> ReadBoundedAsync(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var rented = ArrayPool<byte>.Shared.Rent(Math.Min(limit + 1, 81920));
        try
        {
            using var memory = new MemoryStream();
            while (true)
            {
                var remaining = limit + 1 - (int)memory.Length;
                if (remaining <= 0)
                {
                    return null;
                }

                var read = await stream.ReadAsync(rented.AsMemory(0, Math.Min(rented.Length, remaining)), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                memory.Write(rented, 0, read);
            }

            return memory.Length > limit ? null : memory.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }

    private static JsonDocument? TryParse(byte[] bytes)
    {
        try
        {
            return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TimeSpan? ReadRetryAfter(JsonElement root)
    {
        if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.False ||
            !root.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("retry_after", out var retryElement) || !retryElement.TryGetInt32(out var seconds) ||
            seconds <= 0)
        {
            return null;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static TelegramCallResult ProtocolFailure(string code) => new(TelegramCallOutcome.Retry, code);

    private static string ValidateToken(string value)
    {
        if (!TelegramTokenShape.IsValid(value))
        {
            throw new ArgumentException("Telegram bot token shape is invalid.", nameof(value));
        }

        return value;
    }

    private sealed record ApiResponse(TelegramCallResult Call, JsonElement? Result);
}
