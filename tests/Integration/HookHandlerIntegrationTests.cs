using CodexTelegramCommon;
using System.Text;
using System.Text.Json;

namespace CodexTelegramIntegrationTests;

[Collection("Process environment")]
public sealed class HookHandlerIntegrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "HookHandlerTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Valid_completion_is_durable_and_starts_worker_once()
    {
        var layout = PrepareLayout();
        var starts = 0;
        var signals = 0;
        var handler = new HookHandler(new ReversingProtector(), new VendorExecutableValidator(root), _ => starts++, _ => signals++);
        var thread = Guid.NewGuid().ToString("D");
        var turn = Guid.NewGuid().ToString("D");
        var payload = $"{{\"type\":\"agent-turn-complete\",\"thread-id\":\"{thread}\",\"turn-id\":\"{turn}\",\"last-assistant-message\":\"must-not-persist\"}}";

        Assert.Equal(0, handler.Handle(layout, payload));

        var queue = new QueueStore(layout.DatabasePath);
        var counts = queue.GetCounts();
        Assert.Equal(1, counts.Pending);
        Assert.Equal(1, starts);
        Assert.Equal(1, signals);
        Assert.DoesNotContain(
            "must-not-persist",
            Encoding.UTF8.GetString(File.ReadAllBytes(layout.DatabasePath)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_event_is_forwarded_but_not_enqueued_or_started()
    {
        var layout = PrepareLayout();
        var starts = 0;
        var handler = new HookHandler(new ReversingProtector(), new VendorExecutableValidator(root), _ => starts++, _ => { });

        handler.Handle(layout, "{\"type\":\"other\"}");

        Assert.Equal(0, new QueueStore(layout.DatabasePath).GetCounts().Pending);
        Assert.Equal(0, starts);
    }

    [Fact]
    public void Local_state_block_marker_routes_new_completion_to_emergency_spool()
    {
        var layout = PrepareLayout();
        AtomicFile.WriteUtf8(layout.LocalStateBlockedMarkerPath, "LOCAL_STATE_BLOCKED\n");
        var handler = new HookHandler(new ReversingProtector(), new VendorExecutableValidator(root), _ => { }, _ => { });
        var thread = Guid.NewGuid().ToString("D");
        var turn = Guid.NewGuid().ToString("D");

        handler.Handle(layout, $"{{\"type\":\"agent-turn-complete\",\"thread-id\":\"{thread}\",\"turn-id\":\"{turn}\"}}");

        Assert.Equal(0, new QueueStore(layout.DatabasePath).GetCounts().Pending);
        Assert.Equal(1, new EmergencySpool(layout.SpoolDirectory).CountPending());
    }

    [Fact]
    public async Task Vendor_wrapped_bridge_does_not_launch_captured_vendor_a_second_time()
    {
        var layout = PrepareLayout();
        var vendorDirectory = Path.Combine(root, "vendor");
        Directory.CreateDirectory(vendorDirectory);
        var vendor = Path.Combine(vendorDirectory, BridgeConstants.VendorExecutableName);
        File.Copy(LocateTestVendor(), vendor);
        var validator = new VendorExecutableValidator(root);
        var captured = validator.ValidateArgv(
            [vendor, BridgeConstants.VendorArgument],
            new string('a', 64),
            DateTimeOffset.UtcNow);
        Assert.True(captured.IsValid);
        new ProtectedJsonStore<UpstreamRecord>(layout.UpstreamPath, new ReversingProtector()).Save(captured.Record!);

        var runtime = new RuntimeConfigStore(layout.RuntimeConfigPath).Load();
        Directory.CreateDirectory(runtime.CodexHome);
        var bridge = Path.Combine(layout.Bin, "CodexTelegramBridge.exe");
        var previous = JsonSerializer.Serialize(new[] { bridge, "hook" });
        File.WriteAllBytes(
            Path.Combine(runtime.CodexHome, "config.toml"),
            CodexConfigDocument.Parse([]).RenderWithNotify(
                [vendor, BridgeConstants.VendorArgument, BridgeConstants.VendorPreviousNotifyArgument, previous]));

        var output = Path.Combine(root, "unexpected-vendor-observation.json");
        var previousOutput = Environment.GetEnvironmentVariable("CODEX_TELEGRAM_TEST_VENDOR_OUTPUT");
        try
        {
            Environment.SetEnvironmentVariable("CODEX_TELEGRAM_TEST_VENDOR_OUTPUT", output);
            var handler = new HookHandler(new ReversingProtector(), validator, _ => { }, _ => { });

            handler.Handle(layout, "{\"type\":\"other\"}");
            await Task.Delay(500);

            Assert.False(File.Exists(output));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_TELEGRAM_TEST_VENDOR_OUTPUT", previousOutput);
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private InstallationLayout PrepareLayout()
    {
        var layout = new InstallationLayout(root);
        layout.EnsureMutableDirectories();
        var config = new RuntimeConfig
        {
            MachineId = Guid.NewGuid().ToString("D"),
            CodexHome = Path.Combine(root, ".codex"),
            CaptureMode = CaptureMode.Shadow,
        };
        new RuntimeConfigStore(layout.RuntimeConfigPath).Save(config);
        new QueueStore(layout.DatabasePath).Initialize();
        new ProtectedJsonStore<UpstreamRecord>(layout.UpstreamPath, new ReversingProtector()).Save(new UpstreamRecord(
            BridgeConstants.SchemaVersion,
            [],
            null,
            null,
            DateTimeOffset.UtcNow,
            new string('0', 64),
            UpstreamKind.Absent));
        return layout;
    }

    private static string LocateTestVendor()
    {
        var configuration = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "TestVendor", "bin", configuration, "net8.0-windows", BridgeConstants.VendorExecutableName));
        Assert.True(File.Exists(path), $"Test vendor was not built: {path}");
        return path;
    }

    private sealed class ReversingProtector : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plaintext)
        {
            var bytes = plaintext.ToArray();
            Array.Reverse(bytes);
            return bytes;
        }

        public byte[] Unprotect(ReadOnlySpan<byte> ciphertext) => Protect(ciphertext);
    }
}
