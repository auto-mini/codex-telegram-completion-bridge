using CodexTelegramCommon;

namespace CodexTelegramIntegrationTests;

public sealed class WorkerEngineIntegrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "WorkerEngineTests", Guid.NewGuid().ToString("N"));
    private readonly ReversingProtector protector = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hook_to_delivery_keeps_only_answer_prefix_across_spool_duplicates_and_worker_restart(bool useSpool)
    {
        var fixture = CreateFixture(CaptureMode.Live, new StateResolution(ResolutionKind.RootReady, "Title"));
        fixture.SaveCredentials();
        new ProtectedJsonStore<UpstreamRecord>(fixture.Layout.UpstreamPath, protector).Save(new UpstreamRecord(
            BridgeConstants.SchemaVersion, [], null, null, fixture.Now, new string('0', 64), UpstreamKind.Absent));
        if (useSpool)
        {
            File.WriteAllText(fixture.Layout.LocalStateBlockedMarkerPath, "blocked");
        }

        const string payload = """
            {"type":"agent-turn-complete","thread-id":"thread","turn-id":"turn","input-messages":["PRIVATE_PROMPT"],"last-assistant-message":"12345678901234567890123456789012345678901234567890PRIVATE_ANSWER_TAIL"}
            """;
        var hook = new HookHandler(protector, new VendorExecutableValidator(root), _ => { }, _ => { });
        Assert.Equal(0, hook.Handle(fixture.Layout, payload));
        Assert.Equal(0, hook.Handle(fixture.Layout, payload.Replace("12345678901234567890123456789012345678901234567890", "changed duplicate")));
        File.Delete(fixture.Layout.LocalStateBlockedMarkerPath);
        fixture.Now = DateTimeOffset.UtcNow.AddSeconds(2);
        fixture.Telegram.Outcomes.Enqueue(new TelegramCallResult(TelegramCallOutcome.Retry, "HTTP_500"));
        await fixture.Engine.ProcessOneAsync(CancellationToken.None);

        var eventId = Hashing.EventId(fixture.Config.MachineId, "thread", "turn");
        fixture.Now = fixture.Queue.GetEvent(eventId)!.NextAttemptAtUtc.AddSeconds(2);
        fixture.Resolver.Result = new StateResolution(ResolutionKind.RootReady, "Renamed title");
        var restarted = new WorkerEngine(fixture.Layout, new QueueStore(fixture.Layout.DatabasePath),
            new EmergencySpool(fixture.Layout.SpoolDirectory), fixture.ConfigStore, protector,
            _ => fixture.Resolver, _ => fixture.Telegram, () => fixture.Now, () => "Changed PC",
            new OperationalLog(fixture.Layout.LogPath), () => new AclVerificationResult(true, "INSTALL_ACL_OK"));
        await restarted.ProcessOneAsync(CancellationToken.None);

        Assert.Equal(2, fixture.Telegram.SentTexts.Count);
        Assert.All(fixture.Telegram.SentTexts, text => Assert.Equal(
            "✅ Codex 응답 완료\nPC: Test PC\n스레드: Title\n답변: 12345678901234567890123456789012345678901234567890…", text));
        Assert.Equal(1, fixture.Resolver.CallCount);
        Assert.Equal(1, fixture.Queue.GetCounts().Sent);
        Assert.Equal(0, new EmergencySpool(fixture.Layout.SpoolDirectory).CountPending());
        foreach (var path in Directory.EnumerateFiles(fixture.Layout.StateDirectory, "*", SearchOption.AllDirectories)
                     .Append(fixture.Layout.LogPath))
        {
            var raw = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path));
            Assert.DoesNotContain("PRIVATE_PROMPT", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("PRIVATE_ANSWER_TAIL", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("12345678901234567890", raw, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Answer_preview_in_shadow_is_encrypted_and_not_sent()
    {
        var fixture = CreateFixture(CaptureMode.Shadow, new StateResolution(ResolutionKind.RootReady, "Title"));
        var item = fixture.Enqueue("답변 앞부분 테스트입니다");
        await fixture.Engine.ProcessOneAsync(CancellationToken.None);
        Assert.Equal(EventState.Shadow, fixture.Queue.GetEvent(item.EventId)!.State);
        Assert.Empty(fixture.Telegram.SentTexts);
        var envelope = ProtectedJsonCodec.Unprotect<DeliveryEnvelope>(fixture.Queue.GetShadowEnvelope(1)!, protector);
        Assert.Equal("답변 앞부분 테스트입니다", envelope.AnswerPreview);
        Assert.EndsWith("\n답변: 답변 앞부분 테스트입니다", envelope.TelegramText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preview_does_not_bypass_subagent_suppression()
    {
        var fixture = CreateFixture(CaptureMode.Live, new StateResolution(ResolutionKind.Subagent));
        fixture.SaveCredentials();
        var item = fixture.Enqueue("subagent answer");
        await fixture.Engine.ProcessOneAsync(CancellationToken.None);
        Assert.Equal(EventState.Suppressed, fixture.Queue.GetEvent(item.EventId)!.State);
        Assert.Empty(fixture.Telegram.SentTexts);
    }

    [Fact]
    public async Task Shadow_root_is_encrypted_and_never_sent()
    {
        var fixture = CreateFixture(CaptureMode.Shadow, new StateResolution(ResolutionKind.RootReady, "Thread title"));
        var item = fixture.Enqueue();

        var result = await fixture.Engine.ProcessOneAsync(CancellationToken.None);

        Assert.Equal(WorkerIterationKind.Processed, result.Kind);
        Assert.Equal(EventState.Shadow, fixture.Queue.GetEvent(item.EventId)!.State);
        Assert.Empty(fixture.Telegram.SentTexts);
        var protectedEnvelope = fixture.Queue.GetShadowEnvelope(1)!;
        var envelope = ProtectedJsonCodec.Unprotect<DeliveryEnvelope>(protectedEnvelope, protector);
        Assert.Equal("Thread title", envelope.ThreadTitle);
        Assert.Equal(3, envelope.TelegramText.Split('\n').Length);
    }

    [Fact]
    public async Task Shadow_verification_accepts_visible_prefix_but_rejects_short_or_wrong_identity()
    {
        var fixture = CreateFixture(CaptureMode.Shadow, new StateResolution(ResolutionKind.RootReady, "Mobile GPT remote notification task"));
        fixture.Enqueue();
        await fixture.Engine.ProcessOneAsync(CancellationToken.None);
        var control = new CaptureControl(protector, _ => { }, () => { });

        Assert.True(control.VerifyShadow(fixture.Layout, 1, "Test PC", "Mobile GPT remote…"));
        Assert.False(control.VerifyShadow(fixture.Layout, 1, "Wrong PC", "Mobile GPT remote"));
        Assert.False(control.VerifyShadow(fixture.Layout, 1, "Test PC", "Different task title"));
        Assert.False(control.VerifyShadow(fixture.Layout, 1, "Test PC", "short"));
    }

    [Fact]
    public async Task Shadow_verification_accepts_only_the_full_title_when_title_is_short()
    {
        var fixture = CreateFixture(CaptureMode.Shadow, new StateResolution(ResolutionKind.RootReady, "test"));
        fixture.Enqueue();
        await fixture.Engine.ProcessOneAsync(CancellationToken.None);
        var control = new CaptureControl(protector, _ => { }, () => { });

        Assert.True(control.VerifyShadow(fixture.Layout, 1, "Test PC", "test"));
        Assert.True(control.VerifyShadow(fixture.Layout, 1, "Test PC", "test…"));
        Assert.False(control.VerifyShadow(fixture.Layout, 1, "Test PC", "tes"));
        Assert.False(control.VerifyShadow(fixture.Layout, 1, "Wrong PC", "test"));
    }

    [Fact]
    public async Task Suppresses_subagent_without_telegram_credentials()
    {
        var fixture = CreateFixture(CaptureMode.Shadow, new StateResolution(ResolutionKind.Subagent));
        var item = fixture.Enqueue();

        await fixture.Engine.ProcessOneAsync(CancellationToken.None);

        Assert.Equal(EventState.Suppressed, fixture.Queue.GetEvent(item.EventId)!.State);
        Assert.Empty(fixture.Telegram.SentTexts);
    }

    [Fact]
    public async Task Sends_live_completion_with_fixed_message()
    {
        var fixture = CreateFixture(CaptureMode.Live, new StateResolution(ResolutionKind.RootReady, "작업 제목"));
        fixture.SaveCredentials();
        var item = fixture.Enqueue();

        await fixture.Engine.ProcessOneAsync(CancellationToken.None);

        Assert.Equal(EventState.Sent, fixture.Queue.GetEvent(item.EventId)!.State);
        Assert.Equal("✅ Codex 응답 완료\nPC: Test PC\n스레드: 작업 제목", Assert.Single(fixture.Telegram.SentTexts));
    }

    [Fact]
    public async Task Long_live_title_is_full_in_encrypted_envelope_but_short_in_telegram()
    {
        var fullTitle = new string('가', 17);
        var fixture = CreateFixture(CaptureMode.Live, new StateResolution(ResolutionKind.RootReady, fullTitle));
        fixture.SaveCredentials();
        var item = fixture.Enqueue();

        await fixture.Engine.ProcessOneAsync(CancellationToken.None);

        var sent = Assert.Single(fixture.Telegram.SentTexts);
        var displayedTitle = sent.Split('\n')[2]["스레드: ".Length..];
        Assert.Equal(new string('가', 12) + "…", displayedTitle);
        var protectedEnvelope = fixture.Queue.GetEvent(item.EventId)!.DeliveryEnvelopeDpapi!;
        var envelope = ProtectedJsonCodec.Unprotect<DeliveryEnvelope>(protectedEnvelope, protector);
        Assert.Equal(fullTitle, envelope.ThreadTitle);
        Assert.Equal(sent, envelope.TelegramText);
    }

    [Fact]
    public async Task Retry_reuses_encrypted_envelope_after_title_and_pc_change()
    {
        var fixture = CreateFixture(CaptureMode.Live, new StateResolution(ResolutionKind.RootReady, "Original title"));
        fixture.Telegram.Outcomes.Enqueue(new TelegramCallResult(TelegramCallOutcome.Retry, "NETWORK_ERROR"));
        fixture.Telegram.Outcomes.Enqueue(new TelegramCallResult(TelegramCallOutcome.Success, "OK"));
        fixture.SaveCredentials();
        var item = fixture.Enqueue();

        await fixture.Engine.ProcessOneAsync(CancellationToken.None);
        fixture.Now = fixture.Queue.GetEvent(item.EventId)!.NextAttemptAtUtc.AddSeconds(1);
        fixture.ConfigStore.Save(new RuntimeConfig
        {
            MachineId = fixture.Config.MachineId,
            CodexHome = fixture.Config.CodexHome,
            PcAlias = "Renamed PC",
            CaptureMode = CaptureMode.Live,
        });
        fixture.Resolver.Result = new StateResolution(ResolutionKind.RootReady, "Changed title");
        await fixture.Engine.ProcessOneAsync(CancellationToken.None);

        Assert.Equal(2, fixture.Telegram.SentTexts.Count);
        Assert.All(fixture.Telegram.SentTexts, text => Assert.Equal("✅ Codex 응답 완료\nPC: Test PC\n스레드: Original tit…", text));
        Assert.Equal(1, fixture.Resolver.CallCount);
        Assert.Equal(EventState.Sent, fixture.Queue.GetEvent(item.EventId)!.State);
    }

    [Fact]
    public async Task Unsupported_source_is_quarantined_and_degrades_health()
    {
        var fixture = CreateFixture(CaptureMode.Shadow, new StateResolution(ResolutionKind.Unsupported, ErrorCode: "STATE_SCHEMA_UNSUPPORTED"));
        var item = fixture.Enqueue();

        await fixture.Engine.ProcessOneAsync(CancellationToken.None);

        Assert.Equal(EventState.Quarantine, fixture.Queue.GetEvent(item.EventId)!.State);
        var codes = fixture.Queue.GetHealthConditions().Select(condition => condition.ConditionCode).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(HealthCodes.EventQuarantined, codes);
        Assert.Contains(HealthCodes.StateSchemaBlocked, codes);
    }

    [Fact]
    public async Task Auth_failure_preserves_event_and_blocks_network()
    {
        var fixture = CreateFixture(CaptureMode.Live, new StateResolution(ResolutionKind.RootReady, "Title"));
        fixture.Telegram.Outcomes.Enqueue(new TelegramCallResult(TelegramCallOutcome.AuthBlocked, HealthCodes.AuthBlocked));
        fixture.SaveCredentials();
        var item = fixture.Enqueue();

        var result = await fixture.Engine.ProcessOneAsync(CancellationToken.None);

        Assert.Equal(WorkerIterationKind.Blocked, result.Kind);
        Assert.Equal(EventState.Pending, fixture.Queue.GetEvent(item.EventId)!.State);
        Assert.Contains(fixture.Queue.GetHealthConditions(), condition => condition.ConditionCode == HealthCodes.AuthBlocked);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private Fixture CreateFixture(CaptureMode mode, StateResolution resolution)
    {
        var layout = new InstallationLayout(root);
        layout.EnsureMutableDirectories();
        var config = new RuntimeConfig
        {
            MachineId = Guid.NewGuid().ToString("D"),
            CodexHome = Path.Combine(root, ".codex"),
            PcAlias = "Test PC",
            CaptureMode = mode,
        };
        var configStore = new RuntimeConfigStore(layout.RuntimeConfigPath);
        configStore.Save(config);
        var queue = new QueueStore(layout.DatabasePath);
        queue.Initialize();
        var resolver = new MutableResolver(resolution);
        var telegram = new FakeTelegramClient();
        var now = DateTimeOffset.UtcNow;
        var fixture = new Fixture(layout, config, configStore, queue, resolver, telegram, now, protector);
        fixture.Engine = new WorkerEngine(
            layout,
            queue,
            new EmergencySpool(layout.SpoolDirectory),
            configStore,
            protector,
            _ => resolver,
            _ => telegram,
            () => fixture.Now,
            () => "Ignored machine name",
            new OperationalLog(layout.LogPath),
            () => new AclVerificationResult(true, "INSTALL_ACL_OK"));
        return fixture;
    }

    private sealed class Fixture(
        InstallationLayout layout,
        RuntimeConfig config,
        RuntimeConfigStore configStore,
        QueueStore queue,
        MutableResolver resolver,
        FakeTelegramClient telegram,
        DateTimeOffset now,
        ISecretProtector protector)
    {
        public InstallationLayout Layout { get; } = layout;
        public RuntimeConfig Config { get; } = config;
        public RuntimeConfigStore ConfigStore { get; } = configStore;
        public QueueStore Queue { get; } = queue;
        public MutableResolver Resolver { get; } = resolver;
        public FakeTelegramClient Telegram { get; } = telegram;
        public DateTimeOffset Now { get; set; } = now;
        public WorkerEngine Engine { get; set; } = null!;

        public MinimalEvent Enqueue(string? answer = null)
        {
            var threadId = Guid.NewGuid().ToString("D");
            var turnId = Guid.NewGuid().ToString("D");
            var item = new MinimalEvent(
                BridgeConstants.SchemaVersion,
                Hashing.EventId(Config.MachineId, threadId, turnId),
                Config.MachineId,
                threadId,
                turnId,
                Now,
                Config.CaptureMode,
                answer is null ? null : ProtectedJsonCodec.Protect(TextNormalizer.NormalizeAnswerPreview(answer), protector));
            Assert.Equal(InsertOutcome.Inserted, Queue.TryInsert(item));
            return item;
        }

        public void SaveCredentials()
        {
            new ProtectedJsonStore<TelegramCredentials>(Layout.TelegramCredentialsPath, protector)
                .Save(new TelegramCredentials(BridgeConstants.SchemaVersion, "123:token", 123, 42, "private"));
        }
    }

    private sealed class MutableResolver(StateResolution result) : IStateResolver
    {
        public StateResolution Result { get; set; } = result;
        public int CallCount { get; private set; }

        public StateResolution Resolve(string threadId)
        {
            CallCount++;
            return Result;
        }
    }

    private sealed class FakeTelegramClient : ITelegramBotClient
    {
        public Queue<TelegramCallResult> Outcomes { get; } = new();
        public List<string> SentTexts { get; } = [];

        public Task<TelegramCallResult> SendCompletionAsync(long chatId, string text, CancellationToken cancellationToken)
        {
            SentTexts.Add(text);
            return Task.FromResult(Outcomes.Count > 0
                ? Outcomes.Dequeue()
                : new TelegramCallResult(TelegramCallOutcome.Success, "OK"));
        }

        public Task<TelegramCallResult> SendSetupTestAsync(long chatId, string text, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TelegramValueResult<TelegramBotIdentity>> GetMeAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TelegramValueResult<TelegramChat>> GetChatAsync(long chatId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TelegramValueResult<TelegramWebhookInfo>> GetWebhookInfoAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TelegramValueResult<IReadOnlyList<TelegramUpdate>>> GetUpdatesAsync(long? offset, int timeoutSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();

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
