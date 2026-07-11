using System.Text.Json;
using CodexTelegramCommon;

namespace CodexTelegramIntegrationTests;

[Collection("Process environment")]
public sealed class UpstreamLauncherIntegrationTests : IDisposable
{
    private const uint FileTypeChar = 0x0002;
    private readonly string root = Path.Combine(Path.GetTempPath(), "UpstreamLauncherTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Launch_preserves_contract_and_has_no_console_or_inherited_streams()
    {
        Directory.CreateDirectory(root);
        var executable = LocateTestVendor();
        var output = Path.Combine(root, "observation.json");
        var previousOutput = Environment.GetEnvironmentVariable("CODEX_TELEGRAM_TEST_VENDOR_OUTPUT");
        var previousMarker = Environment.GetEnvironmentVariable("CODEX_TELEGRAM_TEST_VENDOR_MARKER");
        var originalDirectory = Environment.CurrentDirectory;
        var workingDirectory = Path.Combine(root, "working directory 한글");
        Directory.CreateDirectory(workingDirectory);
        try
        {
            Environment.SetEnvironmentVariable("CODEX_TELEGRAM_TEST_VENDOR_OUTPUT", output);
            Environment.SetEnvironmentVariable("CODEX_TELEGRAM_TEST_VENDOR_MARKER", "inherited-value");
            Environment.CurrentDirectory = workingDirectory;
            var validator = new VendorExecutableValidator(Path.GetDirectoryName(executable)!);
            var captured = validator.ValidateArgv(
                [executable, BridgeConstants.VendorArgument],
                new string('a', 64),
                DateTimeOffset.UtcNow);
            Assert.True(captured.IsValid);

            const string payload = "{\"type\":\"agent-turn-complete\",\"value\":\"spaces \\\"quotes\\\" 한글\"}";
            var result = new UpstreamLauncher(validator).Launch(captured.Record!, payload);

            Assert.True(result.Success);
            var observation = await WaitForObservationAsync(output);
            Assert.Equal([BridgeConstants.VendorArgument, payload], observation.Arguments);
            Assert.Equal(workingDirectory, observation.WorkingDirectory);
            Assert.Equal("inherited-value", observation.Marker);
            Assert.Equal(FileTypeChar, observation.StandardInputType);
            Assert.Equal(FileTypeChar, observation.StandardOutputType);
            Assert.Equal(FileTypeChar, observation.StandardErrorType);
            Assert.False(observation.HasConsoleWindow);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            Environment.SetEnvironmentVariable("CODEX_TELEGRAM_TEST_VENDOR_OUTPUT", previousOutput);
            Environment.SetEnvironmentVariable("CODEX_TELEGRAM_TEST_VENDOR_MARKER", previousMarker);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string LocateTestVendor()
    {
        var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "TestVendor", "bin", configuration, "net8.0-windows", "codex-computer-use.exe"));
        Assert.True(File.Exists(path), $"Test vendor was not built: {path}");
        return path;
    }

    private static async Task<VendorObservation> WaitForObservationAsync(string path)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (!File.Exists(path) && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        Assert.True(File.Exists(path), "Test vendor did not produce its observation.");
        return JsonSerializer.Deserialize<VendorObservation>(await File.ReadAllTextAsync(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidDataException("Vendor observation is empty.");
    }

    private sealed record VendorObservation(
        IReadOnlyList<string> Arguments,
        string WorkingDirectory,
        string? Marker,
        uint StandardInputType,
        uint StandardOutputType,
        uint StandardErrorType,
        bool HasConsoleWindow);
}

[CollectionDefinition("Process environment", DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection;
