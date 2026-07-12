using System.Text.Json;

namespace CodexTelegramCommon;

public sealed record InstallPlannerOptions(
    string PackageRoot,
    string? InstallationRoot = null,
    string? CodexHome = null,
    string? PcAlias = null);

public sealed class InstallPlanner(
    VendorExecutableValidator vendorValidator,
    ISecretProtector protector,
    Func<DateTimeOffset> utcNow,
    Func<string> currentSid,
    Func<IReadOnlyList<string>> runningDesktopProcesses,
    Action ensureSupportedHost)
{
    public static InstallPlanner CreateProduction() => new(
        VendorExecutableValidator.ForCurrentUser(),
        new DpapiSecretProtector(),
        () => DateTimeOffset.UtcNow,
        () => CurrentUserContext.Sid,
        DesktopProcessGuard.FindRunning,
        CurrentUserContext.EnsureSupportedHost);

    public InstallPlan Create(InstallPlannerOptions options)
    {
        ensureSupportedHost();
        var now = utcNow();
        var package = PackageManifest.LoadAndVerify(options.PackageRoot);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            options.InstallationRoot ?? InstallationLayout.DefaultForCurrentUser().Root));
        var layout = new InstallationLayout(root);
        var codexHome = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.CodexHome ?? CurrentUserContext.ResolveCodexHome()));
        var configPath = Path.Combine(codexHome, "config.toml");
        var configExisted = File.Exists(configPath);
        var configBytes = configExisted ? File.ReadAllBytes(configPath) : [];
        var configHash = Hashing.Sha256Hex(configBytes);
        var document = CodexConfigDocument.Parse(configBytes);
        var bridgePath = Path.Combine(layout.Bin, "CodexTelegramBridge.exe");
        var controlPath = Path.Combine(layout.Bin, "CodexTelegramCtl.exe");
        var classification = ClassifyNotify(document.NotifyArgv, configHash, layout, configPath, bridgePath);
        var retainedRuntime = ReadRetainedRuntime(layout);
        var machineId = retainedRuntime?.MachineId ?? Guid.NewGuid().ToString("D");
        var normalizedAlias = options.PcAlias is null
            ? retainedRuntime?.PcAlias
            : TextNormalizer.NormalizePcName(options.PcAlias)
              ?? throw new InvalidDataException("PC alias is empty after normalization.");
        var running = runningDesktopProcesses();

        var plan = new InstallPlan(
            BridgeConstants.SchemaVersion,
            Guid.NewGuid().ToString("D"),
            now,
            now + TimeSpan.FromMinutes(30),
            machineId,
            currentSid(),
            "win-x64",
            codexHome,
            configPath,
            configExisted,
            configHash,
            classification,
            Redact(document.NotifyArgv, classification),
            root,
            bridgePath,
            controlPath,
            package.PackageRoot,
            package.ManifestSha256,
            HashIfExists(layout.TransactionRecordPath),
            HashIfExists(layout.RuntimeConfigPath),
            HashIfExists(layout.UpstreamPath),
            $"config-{now:yyyyMMddTHHmmssZ}-{configHash[..12]}.dpapi",
            normalizedAlias,
            running.Count > 0,
            [
                @"CodexTelegramBridge\Drain: create disabled, then enable after config commit",
                @"CodexTelegramBridge\Repair: create disabled, then enable after config commit",
            ]);
        plan.Validate();
        return plan;
    }

    private NotifyClassification ClassifyNotify(
        IReadOnlyList<string>? argv,
        string configHash,
        InstallationLayout layout,
        string configPath,
        string expectedBridgePath)
    {
        if (argv is null)
        {
            return NotifyClassification.Absent;
        }

        var bridgeMatch = BridgeNotifyCommand.Match(argv, expectedBridgePath);
        if (bridgeMatch.IsActive)
        {
            if (bridgeMatch.Shape == BridgeNotifyShape.VendorWrapped &&
                !vendorValidator.ValidateArgv(bridgeMatch.OuterVendorArgv ?? [], configHash, utcNow()).IsValid)
            {
                return NotifyClassification.Conflict;
            }

            return IsHealthyInstalledBridge(layout, configPath)
                ? NotifyClassification.HealthyBridge
                : NotifyClassification.Conflict;
        }

        if (argv.Count > 0 && string.Equals(Path.GetFileName(argv[0]), "CodexTelegramBridge.exe", StringComparison.OrdinalIgnoreCase))
        {
            return NotifyClassification.Conflict;
        }

        return vendorValidator.ValidateArgv(argv, configHash, utcNow()).IsValid
            ? NotifyClassification.RecognizedVendor
            : NotifyClassification.Conflict;
    }

    private bool IsHealthyInstalledBridge(InstallationLayout layout, string configPath)
    {
        try
        {
            var record = JsonSerializer.Deserialize<InstallationRecord>(AtomicFile.ReadUtf8(layout.TransactionRecordPath), JsonDefaults.Options)
                         ?? throw new InvalidDataException("Installation record is empty.");
            record.Validate();
            var runtime = new RuntimeConfigStore(layout.RuntimeConfigPath).Load();
            var upstream = new ProtectedJsonStore<UpstreamRecord>(layout.UpstreamPath, protector).Load();
            upstream.ValidateShape();
            new QueueStore(layout.DatabasePath).ValidateExistingSchema();
            var installedManifest = PackageManifest.LoadAndVerify(layout.Root, allowInstalledMutableFiles: true);
            return record.State == InstallationState.Active &&
                   string.Equals(record.UserSid, currentSid(), StringComparison.Ordinal) &&
                   string.Equals(record.ConfigPath, configPath, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(record.MachineId, runtime.MachineId, StringComparison.Ordinal) &&
                   string.Equals(record.ManifestSha256, installedManifest.ManifestSha256, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Security.Cryptography.CryptographicException or JsonException or Microsoft.Data.Sqlite.SqliteException)
        {
            return false;
        }
    }

    private static RuntimeConfig? ReadRetainedRuntime(InstallationLayout layout)
    {
        try
        {
            return new RuntimeConfigStore(layout.RuntimeConfigPath).Load();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            return null;
        }
    }

    internal static bool IsExactBridgeArgv(IReadOnlyList<string> argv, string expectedBridgePath) =>
        BridgeNotifyCommand.IsExactBridgeArgv(argv, expectedBridgePath);

    private static IReadOnlyList<string> Redact(IReadOnlyList<string>? argv, NotifyClassification classification) => classification switch
    {
        NotifyClassification.Absent => [],
        NotifyClassification.RecognizedVendor => [BridgeConstants.VendorExecutableName, BridgeConstants.VendorArgument],
        NotifyClassification.HealthyBridge => ["CodexTelegramBridge.exe", "hook"],
        _ => argv is null ? ["<unsupported-handler>"] : ["<unsupported-handler>", $"argc={argv.Count}"],
    };

    private static string? HashIfExists(string path) => File.Exists(path) ? Hashing.Sha256File(path) : null;
}
