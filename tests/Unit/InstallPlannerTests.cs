using System.Text;
using CodexTelegramCommon;

namespace CodexTelegramUnitTests;

public sealed class InstallPlannerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "InstallPlannerTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Plan_is_read_only_and_classifies_absent_notify()
    {
        var fixture = CreateFixture("model = \"gpt\"\n");
        var before = Snapshot(root);

        var plan = fixture.Planner.Create(new InstallPlannerOptions(fixture.PackageRoot, fixture.InstallRoot, fixture.CodexHome, " My PC "));

        Assert.Equal(NotifyClassification.Absent, plan.NotifyClassification);
        Assert.Equal("My PC", plan.PcAlias);
        Assert.False(plan.DesktopProcessesRunning);
        Assert.Equal(before, Snapshot(root));
    }

    [Fact]
    public void Plan_recognizes_contained_vendor_and_redacts_its_path()
    {
        var fixture = CreateFixture(string.Empty);
        var vendor = Path.Combine(fixture.VendorRoot, BridgeConstants.VendorExecutableName);
        File.WriteAllText(vendor, "synthetic vendor");
        File.WriteAllText(Path.Combine(fixture.CodexHome, "config.toml"), $"notify = [{Quote(vendor)}, \"turn-ended\"]\n");

        var plan = fixture.Planner.Create(new InstallPlannerOptions(fixture.PackageRoot, fixture.InstallRoot, fixture.CodexHome));

        Assert.Equal(NotifyClassification.RecognizedVendor, plan.NotifyClassification);
        Assert.Equal([BridgeConstants.VendorExecutableName, BridgeConstants.VendorArgument], plan.RedactedNotifyArgv);
        Assert.DoesNotContain(fixture.VendorRoot, string.Join(" ", plan.RedactedNotifyArgv), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_marks_custom_handler_as_conflict_without_exposing_it()
    {
        var fixture = CreateFixture("notify = [\"C:\\\\secret\\\\custom.exe\", \"private-argument\"]\n");

        var plan = fixture.Planner.Create(new InstallPlannerOptions(fixture.PackageRoot, fixture.InstallRoot, fixture.CodexHome));

        Assert.Equal(NotifyClassification.Conflict, plan.NotifyClassification);
        Assert.All(plan.RedactedNotifyArgv, value => Assert.DoesNotContain("secret", value, StringComparison.OrdinalIgnoreCase));
        Assert.All(plan.RedactedNotifyArgv, value => Assert.DoesNotContain("private", value, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private Fixture CreateFixture(string config)
    {
        var package = Path.Combine(root, "package");
        var packageBin = Path.Combine(package, "bin");
        var codexHome = Path.Combine(root, "codex-home");
        var install = Path.Combine(root, "installed");
        var vendorRoot = Path.Combine(root, "vendor-root");
        Directory.CreateDirectory(packageBin);
        Directory.CreateDirectory(codexHome);
        Directory.CreateDirectory(vendorRoot);
        File.WriteAllText(Path.Combine(packageBin, "CodexTelegramBridge.exe"), "bridge");
        File.WriteAllText(Path.Combine(packageBin, "CodexTelegramCtl.exe"), "ctl");
        WriteManifest(package, ["bin/CodexTelegramBridge.exe", "bin/CodexTelegramCtl.exe"]);
        File.WriteAllText(Path.Combine(codexHome, "config.toml"), config);
        var planner = new InstallPlanner(
            new VendorExecutableValidator(vendorRoot),
            new ReversingProtector(),
            () => new DateTimeOffset(2026, 7, 11, 0, 0, 0, TimeSpan.Zero),
            () => "S-1-5-21-test",
            () => [],
            () => { });
        return new Fixture(package, codexHome, install, vendorRoot, planner);
    }

    private static void WriteManifest(string packageRoot, IReadOnlyList<string> paths)
    {
        var lines = paths.Select(path => $"{Hashing.Sha256File(Path.Combine(packageRoot, path.Replace('/', Path.DirectorySeparatorChar)))}  {path}");
        File.WriteAllText(Path.Combine(packageRoot, "manifest.sha256"), string.Join("\n", lines) + "\n", new UTF8Encoding(false));
    }

    private static IReadOnlyDictionary<string, string> Snapshot(string path) => Directory.Exists(path)
        ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(path, file), Hashing.Sha256File, StringComparer.OrdinalIgnoreCase)
        : new Dictionary<string, string>();

    private static string Quote(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    private sealed record Fixture(
        string PackageRoot,
        string CodexHome,
        string InstallRoot,
        string VendorRoot,
        InstallPlanner Planner);

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
