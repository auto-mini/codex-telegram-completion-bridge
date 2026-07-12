namespace CodexTelegramCommon;

public sealed class UpstreamAdoptionService(
    ISecretProtector protector,
    VendorExecutableValidator validator,
    Func<DateTimeOffset> utcNow,
    Func<string> currentSid)
{
    public static UpstreamAdoptionService CreateProduction() => new(
        new DpapiSecretProtector(),
        VendorExecutableValidator.ForCurrentUser(),
        () => DateTimeOffset.UtcNow,
        () => CurrentUserContext.Sid);

    public void Adopt(InstallationLayout layout, string executablePath)
    {
        using var mutationLock = new InstallationMutationLock(currentSid());
        if (!mutationLock.TryAcquire())
        {
            throw new InvalidOperationException("INSTALL_MUTATION_BUSY");
        }

        var acl = WindowsAclManager.VerifyTree(layout.Root, currentSid());
        if (!acl.IsValid)
        {
            throw new UnauthorizedAccessException(HealthCodes.InstallAclBlocked);
        }

        var runtime = new RuntimeConfigStore(layout.RuntimeConfigPath).Load();
        var configPath = Path.Combine(runtime.CodexHome, "config.toml");
        var bytes = File.ReadAllBytes(configPath);
        var notify = CodexConfigDocument.Parse(bytes).NotifyArgv;
        var match = BridgeNotifyCommand.Match(notify, Path.Combine(layout.Bin, "CodexTelegramBridge.exe"));
        var active = match.Shape == BridgeNotifyShape.Direct ||
                     match.Shape == BridgeNotifyShape.VendorWrapped &&
                     validator.ValidateArgv(match.OuterVendorArgv ?? [], Hashing.Sha256Hex(bytes), utcNow()).IsValid;
        if (!active)
        {
            throw new InvalidOperationException(HealthCodes.ConfigConflict);
        }

        var result = validator.ValidateArgv(
            [Path.GetFullPath(executablePath), BridgeConstants.VendorArgument],
            Hashing.Sha256Hex(bytes),
            utcNow());
        if (!result.IsValid || result.Record is null)
        {
            throw new InvalidOperationException(result.OperationCode);
        }

        var launch = new UpstreamLauncher(validator).Launch(result.Record, "{\"type\":\"codex-telegram-upstream-adoption-test\"}");
        if (!launch.Success)
        {
            throw new InvalidOperationException(launch.OperationCode);
        }

        new ProtectedJsonStore<UpstreamRecord>(layout.UpstreamPath, protector).Save(result.Record);
        new QueueStore(layout.DatabasePath).ClearHealth(HealthCodes.UpstreamBlocked);
    }
}
