using System.Security.Cryptography;

namespace CodexTelegramCommon;

public sealed class CaptureControl(
    ISecretProtector protector,
    Action<string> signalWorker,
    Action startWorker,
    Func<string>? currentSid = null,
    VendorExecutableValidator? vendorValidator = null,
    Func<DateTimeOffset>? utcNow = null)
{
    public static CaptureControl CreateProduction(string bridgeExecutablePath) => new(
        new DpapiSecretProtector(),
        WorkerCoordination.SignalExistingOrCreate,
        () =>
        {
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = bridgeExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("worker");
            System.Diagnostics.Process.Start(start);
        });

    public void EnableLive(InstallationLayout layout)
    {
        using var mutationLock = AcquireMutationLock();
        var credentials = new ProtectedJsonStore<TelegramCredentials>(layout.TelegramCredentialsPath, protector).Load();
        credentials.Validate();
        var store = new RuntimeConfigStore(layout.RuntimeConfigPath);
        var current = store.Load();
        if (current.CaptureMode != CaptureMode.Shadow)
        {
            throw new InvalidOperationException("CAPTURE_ALREADY_LIVE");
        }

        var queue = new QueueStore(layout.DatabasePath);
        queue.ValidateExistingSchema();
        var acl = WindowsAclManager.VerifyTree(layout.Root, (currentSid ?? (() => CurrentUserContext.Sid))());
        if (!acl.IsValid)
        {
            throw new InvalidOperationException(HealthCodes.InstallAclBlocked);
        }

        var configBytes = File.ReadAllBytes(Path.Combine(current.CodexHome, "config.toml"));
        var notify = CodexConfigDocument.Parse(configBytes).NotifyArgv;
        var match = BridgeNotifyCommand.Match(notify, Path.Combine(layout.Bin, "CodexTelegramBridge.exe"));
        var notifyActive = match.Shape == BridgeNotifyShape.Direct ||
                           match.Shape == BridgeNotifyShape.VendorWrapped &&
                           (vendorValidator ?? VendorExecutableValidator.ForCurrentUser()).ValidateArgv(
                               match.OuterVendorArgv ?? [],
                               Hashing.Sha256Hex(configBytes),
                               (utcNow ?? (() => DateTimeOffset.UtcNow))()).IsValid;
        if (!notifyActive)
        {
            throw new InvalidOperationException(HealthCodes.ConfigConflict);
        }

        var resolver = new CodexStateResolver(current.CodexHome);
        if (!resolver.HasAnyCompatibleDatabase() || resolver.Resolve(Guid.NewGuid().ToString("D")).Kind == ResolutionKind.Unsupported)
        {
            throw new InvalidOperationException(HealthCodes.StateSchemaBlocked);
        }

        if (queue.GetCounts().Quarantine > 0 || queue.GetHealthConditions().Any(condition =>
                condition.ConditionCode is HealthCodes.StateSchemaBlocked or HealthCodes.EventQuarantined or HealthCodes.SpoolCorrupt or HealthCodes.LocalStateBlocked or HealthCodes.InstallAclBlocked))
        {
            throw new InvalidOperationException("QUALIFICATION_HEALTH_NOT_CLEAR");
        }

        Save(store, current, CaptureMode.Live, current.DeliveryPaused);
    }

    public void Pause(InstallationLayout layout)
    {
        using var mutationLock = AcquireMutationLock();
        var store = new RuntimeConfigStore(layout.RuntimeConfigPath);
        var current = store.Load();
        Save(store, current, current.CaptureMode, deliveryPaused: true);
    }

    public void Resume(InstallationLayout layout)
    {
        using var mutationLock = AcquireMutationLock();
        var credentials = new ProtectedJsonStore<TelegramCredentials>(layout.TelegramCredentialsPath, protector).Load();
        credentials.Validate();
        var queue = new QueueStore(layout.DatabasePath);
        queue.ValidateExistingSchema();
        if (queue.HasNetworkBlockingHealthCondition())
        {
            throw new InvalidOperationException("NETWORK_HEALTH_BLOCKED");
        }

        var store = new RuntimeConfigStore(layout.RuntimeConfigPath);
        var current = store.Load();
        Save(store, current, current.CaptureMode, deliveryPaused: false);
        startWorker();
        signalWorker(current.MachineId);
    }

    public IReadOnlyList<ShadowRecord> ListShadow(InstallationLayout layout) => new QueueStore(layout.DatabasePath).ListShadow();

    public IReadOnlyList<QuarantineRecord> ListQuarantine(InstallationLayout layout) => new QueueStore(layout.DatabasePath).ListQuarantine();

    public void AcknowledgeQuarantine(InstallationLayout layout, string eventId)
    {
        using var mutationLock = AcquireMutationLock();
        var queue = new QueueStore(layout.DatabasePath);
        queue.ValidateExistingSchema();
        var acl = WindowsAclManager.VerifyTree(layout.Root, (currentSid ?? (() => CurrentUserContext.Sid))());
        if (!acl.IsValid)
        {
            throw new InvalidOperationException(HealthCodes.InstallAclBlocked);
        }

        queue.AcknowledgeQuarantine(eventId, (utcNow ?? (() => DateTimeOffset.UtcNow))());
    }

    public bool VerifyShadow(InstallationLayout layout, long sequence, string expectedPc, string expectedTitlePrefix)
    {
        var protectedEnvelope = new QueueStore(layout.DatabasePath).GetShadowEnvelope(sequence)
                                ?? throw new InvalidOperationException("SHADOW_SEQUENCE_NOT_FOUND");
        try
        {
            var envelope = ProtectedJsonCodec.Unprotect<DeliveryEnvelope>(protectedEnvelope, protector);
            var pc = TextNormalizer.NormalizePcName(expectedPc);
            var titlePrefix = TextNormalizer.NormalizeTitleVerificationPrefix(expectedTitlePrefix);
            return pc is not null && titlePrefix is not null &&
                   string.Equals(pc, envelope.PcName, StringComparison.Ordinal) &&
                   envelope.ThreadTitle.StartsWith(titlePrefix, StringComparison.Ordinal);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedEnvelope);
        }
    }

    private static void Save(RuntimeConfigStore store, RuntimeConfig current, CaptureMode mode, bool deliveryPaused) =>
        store.Save(new RuntimeConfig
        {
            MachineId = current.MachineId,
            PcAlias = current.PcAlias,
            CodexHome = current.CodexHome,
            CaptureMode = mode,
            DeliveryPaused = deliveryPaused,
            AutoRepairVendorNotify = current.AutoRepairVendorNotify,
        });

    private InstallationMutationLock AcquireMutationLock()
    {
        var mutationLock = new InstallationMutationLock((currentSid ?? (() => CurrentUserContext.Sid))());
        if (!mutationLock.TryAcquire())
        {
            mutationLock.Dispose();
            throw new InvalidOperationException("INSTALL_MUTATION_BUSY");
        }

        return mutationLock;
    }
}
