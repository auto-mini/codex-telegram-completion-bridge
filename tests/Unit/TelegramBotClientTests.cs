using System.Net;
using System.Text;
using System.Text.Json;
using CodexTelegramCommon;

namespace CodexTelegramUnitTests;

public sealed class TelegramBotClientTests
{
    [Fact]
    public async Task Sends_exact_plaintext_completion_contract()
    {
        string? requestBody = null;
        var handler = new DelegateHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":{\"message_id\":1}}");
        });
        using var client = new TelegramBotClient("123:token", handler);
        var text = "✅ Codex 응답 완료\nPC: PC\n스레드: title_*";

        var result = await client.SendCompletionAsync(42, text, CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var body = JsonDocument.Parse(requestBody!);
        Assert.Equal(42, body.RootElement.GetProperty("chat_id").GetInt64());
        Assert.Equal(text, body.RootElement.GetProperty("text").GetString());
        Assert.False(body.RootElement.GetProperty("disable_notification").GetBoolean());
        Assert.True(body.RootElement.GetProperty("protect_content").GetBoolean());
        Assert.False(body.RootElement.TryGetProperty("parse_mode", out _));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, TelegramCallOutcome.AuthBlocked)]
    [InlineData(HttpStatusCode.Forbidden, TelegramCallOutcome.ChatBlocked)]
    [InlineData(HttpStatusCode.BadRequest, TelegramCallOutcome.ApiBlocked)]
    [InlineData(HttpStatusCode.Redirect, TelegramCallOutcome.ApiBlocked)]
    [InlineData(HttpStatusCode.InternalServerError, TelegramCallOutcome.Retry)]
    public async Task Classifies_http_failures(HttpStatusCode status, TelegramCallOutcome expected)
    {
        using var client = new TelegramBotClient("123:token", new DelegateHandler(_ => Task.FromResult(Json(status, "{\"ok\":false}"))));

        var result = await client.SendCompletionAsync(42, "message", CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, TelegramCallOutcome.AuthBlocked)]
    [InlineData(HttpStatusCode.Redirect, TelegramCallOutcome.ApiBlocked)]
    [InlineData(HttpStatusCode.InternalServerError, TelegramCallOutcome.Retry)]
    public async Task Classifies_http_status_even_when_body_is_not_json(HttpStatusCode status, TelegramCallOutcome expected)
    {
        using var client = new TelegramBotClient("123:token", new DelegateHandler(_ => Task.FromResult(Json(status, "not-json"))));

        var result = await client.SendCompletionAsync(42, "message", CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public async Task Honors_retry_after_independent_of_error_code()
    {
        using var client = new TelegramBotClient(
            "123:token",
            new DelegateHandler(_ => Task.FromResult(Json(HttpStatusCode.BadRequest, "{\"ok\":false,\"error_code\":999,\"parameters\":{\"retry_after\":17}}"))));

        var result = await client.SendCompletionAsync(42, "message", CancellationToken.None);

        Assert.Equal(TelegramCallOutcome.Retry, result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(17), result.RetryAfter);
    }

    [Fact]
    public async Task Rejects_oversized_runtime_response_without_parsing_content()
    {
        var oversized = new string('x', BridgeConstants.RuntimeTelegramResponseLimitBytes + 1);
        using var client = new TelegramBotClient("123:token", new DelegateHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, oversized))));

        var result = await client.SendCompletionAsync(42, "message", CancellationToken.None);

        Assert.Equal("RESPONSE_TOO_LARGE", result.OperationCode);
        Assert.Equal(TelegramCallOutcome.Retry, result.Outcome);
    }

    [Fact]
    public async Task Parses_bot_chat_webhook_and_updates()
    {
        var handler = new DelegateHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(path.EndsWith("getMe", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":{\"id\":7,\"username\":\"bot\"}}")
                : path.EndsWith("getChat", StringComparison.Ordinal)
                    ? Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":{\"id\":42,\"type\":\"private\"}}")
                    : path.EndsWith("getWebhookInfo", StringComparison.Ordinal)
                        ? Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":{\"url\":\"\"}}")
                        : Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":[{\"update_id\":5,\"message\":{\"text\":\"challenge\",\"chat\":{\"id\":42,\"type\":\"private\"}}}]}"));
        });
        using var client = new TelegramBotClient("123:token", handler);

        Assert.Equal(7, (await client.GetMeAsync(CancellationToken.None)).Value!.Id);
        Assert.Equal("private", (await client.GetChatAsync(42, CancellationToken.None)).Value!.Type);
        Assert.Equal("", (await client.GetWebhookInfoAsync(CancellationToken.None)).Value!.Url);
        var updates = (await client.GetUpdatesAsync(null, 0, CancellationToken.None)).Value!;
        Assert.Equal("challenge", Assert.Single(updates).Text);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string value) => new(statusCode)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json"),
    };

    private sealed class DelegateHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request);
    }
}
