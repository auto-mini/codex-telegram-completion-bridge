using CodexTelegramCommon;

namespace CodexTelegramIntegrationTests;

public sealed class TelegramConfiguratorIntegrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "TelegramConfiguratorTests", Guid.NewGuid().ToString("N"));
    private readonly ReversingProtector protector = new();

    [Fact]
    public async Task Bootstrap_selects_only_exact_private_challenge_then_commits_after_test()
    {
        var layout = Prepare(CaptureMode.Shadow, paused: false);
        var fake = new FakeTelegramClient();
        var now = DateTimeOffset.UtcNow;
        var service = Create(fake, () => now);
        string? shown = null;

        var chatId = await service.BootstrapFirstAsync(layout, "123:secret-token", challenge =>
        {
            shown = challenge;
            fake.UpdateResults.Enqueue(Success<IReadOnlyList<TelegramUpdate>>(
            [
                new TelegramUpdate(10, 777, "private", "stale-wrong-value"),
                new TelegramUpdate(11, 777, "private", challenge),
            ]));
            fake.UpdateResults.Enqueue(Success<IReadOnlyList<TelegramUpdate>>([]));
        }, CancellationToken.None);

        Assert.Equal(777, chatId);
        Assert.NotNull(shown);
        Assert.DoesNotContain(shown!, " \r\n\t", StringComparison.Ordinal);
        Assert.Equal("🧪 Codex 알림 연결 테스트\nPC: Test PC", Assert.Single(fake.SetupTexts));
        var credentials = new ProtectedJsonStore<TelegramCredentials>(layout.TelegramCredentialsPath, protector).Load();
        Assert.Equal(42, credentials.BotUserId);
        Assert.Equal(777, credentials.ChatId);
        Assert.DoesNotContain("secret-token", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(layout.TelegramCredentialsPath)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Duplicate_challenge_fails_without_credentials()
    {
        var layout = Prepare(CaptureMode.Shadow, paused: false);
        var fake = new FakeTelegramClient();
        var service = Create(fake, () => DateTimeOffset.UtcNow);

        var exception = await Assert.ThrowsAsync<TelegramConfigurationException>(() =>
            service.BootstrapFirstAsync(layout, "123:token", challenge =>
            {
                fake.UpdateResults.Enqueue(Success<IReadOnlyList<TelegramUpdate>>(
                [
                    new TelegramUpdate(1, 7, "private", challenge),
                    new TelegramUpdate(2, 7, "private", challenge),
                ]));
            }, CancellationToken.None));

        Assert.Equal("CHALLENGE_AMBIGUOUS", exception.OperationCode);
        Assert.False(File.Exists(layout.TelegramCredentialsPath));
        Assert.Empty(fake.SetupTexts);
    }

    [Fact]
    public async Task Challenge_expiry_and_non_private_chat_fail_closed()
    {
        var layout = Prepare(CaptureMode.Shadow, paused: false);
        var fake = new FakeTelegramClient();
        var now = DateTimeOffset.UtcNow;
        fake.OnGetUpdates = () => now += TimeSpan.FromMinutes(11);
        var expiry = await Assert.ThrowsAsync<TelegramConfigurationException>(() =>
            Create(fake, () => now).BootstrapFirstAsync(layout, "123:token", _ => { }, CancellationToken.None));
        Assert.Equal("CHALLENGE_EXPIRED", expiry.OperationCode);

        fake.OnGetUpdates = null;
        var group = await Assert.ThrowsAsync<TelegramConfigurationException>(() =>
            Create(fake, () => DateTimeOffset.UtcNow).BootstrapFirstAsync(layout, "123:token", challenge =>
            {
                fake.UpdateResults.Enqueue(Success<IReadOnlyList<TelegramUpdate>>([new TelegramUpdate(3, -100, "group", challenge)]));
            }, CancellationToken.None));
        Assert.Equal("CHAT_NOT_PRIVATE", group.OperationCode);
        Assert.False(File.Exists(layout.TelegramCredentialsPath));
    }

    [Fact]
    public async Task Reconfigure_requires_pause_and_refuses_silent_bot_change()
    {
        var layout = Prepare(CaptureMode.Live, paused: true);
        new ProtectedJsonStore<TelegramCredentials>(layout.TelegramCredentialsPath, protector)
            .Save(new TelegramCredentials(BridgeConstants.SchemaVersion, "123:old", 42, 777, "private"));
        var fake = new FakeTelegramClient { Identity = new TelegramBotIdentity(99, "different") };

        var error = await Assert.ThrowsAsync<TelegramConfigurationException>(() =>
            Create(fake, () => DateTimeOffset.UtcNow).ConfigureKnownChatAsync(layout, "123:new", 777, reconfigure: true, CancellationToken.None));

        Assert.Equal("BOT_IDENTITY_CHANGED", error.OperationCode);
        var retained = new ProtectedJsonStore<TelegramCredentials>(layout.TelegramCredentialsPath, protector).Load();
        Assert.Equal("123:old", retained.BotToken);
        Assert.Empty(fake.SetupTexts);
    }

    [Fact]
    public async Task Explicit_paused_bot_migration_uses_full_challenge_and_accepts_new_identity()
    {
        var layout = Prepare(CaptureMode.Live, paused: true);
        new ProtectedJsonStore<TelegramCredentials>(layout.TelegramCredentialsPath, protector)
            .Save(new TelegramCredentials(BridgeConstants.SchemaVersion, "123:old", 42, 777, "private"));
        var fake = new FakeTelegramClient { Identity = new TelegramBotIdentity(99, "new-bot") };
        var service = Create(fake, () => DateTimeOffset.UtcNow);

        await service.BootstrapFirstAsync(layout, "999:new-token", challenge =>
        {
            fake.UpdateResults.Enqueue(Success<IReadOnlyList<TelegramUpdate>>([new TelegramUpdate(20, 777, "private", challenge)]));
            fake.UpdateResults.Enqueue(Success<IReadOnlyList<TelegramUpdate>>([]));
        }, CancellationToken.None, migrateExistingBot: true);

        var migrated = new ProtectedJsonStore<TelegramCredentials>(layout.TelegramCredentialsPath, protector).Load();
        Assert.Equal(99, migrated.BotUserId);
        Assert.Equal("999:new-token", migrated.BotToken);
        Assert.True(new RuntimeConfigStore(layout.RuntimeConfigPath).Load().DeliveryPaused);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private InstallationLayout Prepare(CaptureMode mode, bool paused)
    {
        var layout = new InstallationLayout(root);
        layout.EnsureMutableDirectories();
        new RuntimeConfigStore(layout.RuntimeConfigPath).Save(new RuntimeConfig
        {
            MachineId = Guid.NewGuid().ToString("D"),
            CodexHome = Path.Combine(root, ".codex"),
            PcAlias = "Test PC",
            CaptureMode = mode,
            DeliveryPaused = paused,
        });
        new QueueStore(layout.DatabasePath).Initialize();
        return layout;
    }

    private TelegramConfigurator Create(FakeTelegramClient fake, Func<DateTimeOffset> clock) => new(
        _ => fake,
        protector,
        clock,
        () => Enumerable.Range(0, 16).Select(value => (byte)value).ToArray());

    private static TelegramValueResult<T> Success<T>(T value) => new(
        new TelegramCallResult(TelegramCallOutcome.Success, "OK"),
        value);

    private sealed class FakeTelegramClient : ITelegramBotClient
    {
        public TelegramBotIdentity Identity { get; set; } = new(42, "bot");
        public TelegramChat Chat { get; set; } = new(777, "private");
        public string WebhookUrl { get; set; } = string.Empty;
        public Queue<TelegramValueResult<IReadOnlyList<TelegramUpdate>>> UpdateResults { get; } = new();
        public List<string> SetupTexts { get; } = [];
        public Action? OnGetUpdates { get; set; }

        public Task<TelegramCallResult> SendCompletionAsync(long chatId, string text, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TelegramCallResult> SendSetupTestAsync(long chatId, string text, CancellationToken cancellationToken)
        {
            SetupTexts.Add(text);
            return Task.FromResult(new TelegramCallResult(TelegramCallOutcome.Success, "OK"));
        }

        public Task<TelegramValueResult<TelegramBotIdentity>> GetMeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Success(Identity));

        public Task<TelegramValueResult<TelegramChat>> GetChatAsync(long chatId, CancellationToken cancellationToken) =>
            Task.FromResult(Success(Chat with { Id = chatId }));

        public Task<TelegramValueResult<TelegramWebhookInfo>> GetWebhookInfoAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Success(new TelegramWebhookInfo(WebhookUrl)));

        public Task<TelegramValueResult<IReadOnlyList<TelegramUpdate>>> GetUpdatesAsync(long? offset, int timeoutSeconds, CancellationToken cancellationToken)
        {
            OnGetUpdates?.Invoke();
            return Task.FromResult(UpdateResults.Count > 0
                ? UpdateResults.Dequeue()
                : Success<IReadOnlyList<TelegramUpdate>>([]));
        }

        public void Dispose()
        {
        }
    }

    private sealed class ReversingProtector : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plaintext)
        {
            var bytes = plaintext.ToArray();
            Array.Reverse(bytes);
            return bytes;
        }

        public byte[] Unprotect(ReadOnlySpan<byte> ciphertext) => Protect(ciphertext);
    }
}
