namespace CodexTelegramCommon;

public sealed record MinimalEvent(
    int SchemaVersion,
    string EventId,
    string MachineId,
    string ThreadId,
    string TurnId,
    DateTimeOffset ObservedAtUtc,
    CaptureMode IngestMode);

public sealed record DeliveryEnvelope(
    int SchemaVersion,
    string PcName,
    string ThreadTitle,
    string TelegramText);

public sealed record TelegramCredentials(
    int SchemaVersion,
    string BotToken,
    long BotUserId,
    long ChatId,
    string ChatType)
{
    public void Validate()
    {
        if (SchemaVersion != BridgeConstants.SchemaVersion ||
            string.IsNullOrWhiteSpace(BotToken) || BotToken.Length > 256 ||
            BotToken.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)) ||
            BotUserId <= 0 ||
            ChatId == 0 ||
            !string.Equals(ChatType, "private", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Telegram credentials failed validation.");
        }
    }
}

public enum UpstreamKind
{
    Absent,
    CodexComputerUseTurnEnded,
}

public sealed record UpstreamRecord(
    int SchemaVersion,
    IReadOnlyList<string> Argv,
    string? ExecutableSha256,
    long? ExecutableSizeBytes,
    DateTimeOffset CapturedAtUtc,
    string CapturedConfigSha256,
    UpstreamKind Kind)
{
    public void ValidateShape()
    {
        if (SchemaVersion != BridgeConstants.SchemaVersion ||
            Argv is null ||
            CapturedConfigSha256 is not { Length: 64 } ||
            !CapturedConfigSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Upstream record metadata is invalid.");
        }

        if (Kind == UpstreamKind.Absent)
        {
            if (Argv.Count != 0 || ExecutableSha256 is not null || ExecutableSizeBytes is not null)
            {
                throw new InvalidDataException("Absent upstream record is invalid.");
            }

            return;
        }

        if (Kind != UpstreamKind.CodexComputerUseTurnEnded ||
            Argv.Count != 2 ||
            ExecutableSha256 is not { Length: 64 } ||
            !ExecutableSha256.All(Uri.IsHexDigit) ||
            ExecutableSizeBytes is null or < 0)
        {
            throw new InvalidDataException("Vendor upstream record is invalid.");
        }
    }
}

public sealed class RuntimeConfig
{
    public int SchemaVersion { get; init; } = BridgeConstants.SchemaVersion;

    public string MachineId { get; init; } = string.Empty;

    public string? PcAlias { get; init; }

    public string CodexHome { get; init; } = string.Empty;

    public CaptureMode CaptureMode { get; init; } = CaptureMode.Shadow;

    public bool DeliveryPaused { get; init; }

    public bool AutoRepairVendorNotify { get; init; } = true;

    public void Validate()
    {
        if (SchemaVersion != BridgeConstants.SchemaVersion)
        {
            throw new InvalidDataException("Unsupported runtime config schema.");
        }

        if (!Guid.TryParseExact(MachineId, "D", out _))
        {
            throw new InvalidDataException("Machine ID must be a canonical GUID.");
        }

        if (string.IsNullOrWhiteSpace(CodexHome) || !Path.IsPathFullyQualified(CodexHome))
        {
            throw new InvalidDataException("CODEX_HOME must be an absolute path.");
        }

        if (PcAlias is not null && TextNormalizer.NormalizePcName(PcAlias) is null)
        {
            throw new InvalidDataException("PC alias is empty after normalization.");
        }
    }
}

public sealed record StateResolution(
    ResolutionKind Kind,
    string? NormalizedTitle = null,
    string? ErrorCode = null);
