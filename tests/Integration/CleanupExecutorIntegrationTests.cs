using CodexTelegramCommon;

namespace CodexTelegramIntegrationTests;

public sealed class CleanupExecutorIntegrationTests : IDisposable
{
    private readonly string container = Path.Combine(Path.GetTempPath(), "CleanupExecutorTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Preserve_cleanup_removes_manifest_artifacts_but_keeps_mutable_state()
    {
        var root = PrepareRoot();
        var requestPath = Path.Combine(root, "state", "uninstall-cleanup.dpapi");
        var request = new CleanupRequest(
            BridgeConstants.SchemaVersion,
            root,
            false,
            ["bin/CodexTelegramBridge.exe", "bin/CodexTelegramCtl.exe"]);
        new ProtectedJsonStore<CleanupRequest>(requestPath, new DpapiSecretProtector()).Save(request);

        var exit = CleanupExecutor.Execute(requestPath, int.MaxValue);

        Assert.Equal(0, exit);
        Assert.False(File.Exists(Path.Combine(root, "bin", "CodexTelegramBridge.exe")));
        Assert.False(File.Exists(Path.Combine(root, "manifest.sha256")));
        Assert.False(File.Exists(requestPath));
        Assert.True(File.Exists(Path.Combine(root, "state", "bridge-state.sqlite")));
        Assert.True(File.Exists(Path.Combine(root, "config", "bridge.json")));
    }

    [Fact]
    public void Purge_cleanup_removes_the_authenticated_root()
    {
        var root = PrepareRoot();
        var requestPath = Path.Combine(root, "state", "uninstall-cleanup.dpapi");
        new ProtectedJsonStore<CleanupRequest>(requestPath, new DpapiSecretProtector()).Save(new CleanupRequest(
            BridgeConstants.SchemaVersion,
            root,
            true,
            ["bin/CodexTelegramBridge.exe"]));

        var exit = CleanupExecutor.Execute(requestPath, int.MaxValue);

        Assert.Equal(0, exit);
        Assert.False(Directory.Exists(root));
    }

    public void Dispose()
    {
        if (Directory.Exists(container))
        {
            Directory.Delete(container, recursive: true);
        }
    }

    private string PrepareRoot()
    {
        var root = Path.Combine(container, "CodexTelegramBridge");
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        Directory.CreateDirectory(Path.Combine(root, "state"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllText(Path.Combine(root, "bin", "CodexTelegramBridge.exe"), "bridge");
        File.WriteAllText(Path.Combine(root, "bin", "CodexTelegramCtl.exe"), "ctl");
        File.WriteAllText(Path.Combine(root, "state", "bridge-state.sqlite"), "state");
        File.WriteAllText(Path.Combine(root, "config", "bridge.json"), "config");
        File.WriteAllText(Path.Combine(root, "manifest.sha256"), "manifest");
        return root;
    }
}
