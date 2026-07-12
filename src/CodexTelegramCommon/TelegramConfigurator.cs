using System.Security.Cryptography;

namespace CodexTelegramCommon;

public sealed class TelegramConfigurationException(string operationCode) : Exception(operationCode)
{
    public string OperationCode { get; } = operationCode;
}

public sealed class TelegramConfigurator(
    Func<string, ITelegramBotClient> clientFactory,
    ISecretProtector protector,
    Func<DateTimeOffset> utcNow,
    Func<byte[]> challengeBytes)
{
    public static TelegramConfigurator CreateProduction() => new(
        token => new TelegramBotClient(token),
        new DpapiSecretProtector(),
        () => DateTimeOffset.UtcNow,
        () => RandomNumberGenerator.GetBytes(16));

    public async Task<long> BootstrapFirstAsync(
        InstallationLayout layout,
        string token,
        Action<string> showChallenge,
        CancellationToken cancellationToken,
        bool migrateExistingBot = false)
    {
        var config = migrateExistingBot
            ? RequireBotMigrationMode(layout)
            : RequireConfigurationMode(layout, requireNoCredentials: true);
        using var client = CreateClient(token);
        var identity = await RequireIdentityAndNoWebhookAsync(client, cancellationToken).ConfigureAwait(false);
        var challenge = CreateChallenge();
        var deadline = utcNow() + TimeSpan.FromMinutes(10);
        showChallenge(challenge);

        TelegramUpdate? matched = null;
        long? offset = null;
        while (utcNow() < deadline && !cancellationToken.IsCancellationRequested)
        {
            var updates = await client.GetUpdatesAsync(offset, 10, cancellationToken).ConfigureAwait(false);
            RequireSuccess(updates.Call);
            var values = updates.Value ?? throw new TelegramConfigurationException("GET_UPDATES_RESULT_INVALID");
            if (values.Count > 0)
            {
                offset = checked(values.Max(update => update.UpdateId) + 1);
            }

            var matches = values.Where(update => string.Equals(update.Text, challenge, StringComparison.Ordinal)).ToArray();
            if (matches.Length > 1 || matched is not null && matches.Length > 0)
            {
                throw new TelegramConfigurationException("CHALLENGE_AMBIGUOUS");
            }

            if (matches.Length == 1)
            {
                matched = matches[0];
                break;
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (matched is null || utcNow() >= deadline)
        {
            throw new TelegramConfigurationException("CHALLENGE_EXPIRED");
        }

        if (matched.ChatId is null || !string.Equals(matched.ChatType, "private", StringComparison.Ordinal))
        {
            throw new TelegramConfigurationException("CHAT_NOT_PRIVATE");
        }

        var advance = await client.GetUpdatesAsync(checked(matched.UpdateId + 1), 0, cancellationToken).ConfigureAwait(false);
        RequireSuccess(advance.Call);
        if (advance.Value?.Any(update => string.Equals(update.Text, challenge, StringComparison.Ordinal)) == true)
        {
            throw new TelegramConfigurationException("CHALLENGE_AMBIGUOUS");
        }

        await ValidateChatAndSendTestAsync(client, matched.ChatId.Value, config, cancellationToken).ConfigureAwait(false);
        var credentials = new TelegramCredentials(
            BridgeConstants.SchemaVersion,
            token,
            identity.Id,
            matched.ChatId.Value,
            "private");
        CommitCredentials(layout, credentials);
        if (migrateExistingBot)
        {
            new QueueStore(layout.DatabasePath).ClearHealth(
                HealthCodes.AuthBlocked,
                HealthCodes.ChatBlocked,
                HealthCodes.TelegramApiBlocked,
                HealthCodes.TelegramRetrying);
        }

        return matched.ChatId.Value;
    }

    public async Task ConfigureKnownChatAsync(
        InstallationLayout layout,
        string token,
        long chatId,
        bool reconfigure,
        CancellationToken cancellationToken)
    {
        var config = RequireConfigurationMode(layout, requireNoCredentials: !reconfigure);
        TelegramCredentials? previous = null;
        if (reconfigure)
        {
            previous = LoadCredentials(layout);
            if (config.CaptureMode == CaptureMode.Live && !config.DeliveryPaused)
            {
                throw new TelegramConfigurationException("DELIVERY_NOT_PAUSED");
            }
        }

        using var client = CreateClient(token);
        var identity = await RequireIdentityAndNoWebhookAsync(client, cancellationToken).ConfigureAwait(false);
        if (previous is not null && identity.Id != previous.BotUserId)
        {
            throw new TelegramConfigurationException("BOT_IDENTITY_CHANGED");
        }

        await ValidateChatAndSendTestAsync(client, chatId, config, cancellationToken).ConfigureAwait(false);
        CommitCredentials(layout, new TelegramCredentials(
            BridgeConstants.SchemaVersion,
            token,
            identity.Id,
            chatId,
            "private"));
        new QueueStore(layout.DatabasePath).ClearHealth(
            HealthCodes.AuthBlocked,
            HealthCodes.ChatBlocked,
            HealthCodes.TelegramApiBlocked,
            HealthCodes.TelegramRetrying);
    }

    private RuntimeConfig RequireConfigurationMode(InstallationLayout layout, bool requireNoCredentials)
    {
        var config = new RuntimeConfigStore(layout.RuntimeConfigPath).Load();
        if (!requireNoCredentials && config.CaptureMode == CaptureMode.Live && !config.DeliveryPaused)
        {
            throw new TelegramConfigurationException("DELIVERY_NOT_PAUSED");
        }

        if (requireNoCredentials && config.CaptureMode != CaptureMode.Shadow)
        {
            throw new TelegramConfigurationException("INITIAL_CONFIG_REQUIRES_SHADOW");
        }

        if (requireNoCredentials && File.Exists(layout.TelegramCredentialsPath))
        {
            throw new TelegramConfigurationException("CREDENTIALS_ALREADY_CONFIGURED");
        }

        return config;
    }

    private static RuntimeConfig RequireBotMigrationMode(InstallationLayout layout)
    {
        var config = new RuntimeConfigStore(layout.RuntimeConfigPath).Load();
        if (!File.Exists(layout.TelegramCredentialsPath))
        {
            throw new TelegramConfigurationException("CREDENTIALS_NOT_CONFIGURED");
        }

        if (config.CaptureMode == CaptureMode.Live && !config.DeliveryPaused)
        {
            throw new TelegramConfigurationException("DELIVERY_NOT_PAUSED");
        }

        return config;
    }

    private async Task<TelegramBotIdentity> RequireIdentityAndNoWebhookAsync(
        ITelegramBotClient client,
        CancellationToken cancellationToken)
    {
        var me = await client.GetMeAsync(cancellationToken).ConfigureAwait(false);
        RequireSuccess(me.Call);
        if (me.Value is not { Id: > 0 } identity)
        {
            throw new TelegramConfigurationException("GET_ME_RESULT_INVALID");
        }

        var webhook = await client.GetWebhookInfoAsync(cancellationToken).ConfigureAwait(false);
        RequireSuccess(webhook.Call);
        if (webhook.Value is null)
        {
            throw new TelegramConfigurationException("GET_WEBHOOK_RESULT_INVALID");
        }

        if (!string.IsNullOrEmpty(webhook.Value.Url))
        {
            throw new TelegramConfigurationException("WEBHOOK_CONFIGURED");
        }

        return identity;
    }

    private static async Task ValidateChatAndSendTestAsync(
        ITelegramBotClient client,
        long chatId,
        RuntimeConfig config,
        CancellationToken cancellationToken)
    {
        if (chatId == 0)
        {
            throw new TelegramConfigurationException("CHAT_ID_INVALID");
        }

        var chat = await client.GetChatAsync(chatId, cancellationToken).ConfigureAwait(false);
        RequireSuccess(chat.Call);
        if (chat.Value is null || chat.Value.Id != chatId || !string.Equals(chat.Value.Type, "private", StringComparison.Ordinal))
        {
            throw new TelegramConfigurationException("CHAT_NOT_PRIVATE");
        }

        var pcName = TextNormalizer.NormalizePcName(config.PcAlias ?? Environment.MachineName)
                     ?? throw new TelegramConfigurationException("PC_NAME_INVALID");
        var test = await client.SendSetupTestAsync(chatId, $"🧪 Codex 알림 연결 테스트\nPC: {pcName}", cancellationToken).ConfigureAwait(false);
        RequireSuccess(test);
    }

    private void CommitCredentials(InstallationLayout layout, TelegramCredentials credentials)
    {
        credentials.Validate();
        var store = new ProtectedJsonStore<TelegramCredentials>(layout.TelegramCredentialsPath, protector);
        var previous = File.Exists(layout.TelegramCredentialsPath) ? File.ReadAllBytes(layout.TelegramCredentialsPath) : null;
        try
        {
            store.Save(credentials);
            var verified = store.Load();
            verified.Validate();
            if (verified.BotUserId != credentials.BotUserId || verified.ChatId != credentials.ChatId ||
                !string.Equals(verified.BotToken, credentials.BotToken, StringComparison.Ordinal))
            {
                throw new TelegramConfigurationException("CREDENTIAL_COMMIT_VERIFY_FAILED");
            }
        }
        catch
        {
            if (previous is null)
            {
                if (File.Exists(layout.TelegramCredentialsPath))
                {
                    File.Delete(layout.TelegramCredentialsPath);
                }
            }
            else
            {
                AtomicFile.WriteBytes(layout.TelegramCredentialsPath, previous);
            }

            throw;
        }
        finally
        {
            if (previous is not null)
            {
                CryptographicOperations.ZeroMemory(previous);
            }
        }
    }

    private TelegramCredentials LoadCredentials(InstallationLayout layout)
    {
        try
        {
            var value = new ProtectedJsonStore<TelegramCredentials>(layout.TelegramCredentialsPath, protector).Load();
            value.Validate();
            return value;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or System.Text.Json.JsonException or InvalidDataException)
        {
            throw new TelegramConfigurationException("CREDENTIALS_INVALID");
        }
    }

    private ITelegramBotClient CreateClient(string token)
    {
        try
        {
            return clientFactory(token);
        }
        catch (ArgumentException)
        {
            throw new TelegramConfigurationException("TOKEN_SHAPE_INVALID");
        }
    }

    private string CreateChallenge()
    {
        var bytes = challengeBytes();
        if (bytes.Length != 16)
        {
            throw new InvalidOperationException("Challenge source must return 128 bits.");
        }

        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static void RequireSuccess(TelegramCallResult result)
    {
        if (!result.IsSuccess)
        {
            throw new TelegramConfigurationException(result.OperationCode);
        }
    }
}
