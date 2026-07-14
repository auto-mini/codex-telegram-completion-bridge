using System.Globalization;
using System.Text;
using System.Text.Json;
using CodexTelegramCommon;

namespace CodexTelegramCtl;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            return args switch
            {
            ["install", "--plan", var path, .. var options] => CreateInstallPlan(path, options),
            ["install", "--apply", var path] => ApplyInstall(path),
            ["doctor", .. var options] => await RunDoctorAsync(options).ConfigureAwait(false),
            ["telegram", "bootstrap"] => await BootstrapTelegramAsync().ConfigureAwait(false),
            ["telegram", "migrate-bot"] => await BootstrapTelegramAsync(migrateExistingBot: true).ConfigureAwait(false),
            ["telegram", "configure-chat", var chatId] => await ConfigureChatAsync(chatId, reconfigure: false).ConfigureAwait(false),
            ["telegram", "reconfigure", var chatId] => await ConfigureChatAsync(chatId, reconfigure: true).ConfigureAwait(false),
            ["enable-live"] => EnableLive(),
            ["pause"] => Pause(),
            ["resume", .. var options] => Resume(options),
            ["shadow", "list"] => ListShadow(),
            ["shadow", "verify", var sequence] => VerifyShadow(sequence),
            ["quarantine", "list"] => ListQuarantine(),
            ["quarantine", "acknowledge", var sequence] => AcknowledgeQuarantine(sequence),
            ["repair"] => RunRepair(),
            ["upstream", "adopt", var path] => AdoptUpstream(path),
            ["uninstall", .. var options] => RunUninstall(options),
            ["maintenance", "clear-local-state"] => ClearLocalState(),
            ["cleanup", "--request", var request, "--parent", var parent, "--self", var self] => RunCleanup(request, parent, self),
            ["help"] or [] => ShowHelp(),
                _ => UsageError(),
            };
        }
        catch (TelegramConfigurationException exception)
        {
            return Fail(exception.OperationCode, 4);
        }
        catch (InstallApplyException exception)
        {
            Console.Error.WriteLine($"{exception.OperationCode} (rollback={(exception.RolledBack ? "complete" : "manual-recovery-required")})");
            return 5;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException or PlatformNotSupportedException or System.Security.Cryptography.CryptographicException or Microsoft.Data.Sqlite.SqliteException or JsonException or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception)
        {
            return Fail(SafeOperationCode(exception.Message), 3);
        }
    }

    private static int CreateInstallPlan(string planPath, string[] options)
    {
        string? packageRoot = null;
        string? installRoot = null;
        string? codexHome = null;
        string? pcAlias = null;
        for (var index = 0; index < options.Length; index += 2)
        {
            if (index + 1 >= options.Length)
            {
                return UsageError();
            }

            switch (options[index])
            {
                case "--package-root":
                    packageRoot = options[index + 1];
                    break;
                case "--install-root":
                    installRoot = options[index + 1];
                    break;
                case "--codex-home":
                    codexHome = options[index + 1];
                    break;
                case "--pc-alias":
                    pcAlias = options[index + 1];
                    break;
                default:
                    return UsageError();
            }
        }

        packageRoot ??= DefaultPackageRoot();
        var plan = InstallPlanner.CreateProduction().Create(new InstallPlannerOptions(packageRoot, installRoot, codexHome, pcAlias));
        InstallPlanStore.Save(planPath, plan);
        Console.WriteLine($"plan_created={Path.GetFullPath(planPath)}");
        Console.WriteLine($"applicable={(plan.NotifyClassification != NotifyClassification.Conflict ? "yes" : "no")}");
        Console.WriteLine($"notify={plan.NotifyClassification.ToString().ToLowerInvariant()}");
        Console.WriteLine($"desktop_closed={(plan.DesktopProcessesRunning ? "no" : "yes")}");
        return plan.NotifyClassification == NotifyClassification.Conflict ? 2 : 0;
    }

    private static int ApplyInstall(string planPath)
    {
        var result = InstallApplier.CreateProduction().Apply(planPath);
        Console.WriteLine("install=complete");
        Console.WriteLine($"mode={(result.WasUpgrade ? "upgrade" : "new")}");
        Console.WriteLine("capture=shadow");
        return 0;
    }

    private static async Task<int> RunDoctorAsync(string[] options)
    {
        var online = options.Contains("--online", StringComparer.Ordinal);
        var json = options.Contains("--json", StringComparer.Ordinal);
        if (options.Any(option => option is not ("--online" or "--json")))
        {
            return UsageError();
        }

        var report = await DoctorService.CreateProduction()
            .RunAsync(InstallationLayout.DefaultForCurrentUser(), online, CancellationToken.None)
            .ConfigureAwait(false);
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, JsonDefaults.Options));
        }
        else
        {
            Console.WriteLine($"overall={report.Overall}");
            Console.WriteLine($"conditions={(report.Conditions.Count == 0 ? "none" : string.Join(',', report.Conditions))}");
            Console.WriteLine($"capture={report.CaptureMode?.ToString().ToLowerInvariant() ?? "unknown"}");
            Console.WriteLine($"delivery_paused={report.DeliveryPaused?.ToString().ToLowerInvariant() ?? "unknown"}");
            if (report.Queue is not null)
            {
                Console.WriteLine($"queue=pending:{report.Queue.Pending},inflight:{report.Queue.Inflight},quarantine:{report.Queue.Quarantine},shadow:{report.Queue.Shadow},sent:{report.Queue.Sent},suppressed:{report.Queue.Suppressed}");
            }
        }

        return report.Overall == "BLOCKED" ? 2 : report.Overall == "DEGRADED" ? 1 : 0;
    }

    private static async Task<int> BootstrapTelegramAsync(bool migrateExistingBot = false)
    {
        var layout = InstallationLayout.DefaultForCurrentUser();
        var token = ReadNoEcho("BotFather 토큰: ");
        Console.WriteLine();
        var chatId = await TelegramConfigurator.CreateProduction().BootstrapFirstAsync(
            layout,
            token,
            challenge =>
            {
                Console.WriteLine("Telegram의 전용 봇 1:1 채팅에서 /start 후 아래 문자열을 정확히 보내세요.");
                Console.WriteLine(challenge);
            },
            CancellationToken.None,
            migrateExistingBot).ConfigureAwait(false);
        Console.WriteLine("telegram=connected");
        Console.WriteLine($"chat_id={chatId.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine(migrateExistingBot ? "delivery_paused=unchanged" : "capture=shadow");
        return 0;
    }

    private static async Task<int> ConfigureChatAsync(string chatIdText, bool reconfigure)
    {
        if (!long.TryParse(chatIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chatId) || chatId == 0)
        {
            return Fail("CHAT_ID_INVALID", 2);
        }

        var token = ReadNoEcho("BotFather 토큰: ");
        Console.WriteLine();
        await TelegramConfigurator.CreateProduction().ConfigureKnownChatAsync(
            InstallationLayout.DefaultForCurrentUser(),
            token,
            chatId,
            reconfigure,
            CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine("telegram=connected");
        Console.WriteLine("delivery_paused=unchanged");
        return 0;
    }

    private static int EnableLive()
    {
        var layout = InstallationLayout.DefaultForCurrentUser();
        Control(layout).EnableLive(layout);
        Console.WriteLine("capture=live");
        return 0;
    }

    private static int Pause()
    {
        var layout = InstallationLayout.DefaultForCurrentUser();
        Control(layout).Pause(layout);
        Console.WriteLine("delivery_paused=true");
        return 0;
    }

    private static int Resume(string[] options)
    {
        if (options.Any(option => option != "--yes"))
        {
            return UsageError();
        }

        var layout = InstallationLayout.DefaultForCurrentUser();
        var queue = new QueueStore(layout.DatabasePath);
        var counts = queue.GetCounts();
        var oldest = queue.GetOldestPendingUtc();
        Console.WriteLine($"pending={counts.Pending + counts.Inflight}");
        Console.WriteLine($"oldest_age_seconds={(oldest is null ? 0 : Math.Max(0, (long)(DateTimeOffset.UtcNow - oldest.Value).TotalSeconds))}");
        if (!options.Contains("--yes", StringComparer.Ordinal) && !Confirm("보류된 알림 전송을 재개할까요? [y/N] "))
        {
            Console.WriteLine("resume=cancelled");
            return 1;
        }

        Control(layout).Resume(layout);
        Console.WriteLine("delivery_paused=false");
        return 0;
    }

    private static int ListShadow()
    {
        var layout = InstallationLayout.DefaultForCurrentUser();
        foreach (var item in Control(layout).ListShadow(layout))
        {
            Console.WriteLine($"{item.Sequence}\t{item.ObservedAtUtc:O}\t{item.EventId[..Math.Min(item.EventId.Length, BridgeConstants.EventIdLogPrefixLength)]}");
        }

        return 0;
    }

    private static int VerifyShadow(string sequenceText)
    {
        if (!long.TryParse(sequenceText, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) || sequence < 1)
        {
            return Fail("SHADOW_SEQUENCE_INVALID", 2);
        }

        var expectedPc = ReadNoEcho("Expected PC name: ");
        Console.WriteLine();
        var expectedTitle = ReadNoEcho($"Visible task title (full title if shorter than {BridgeConstants.MinTitleVerificationPrefixGraphemes} graphemes; otherwise a {BridgeConstants.MinTitleVerificationPrefixGraphemes}+ grapheme prefix; trailing ellipsis is optional): ");
        Console.WriteLine();
        var layout = InstallationLayout.DefaultForCurrentUser();
        Console.WriteLine(Control(layout).VerifyShadow(layout, sequence, expectedPc, expectedTitle) ? "MATCH" : "MISMATCH");
        return 0;
    }

    private static int ListQuarantine()
    {
        var layout = InstallationLayout.DefaultForCurrentUser();
        foreach (var item in Control(layout).ListQuarantine(layout))
        {
            Console.WriteLine($"{item.Sequence}\t{item.ObservedAtUtc:O}\t{item.IngestMode.ToString().ToLowerInvariant()}\t{item.ErrorCode}\t{item.EventId[..BridgeConstants.EventIdLogPrefixLength]}");
        }

        return 0;
    }

    private static int AcknowledgeQuarantine(string sequenceText)
    {
        if (!long.TryParse(sequenceText, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) || sequence < 1)
        {
            return Fail("QUARANTINE_SEQUENCE_INVALID", 2);
        }

        var layout = InstallationLayout.DefaultForCurrentUser();
        var control = Control(layout);
        var item = control.ListQuarantine(layout).FirstOrDefault(candidate => candidate.Sequence == sequence);
        if (item is null)
        {
            return Fail("QUARANTINE_SEQUENCE_NOT_FOUND", 2);
        }

        Console.WriteLine($"sequence={item.Sequence}");
        Console.WriteLine($"observed={item.ObservedAtUtc:O}");
        Console.WriteLine($"mode={item.IngestMode.ToString().ToLowerInvariant()}");
        Console.WriteLine($"reason={item.ErrorCode}");
        Console.WriteLine($"event={item.EventId[..BridgeConstants.EventIdLogPrefixLength]}");
        if (!Confirm("Acknowledge and suppress this quarantined event? [y/N] "))
        {
            Console.WriteLine("quarantine=unchanged");
            return 1;
        }

        control.AcknowledgeQuarantine(layout, item.EventId);
        Console.WriteLine("quarantine=acknowledged_and_suppressed");
        return 0;
    }

    private static int RunRepair()
    {
        var result = RepairService.CreateProduction().Run(InstallationLayout.DefaultForCurrentUser());
        Console.WriteLine($"repair={result.Outcome.ToString().ToLowerInvariant()}");
        Console.WriteLine($"code={result.OperationCode}");
        return result.Outcome is RepairOutcome.Healthy or RepairOutcome.Repaired ? 0 : 2;
    }

    private static int AdoptUpstream(string path)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            return Fail("VENDOR_PATH_UNSUPPORTED", 2);
        }

        UpstreamAdoptionService.CreateProduction().Adopt(InstallationLayout.DefaultForCurrentUser(), path);
        Console.WriteLine("upstream=adopted");
        return 0;
    }

    private static int RunUninstall(string[] options)
    {
        if (options.Any(option => option is not ("--keep-config-conflict" or "--purge-state")))
        {
            return UsageError();
        }

        var keepConflict = options.Contains("--keep-config-conflict", StringComparer.Ordinal);
        var purge = options.Contains("--purge-state", StringComparer.Ordinal);
        if (!Confirm(purge
                ? "Codex Telegram Bridge와 모든 로컬 상태를 영구 삭제할까요? [y/N] "
                : "Codex Telegram Bridge를 제거하고 상태·백업은 보존할까요? [y/N] "))
        {
            Console.WriteLine("uninstall=cancelled");
            return 1;
        }

        var result = UninstallService.CreateProduction().Run(InstallationLayout.DefaultForCurrentUser(), keepConflict, purge);
        if (result.Outcome == UninstallOutcome.Conflict || result.CleanupRequestPath is null)
        {
            return Fail(result.OperationCode, 2);
        }

        ScheduleCleanup(result.CleanupRequestPath);
        Console.WriteLine("uninstall=config-restored; cleanup=scheduled");
        return 0;
    }

    private static int RunCleanup(string request, string parentText, string self)
    {
        if (!int.TryParse(parentText, NumberStyles.None, CultureInfo.InvariantCulture, out var parent) || parent <= 0)
        {
            return 2;
        }

        return CleanupExecutor.Execute(request, parent, self);
    }

    private static int ClearLocalState()
    {
        LocalStateMaintenance.ClearAfterVerifiedRepair(InstallationLayout.DefaultForCurrentUser());
        Console.WriteLine("local_state=verified_and_cleared");
        return 0;
    }

    private static void ScheduleCleanup(string requestPath)
    {
        var source = Environment.ProcessPath ?? throw new InvalidOperationException("PROCESS_PATH_UNAVAILABLE");
        var temporary = Path.Combine(Path.GetTempPath(), $"CodexTelegramCtl-cleanup-{Guid.NewGuid():N}.exe");
        File.Copy(source, temporary, overwrite: false);
        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = temporary,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("cleanup");
        start.ArgumentList.Add("--request");
        start.ArgumentList.Add(requestPath);
        start.ArgumentList.Add("--parent");
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add("--self");
        start.ArgumentList.Add(temporary);
        _ = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("CLEANUP_HELPER_START_FAILED");
    }

    private static CaptureControl Control(InstallationLayout layout) =>
        CaptureControl.CreateProduction(Path.Combine(layout.Bin, "CodexTelegramBridge.exe"));

    private static string ReadNoEcho(string prompt)
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException("NO_ECHO_TTY_REQUIRED");
        }

        Console.Write(prompt);
        var value = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length > 0)
                {
                    value.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                value.Append(key.KeyChar);
            }
        }

        return value.ToString();
    }

    private static bool Confirm(string prompt)
    {
        Console.Write(prompt);
        var answer = Console.ReadLine();
        return string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string DefaultPackageRoot()
    {
        var directory = new DirectoryInfo(Path.GetFullPath(AppContext.BaseDirectory));
        return directory.Name.Equals("bin", StringComparison.OrdinalIgnoreCase) && directory.Parent is not null
            ? directory.Parent.FullName
            : directory.FullName;
    }

    private static int ShowHelp()
    {
        Console.WriteLine("CodexTelegramCtl install --plan <file> [--package-root <dir>] [--pc-alias <name>]");
        Console.WriteLine("CodexTelegramCtl install --apply <file>");
        Console.WriteLine("CodexTelegramCtl doctor [--online] [--json]");
        Console.WriteLine("CodexTelegramCtl telegram bootstrap|migrate-bot|configure-chat <chat-id>|reconfigure <chat-id>");
        Console.WriteLine("CodexTelegramCtl enable-live|pause|resume [--yes]");
        Console.WriteLine("CodexTelegramCtl shadow list|verify <sequence>");
        Console.WriteLine("CodexTelegramCtl quarantine list|acknowledge <sequence>");
        Console.WriteLine("CodexTelegramCtl repair|upstream adopt <absolute-path>");
        Console.WriteLine("CodexTelegramCtl uninstall [--keep-config-conflict] [--purge-state]");
        Console.WriteLine("CodexTelegramCtl maintenance clear-local-state");
        return 0;
    }

    private static int UsageError()
    {
        Console.Error.WriteLine("INVALID_ARGUMENTS; run 'CodexTelegramCtl help'");
        return 2;
    }

    private static int Fail(string operationCode, int exitCode)
    {
        Console.Error.WriteLine(operationCode);
        return exitCode;
    }

    private static string SafeOperationCode(string message) =>
        message.Length is > 0 and <= 80 && message.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')
            ? message
            : "OPERATION_FAILED";
}
