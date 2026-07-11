namespace CodexTelegramCommon;

public sealed record InstallationLayout(string Root)
{
    public string Bin => Path.Combine(Root, "bin");
    public string ConfigDirectory => Path.Combine(Root, "config");
    public string StateDirectory => Path.Combine(Root, "state");
    public string SpoolDirectory => Path.Combine(StateDirectory, "emergency-spool");
    public string CorruptSpoolDirectory => Path.Combine(SpoolDirectory, "corrupt");
    public string LogsDirectory => Path.Combine(Root, "logs");
    public string BackupsDirectory => Path.Combine(Root, "backups");
    public string RuntimeConfigPath => Path.Combine(ConfigDirectory, "bridge.json");
    public string UpstreamPath => Path.Combine(ConfigDirectory, "upstream.dpapi");
    public string TelegramCredentialsPath => Path.Combine(ConfigDirectory, "telegram-credentials.dpapi");
    public string DatabasePath => Path.Combine(StateDirectory, "bridge-state.sqlite");
    public string LocalStateBlockedMarkerPath => Path.Combine(StateDirectory, "local-state.blocked");
    public string QuickCheckStampPath => Path.Combine(StateDirectory, "last-quick-check.utc");
    public string WorkerStopMarkerPath => Path.Combine(StateDirectory, "worker-stop.request");
    public string LogPath => Path.Combine(LogsDirectory, "bridge.log");
    public string ManifestPath => Path.Combine(Root, "manifest.sha256");
    public string TransactionRecordPath => Path.Combine(Root, "transaction-final.json");
    public string ActiveJournalPath => Path.Combine(Path.GetDirectoryName(Root)!, "CodexTelegramBridge.install-journal");

    public static InstallationLayout DefaultForCurrentUser() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexTelegramBridge"));

    public static InstallationLayout FromExecutableBase(string baseDirectory)
    {
        var full = Path.GetFullPath(baseDirectory);
        var directory = new DirectoryInfo(full);
        var root = directory.Name.Equals("bin", StringComparison.OrdinalIgnoreCase)
            ? directory.Parent?.FullName
            : directory.FullName;
        return new InstallationLayout(root ?? throw new InvalidOperationException("Cannot derive installation root."));
    }

    public void EnsureMutableDirectories()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(StateDirectory);
        Directory.CreateDirectory(SpoolDirectory);
        Directory.CreateDirectory(CorruptSpoolDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
    }
}
