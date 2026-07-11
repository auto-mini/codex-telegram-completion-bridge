using System.Diagnostics;

namespace CodexTelegramCommon;

public sealed class HookHandler(
    ISecretProtector protector,
    VendorExecutableValidator vendorValidator,
    Action<string> startWorker,
    Action<string> signalWorker)
{
    public int Handle(InstallationLayout layout, string originalNotifyJson)
    {
        var log = new OperationalLog(layout.LogPath);
        RuntimeConfig? config = null;
        QueueStore? queue = null;
        try
        {
            config = new RuntimeConfigStore(layout.RuntimeConfigPath).Load();
            queue = new QueueStore(layout.DatabasePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            log.Write("ERROR", "RUNTIME_CONFIG_INVALID", exception: exception);
        }

        var parsed = NotifyPayloadParser.Parse(originalNotifyJson);
        if (parsed.Kind == NotifyParseKind.Completion && config is not null && queue is not null)
        {
            var item = new MinimalEvent(
                BridgeConstants.SchemaVersion,
                Hashing.EventId(config.MachineId, parsed.ThreadId!, parsed.TurnId!),
                config.MachineId,
                parsed.ThreadId!,
                parsed.TurnId!,
                DateTimeOffset.UtcNow,
                config.CaptureMode);
            try
            {
                var spool = new EmergencySpool(layout.SpoolDirectory);
                if (File.Exists(layout.LocalStateBlockedMarkerPath))
                {
                    if (!spool.TryWrite(item))
                    {
                        log.Write("ERROR", "CAPTURE_STORAGE_FAILED", item.EventId);
                    }
                }
                else
                {
                    var outcome = queue.TryInsert(item);
                    if (outcome == InsertOutcome.Busy && !spool.TryWrite(item))
                    {
                        log.Write("ERROR", "CAPTURE_STORAGE_FAILED", item.EventId);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or InvalidDataException)
            {
                if (!new EmergencySpool(layout.SpoolDirectory).TryWrite(item))
                {
                    log.Write("ERROR", "CAPTURE_STORAGE_FAILED", item.EventId, exception: exception);
                }
            }
        }
        else if (parsed.Kind == NotifyParseKind.Invalid)
        {
            log.Write("WARN", parsed.ErrorCode ?? "PAYLOAD_INVALID");
        }

        InvokeUpstream(layout, queue, log, originalNotifyJson);

        if (parsed.Kind == NotifyParseKind.Completion && config is not null)
        {
            try
            {
                startWorker(config.MachineId);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
            {
                log.Write("ERROR", "WORKER_START_FAILED", exception: exception);
            }

            try
            {
                signalWorker(config.MachineId);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Threading.WaitHandleCannotBeOpenedException)
            {
                log.Write("ERROR", "WORKER_SIGNAL_FAILED", exception: exception);
            }
        }

        return 0;
    }

    public static HookHandler CreateProduction(string bridgeExecutablePath) => new(
        new DpapiSecretProtector(),
        VendorExecutableValidator.ForCurrentUser(),
        _ =>
        {
            var start = new ProcessStartInfo
            {
                FileName = bridgeExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Environment.CurrentDirectory,
            };
            start.ArgumentList.Add("worker");
            Process.Start(start);
        },
        WorkerCoordination.SignalExistingOrCreate);

    private void InvokeUpstream(InstallationLayout layout, QueueStore? queue, OperationalLog log, string originalNotifyJson)
    {
        try
        {
            var upstream = new ProtectedJsonStore<UpstreamRecord>(layout.UpstreamPath, protector).Load();
            var result = new UpstreamLauncher(vendorValidator).Launch(upstream, originalNotifyJson);
            if (!result.Success)
            {
                SafeUpsertHealth(queue, HealthCodes.UpstreamBlocked);
                log.Write("ERROR", result.OperationCode);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or InvalidDataException)
        {
            SafeUpsertHealth(queue, HealthCodes.UpstreamBlocked);
            log.Write("ERROR", "UPSTREAM_STATE_INVALID", exception: exception);
        }
    }

    private static void SafeUpsertHealth(QueueStore? queue, string code)
    {
        try
        {
            queue?.UpsertHealth(code, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or InvalidDataException)
        {
        }
    }
}
