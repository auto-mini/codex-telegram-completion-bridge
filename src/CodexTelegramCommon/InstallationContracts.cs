using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

namespace CodexTelegramCommon;

public enum NotifyClassification
{
    Absent,
    RecognizedVendor,
    HealthyBridge,
    Conflict,
}

public enum InstallationState
{
    Active,
    RolledBack,
}

public sealed record InstallPlan(
    int SchemaVersion,
    string PlanId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string MachineId,
    string UserSid,
    string Architecture,
    string CodexHome,
    string ConfigPath,
    bool ConfigExisted,
    string ConfigSha256,
    NotifyClassification NotifyClassification,
    IReadOnlyList<string> RedactedNotifyArgv,
    string InstallationRoot,
    string BridgeExecutablePath,
    string ControlExecutablePath,
    string PackageRoot,
    string ManifestSha256,
    string? ExistingInstallationRecordSha256,
    string? ExistingRuntimeConfigSha256,
    string? ExistingUpstreamSha256,
    string BackupFileName,
    string? PcAlias,
    bool DesktopProcessesRunning,
    IReadOnlyList<string> ScheduledTaskChanges)
{
    public void Validate()
    {
        if (SchemaVersion != BridgeConstants.SchemaVersion ||
            !Guid.TryParseExact(PlanId, "D", out _) ||
            !Guid.TryParseExact(MachineId, "D", out _) ||
            ExpiresAtUtc <= CreatedAtUtc || ExpiresAtUtc - CreatedAtUtc > TimeSpan.FromMinutes(31) ||
            string.IsNullOrWhiteSpace(UserSid) ||
            !string.Equals(Architecture, "win-x64", StringComparison.Ordinal) ||
            !Path.IsPathFullyQualified(CodexHome) ||
            !Path.IsPathFullyQualified(ConfigPath) ||
            !Path.IsPathFullyQualified(InstallationRoot) ||
            !Path.IsPathFullyQualified(BridgeExecutablePath) ||
            !Path.IsPathFullyQualified(ControlExecutablePath) ||
            !Path.IsPathFullyQualified(PackageRoot) ||
            !IsSha256(ConfigSha256) ||
            !IsSha256(ManifestSha256) ||
            ExistingInstallationRecordSha256 is not null && !IsSha256(ExistingInstallationRecordSha256) ||
            ExistingRuntimeConfigSha256 is not null && !IsSha256(ExistingRuntimeConfigSha256) ||
            ExistingUpstreamSha256 is not null && !IsSha256(ExistingUpstreamSha256) ||
            Path.GetFileName(BackupFileName) != BackupFileName ||
            string.IsNullOrWhiteSpace(BackupFileName) ||
            NotifyClassification == NotifyClassification.Conflict && RedactedNotifyArgv.Count == 0)
        {
            throw new InvalidDataException("Install plan failed validation.");
        }

        if (PcAlias is not null && TextNormalizer.NormalizePcName(PcAlias) is null)
        {
            throw new InvalidDataException("PC alias is empty after normalization.");
        }
    }

    private static bool IsSha256(string value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

public sealed record InstallationRecord(
    int SchemaVersion,
    string MachineId,
    string UserSid,
    string ManifestSha256,
    string ConfigPath,
    DateTimeOffset InstalledAtUtc,
    InstallationState State)
{
    public void Validate()
    {
        if (SchemaVersion != BridgeConstants.SchemaVersion ||
            !Guid.TryParseExact(MachineId, "D", out _) ||
            string.IsNullOrWhiteSpace(UserSid) ||
            ManifestSha256 is not { Length: 64 } ||
            !ManifestSha256.All(Uri.IsHexDigit) ||
            !Path.IsPathFullyQualified(ConfigPath))
        {
            throw new InvalidDataException("Installation record failed validation.");
        }
    }
}

public sealed record ConfigBackupRecord(
    int SchemaVersion,
    string ConfigPath,
    bool Existed,
    string Sha256,
    byte[] Content,
    DateTimeOffset CapturedAtUtc)
{
    public void Validate()
    {
        if (SchemaVersion != BridgeConstants.SchemaVersion ||
            !Path.IsPathFullyQualified(ConfigPath) ||
            Sha256 is not { Length: 64 } ||
            !Sha256.All(Uri.IsHexDigit) ||
            !string.Equals(Hashing.Sha256Hex(Content), Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Config backup failed validation.");
        }
    }
}

public enum JournalKind
{
    Install,
    Repair,
    Uninstall,
}

public sealed record TransactionJournal(
    int SchemaVersion,
    string TransactionId,
    JournalKind Kind,
    string Phase,
    string PlanSha256,
    string ConfigBeforeSha256,
    string? ConfigAfterSha256,
    string InstallationRoot,
    string BackupPath,
    DateTimeOffset UpdatedAtUtc)
{
    public void Validate()
    {
        if (SchemaVersion != BridgeConstants.SchemaVersion ||
            !Guid.TryParseExact(TransactionId, "D", out _) ||
            string.IsNullOrWhiteSpace(Phase) || Phase.Any(character => !(character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')) ||
            PlanSha256 is not { Length: 64 } || !PlanSha256.All(Uri.IsHexDigit) ||
            ConfigBeforeSha256 is not { Length: 64 } || !ConfigBeforeSha256.All(Uri.IsHexDigit) ||
            ConfigAfterSha256 is not null && (ConfigAfterSha256.Length != 64 || !ConfigAfterSha256.All(Uri.IsHexDigit)) ||
            !Path.IsPathFullyQualified(InstallationRoot) ||
            !Path.IsPathFullyQualified(BackupPath))
        {
            throw new InvalidDataException("Transaction journal failed validation.");
        }
    }
}

public static class CurrentUserContext
{
    public static string Sid => WindowsIdentity.GetCurrent().User?.Value
                                ?? throw new InvalidOperationException("Current Windows user SID is unavailable.");

    public static void EnsureSupportedHost()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.OSVersion.Version.Major < 10 ||
            RuntimeInformation.OSArchitecture != Architecture.X64 ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException("V1 requires x64 Windows 10 or Windows 11.");
        }
    }

    public static string ResolveCodexHome()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        var value = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")
            : configured;
        if (!Path.IsPathFullyQualified(value))
        {
            throw new InvalidDataException("CODEX_HOME must resolve to an absolute path.");
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    }
}

public static class InstallPlanStore
{
    public static InstallPlan Load(string path)
    {
        var plan = JsonSerializer.Deserialize<InstallPlan>(AtomicFile.ReadUtf8(path), JsonDefaults.Options)
                   ?? throw new InvalidDataException("Install plan is empty.");
        plan.Validate();
        return plan;
    }

    public static void Save(string path, InstallPlan plan)
    {
        plan.Validate();
        AtomicFile.WriteUtf8(path, JsonSerializer.Serialize(plan, JsonDefaults.Options));
    }
}
