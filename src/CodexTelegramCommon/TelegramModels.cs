namespace CodexTelegramCommon;

public enum TelegramCallOutcome
{
    Success,
    Retry,
    AuthBlocked,
    ChatBlocked,
    ApiBlocked,
}

public sealed record TelegramCallResult(
    TelegramCallOutcome Outcome,
    string OperationCode,
    TimeSpan? RetryAfter = null)
{
    public bool IsSuccess => Outcome == TelegramCallOutcome.Success;
}

public sealed record TelegramValueResult<T>(TelegramCallResult Call, T? Value)
{
    public bool IsSuccess => Call.IsSuccess && Value is not null;
}

public sealed record TelegramBotIdentity(long Id, string? Username);

public sealed record TelegramChat(long Id, string Type);

public sealed record TelegramWebhookInfo(string Url);

public sealed record TelegramUpdate(long UpdateId, long? ChatId, string? ChatType, string? Text);

public interface ITelegramBotClient : IDisposable
{
    Task<TelegramCallResult> SendCompletionAsync(long chatId, string text, CancellationToken cancellationToken);

    Task<TelegramCallResult> SendSetupTestAsync(long chatId, string text, CancellationToken cancellationToken);

    Task<TelegramValueResult<TelegramBotIdentity>> GetMeAsync(CancellationToken cancellationToken);

    Task<TelegramValueResult<TelegramChat>> GetChatAsync(long chatId, CancellationToken cancellationToken);

    Task<TelegramValueResult<TelegramWebhookInfo>> GetWebhookInfoAsync(CancellationToken cancellationToken);

    Task<TelegramValueResult<IReadOnlyList<TelegramUpdate>>> GetUpdatesAsync(long? offset, int timeoutSeconds, CancellationToken cancellationToken);
}
