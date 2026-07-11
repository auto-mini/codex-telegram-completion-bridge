using System.Security.Cryptography;
using System.Text.Json;

namespace CodexTelegramCommon;

public enum WorkerIterationKind
{
    Processed,
    Waiting,
    Blocked,
}

public sealed record WorkerIterationResult(WorkerIterationKind Kind, DateTimeOffset? NextDueUtc = null);

public sealed class WorkerEngine
{
    private readonly InstallationLayout layout;
    private readonly QueueStore queue;
    private readonly EmergencySpool spool;
    private readonly RuntimeConfigStore configStore;
    private readonly ISecretProtector protector;
    private readonly Func<string, IStateResolver> resolverFactory;
    private readonly Func<string, ITelegramBotClient> telegramFactory;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly Func<string?> computerName;
    private readonly OperationalLog log;
    private DateTimeOffset? lastNetworkAttemptUtc;

    public WorkerEngine(
        InstallationLayout layout,
        QueueStore queue,
        EmergencySpool spool,
        RuntimeConfigStore configStore,
        ISecretProtector protector,
        Func<string, IStateResolver> resolverFactory,
        Func<string, ITelegramBotClient> telegramFactory,
        Func<DateTimeOffset> utcNow,
        Func<string?> computerName,
        OperationalLog log)
    {
        this.layout = layout;
        this.queue = queue;
        this.spool = spool;
        this.configStore = configStore;
        this.protector = protector;
        this.resolverFactory = resolverFactory;
        this.telegramFactory = telegramFactory;
        this.utcNow = utcNow;
        this.computerName = computerName;
        this.log = log;
    }

    public static WorkerEngine CreateProduction(InstallationLayout layout)
    {
        var protector = new DpapiSecretProtector();
        return new WorkerEngine(
            layout,
            new QueueStore(layout.DatabasePath),
            new EmergencySpool(layout.SpoolDirectory),
            new RuntimeConfigStore(layout.RuntimeConfigPath),
            protector,
            codexHome => new CodexStateResolver(codexHome),
            token => new TelegramBotClient(token),
            () => DateTimeOffset.UtcNow,
            () => Environment.MachineName,
            new OperationalLog(layout.LogPath));
    }

    public async Task<WorkerIterationResult> ProcessOneAsync(CancellationToken cancellationToken)
    {
        RuntimeConfig config;
        try
        {
            config = configStore.Load();
            queue.Initialize();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            log.Write("ERROR", "RUNTIME_CONFIG_INVALID", exception: exception);
            return new WorkerIterationResult(WorkerIterationKind.Blocked);
        }

        var now = utcNow();
        try
        {
            spool.ImportAll(queue, now);
            queue.RecoverExpiredLeases(now);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Write("ERROR", "LOCAL_STATE_BLOCKED", exception: exception);
            return new WorkerIterationResult(WorkerIterationKind.Blocked);
        }

        var networkAllowed = IsNetworkAllowed(config, now);
        var item = queue.AcquireNextDue(now, networkAllowed);
        if (item is null)
        {
            var nextDue = queue.GetNextPendingDueUtc(includeDeliveryReady: networkAllowed);
            if (!networkAllowed && nextDue is null && queue.GetCounts().Pending > 0)
            {
                return new WorkerIterationResult(WorkerIterationKind.Blocked);
            }

            return new WorkerIterationResult(
                WorkerIterationKind.Waiting,
                nextDue);
        }

        DeliveryEnvelope? envelope = null;
        if (item.DeliveryEnvelopeDpapi is null)
        {
            var resolution = resolverFactory(config.CodexHome).Resolve(item.ThreadId);
            switch (resolution.Kind)
            {
                case ResolutionKind.Subagent:
                    queue.MarkSuppressed(item.EventId, now);
                    log.Write("INFO", "SUBAGENT_SUPPRESSED", item.EventId);
                    return new WorkerIterationResult(WorkerIterationKind.Processed);
                case ResolutionKind.Unsupported:
                    queue.MarkQuarantine(item.EventId, now, resolution.ErrorCode ?? "STATE_SCHEMA_UNSUPPORTED");
                    queue.UpsertHealth(HealthCodes.StateSchemaBlocked, now);
                    log.Write("WARN", "EVENT_QUARANTINED", item.EventId);
                    return new WorkerIterationResult(WorkerIterationKind.Processed);
                case ResolutionKind.NotReady:
                    if (now - item.ObservedAtUtc >= BridgeConstants.ResolverQuarantineAge)
                    {
                        queue.MarkQuarantine(item.EventId, now, "THREAD_STATE_TIMEOUT");
                        log.Write("WARN", "EVENT_QUARANTINED", item.EventId);
                    }
                    else
                    {
                        var delay = RetryPolicy.ResolutionDelay(item.ResolutionAttemptCount + 1);
                        queue.RescheduleResolution(item.EventId, now + delay, resolution.ErrorCode ?? "THREAD_NOT_READY");
                    }

                    return new WorkerIterationResult(WorkerIterationKind.Processed);
                case ResolutionKind.RootReady:
                    byte[] protectedEnvelope;
                    try
                    {
                        envelope = CreateEnvelope(config, resolution.NormalizedTitle!);
                        protectedEnvelope = ProtectedJsonCodec.Protect(envelope, protector);
                    }
                    catch (Exception exception) when (exception is CryptographicException or InvalidDataException)
                    {
                        queue.RescheduleResolution(item.EventId, now + TimeSpan.FromMinutes(5), "ENVELOPE_PROTECT_FAILED");
                        queue.UpsertHealth(HealthCodes.LocalStateBlocked, now);
                        log.Write("ERROR", "ENVELOPE_PROTECT_FAILED", item.EventId, exception: exception);
                        return new WorkerIterationResult(WorkerIterationKind.Blocked);
                    }

                    if (item.IngestMode == CaptureMode.Shadow)
                    {
                        try
                        {
                            queue.MarkShadow(item.EventId, protectedEnvelope, now);
                        }
                        finally
                        {
                            CryptographicOperations.ZeroMemory(protectedEnvelope);
                        }

                        log.Write("INFO", "SHADOW_CAPTURED", item.EventId);
                        return new WorkerIterationResult(WorkerIterationKind.Processed);
                    }

                    try
                    {
                        queue.SaveEnvelopeForDelivery(item.EventId, protectedEnvelope, now);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(protectedEnvelope);
                    }

                    break;
                default:
                    throw new InvalidOperationException("Unknown resolver outcome.");
            }
        }

        if (!networkAllowed)
        {
            queue.ReleaseInflightForDelivery(item.EventId, now, "DELIVERY_GATED");
            return new WorkerIterationResult(WorkerIterationKind.Blocked, queue.GetNextPendingDueUtc(includeDeliveryReady: false));
        }

        try
        {
            if (envelope is null)
            {
                var protectedEnvelope = item.DeliveryEnvelopeDpapi!;
                try
                {
                    envelope = ProtectedJsonCodec.Unprotect<DeliveryEnvelope>(protectedEnvelope, protector);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(protectedEnvelope);
                }
            }

            ValidateEnvelope(envelope);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or InvalidDataException)
        {
            queue.ReleaseInflightForDelivery(item.EventId, now + TimeSpan.FromMinutes(5), "ENVELOPE_UNPROTECT_FAILED");
            queue.UpsertHealth(HealthCodes.LocalStateBlocked, now);
            log.Write("ERROR", "ENVELOPE_UNPROTECT_FAILED", item.EventId, exception: exception);
            return new WorkerIterationResult(WorkerIterationKind.Blocked);
        }

        TelegramCredentials credentials;
        try
        {
            credentials = new ProtectedJsonStore<TelegramCredentials>(layout.TelegramCredentialsPath, protector).Load();
            credentials.Validate();
        }
        catch (Exception exception) when (exception is IOException or CryptographicException or JsonException or InvalidDataException)
        {
            queue.BlockDelivery(item.EventId, HealthCodes.AuthBlocked, now);
            log.Write("ERROR", "TELEGRAM_CREDENTIALS_INVALID", item.EventId, exception: exception);
            return new WorkerIterationResult(WorkerIterationKind.Blocked);
        }

        await EnforceThrottleAsync(now, cancellationToken).ConfigureAwait(false);
        using var telegram = telegramFactory(credentials.BotToken);
        var result = await telegram.SendCompletionAsync(credentials.ChatId, envelope.TelegramText, cancellationToken).ConfigureAwait(false);
        lastNetworkAttemptUtc = utcNow();
        switch (result.Outcome)
        {
            case TelegramCallOutcome.Success:
                queue.MarkSent(item.EventId, utcNow());
                log.Write("INFO", "TELEGRAM_SENT", item.EventId, item.DeliveryAttemptCount);
                return new WorkerIterationResult(WorkerIterationKind.Processed);
            case TelegramCallOutcome.Retry:
                var delay = result.RetryAfter is null
                    ? RetryPolicy.DeliveryDelay(item.DeliveryAttemptCount)
                    : result.RetryAfter.Value + TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(0, 1001));
                var next = utcNow() + delay;
                queue.RescheduleDelivery(
                    item.EventId,
                    next,
                    result.OperationCode,
                    utcNow(),
                    result.RetryAfter is null ? null : next);
                log.Write("WARN", "TELEGRAM_RETRY", item.EventId, item.DeliveryAttemptCount + 1);
                return new WorkerIterationResult(WorkerIterationKind.Processed, next);
            case TelegramCallOutcome.AuthBlocked:
                queue.BlockDelivery(item.EventId, HealthCodes.AuthBlocked, utcNow());
                return new WorkerIterationResult(WorkerIterationKind.Blocked);
            case TelegramCallOutcome.ChatBlocked:
                queue.BlockDelivery(item.EventId, HealthCodes.ChatBlocked, utcNow());
                return new WorkerIterationResult(WorkerIterationKind.Blocked);
            case TelegramCallOutcome.ApiBlocked:
                queue.BlockDelivery(item.EventId, HealthCodes.TelegramApiBlocked, utcNow());
                return new WorkerIterationResult(WorkerIterationKind.Blocked);
            default:
                throw new InvalidOperationException("Unknown Telegram outcome.");
        }
    }

    private bool IsNetworkAllowed(RuntimeConfig config, DateTimeOffset now)
    {
        if (config.CaptureMode != CaptureMode.Live || config.DeliveryPaused || !File.Exists(layout.TelegramCredentialsPath) || queue.HasNetworkBlockingHealthCondition())
        {
            return false;
        }

        var notBefore = queue.GetTelegramNotBeforeUtc();
        return notBefore is null || notBefore <= now;
    }

    private DeliveryEnvelope CreateEnvelope(RuntimeConfig config, string normalizedTitle)
    {
        var pc = TextNormalizer.NormalizePcName(config.PcAlias ?? computerName())
                 ?? throw new InvalidDataException("PC name is unavailable.");
        return new DeliveryEnvelope(
            BridgeConstants.SchemaVersion,
            pc,
            normalizedTitle,
            TextNormalizer.RenderCompletion(pc, normalizedTitle));
    }

    private async Task EnforceThrottleAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (lastNetworkAttemptUtc is null)
        {
            return;
        }

        var remaining = TimeSpan.FromSeconds(1) - (now - lastNetworkAttemptUtc.Value);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ValidateEnvelope(DeliveryEnvelope envelope)
    {
        if (envelope.SchemaVersion != BridgeConstants.SchemaVersion ||
            !string.Equals(TextNormalizer.RenderCompletion(envelope.PcName, envelope.ThreadTitle), envelope.TelegramText, StringComparison.Ordinal) ||
            envelope.TelegramText.Split('\n').Length != 3)
        {
            throw new InvalidDataException("Delivery envelope failed validation.");
        }
    }
}
