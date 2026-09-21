using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CodexTelegramIntegrationTests;

public sealed class StopHookAdapterIntegrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "StopHookAdapterTests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("가")]
    [InlineData("👨‍👩‍👧‍👦")]
    public async Task Large_stdin_reaches_bridge_as_one_bounded_unicode_safe_argument(string grapheme)
    {
        var answer = string.Concat(Enumerable.Repeat(grapheme, 20_000)) + "PRIVATE_TAIL";
        var payload = JsonSerializer.Serialize(new
        {
            hook_event_name = "Stop", session_id = "thread", turn_id = "turn",
            last_assistant_message = answer, prompt = "PRIVATE_PROMPT",
        });
        Assert.True(payload.Length > 32_767);

        var result = await RunAsync(payload);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("{}", result.Stdout.Trim());
        Assert.Empty(result.Stderr);
        using var observation = JsonDocument.Parse(File.ReadAllText(result.Observation));
        var arguments = observation.RootElement.GetProperty("Arguments");
        Assert.Equal(2, arguments.GetArrayLength());
        Assert.Equal("hook", arguments[0].GetString());
        var forwarded = arguments[1].GetString()!;
        Assert.True(forwarded.Length < 8192);
        Assert.DoesNotContain("PRIVATE_TAIL", forwarded, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE_PROMPT", forwarded, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(forwarded);
        Assert.Equal("thread", json.RootElement.GetProperty("thread-id").GetString());
        Assert.Equal("turn", json.RootElement.GetProperty("turn-id").GetString());
        Assert.Equal(string.Concat(Enumerable.Repeat(grapheme, 50)) + "…", json.RootElement.GetProperty("last-assistant-message").GetString());
    }

    [Fact]
    public async Task Quotes_backslashes_and_newlines_are_forwarded_without_shell_interpretation()
    {
        const string answer = "quote\" C:\\test\\ file\r\n한글 끝";
        var result = await RunAsync(JsonSerializer.Serialize(new
        {
            hook_event_name = "Stop", session_id = "thread", turn_id = "turn", last_assistant_message = answer,
        }));
        using var observation = JsonDocument.Parse(File.ReadAllText(result.Observation));
        using var payload = JsonDocument.Parse(observation.RootElement.GetProperty("Arguments")[1].GetString()!);
        Assert.Equal("quote\" C:\\test\\ file 한글 끝", payload.RootElement.GetProperty("last-assistant-message").GetString());
        Assert.Empty(result.Stderr);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"hook_event_name\":\"SubagentStop\",\"session_id\":\"t\",\"turn_id\":\"u\"}")]
    [InlineData("{\"hook_event_name\":\"Stop\",\"session_id\":7,\"turn_id\":\"u\"}")]
    [InlineData("{\"hook_event_name\":\"Stop\",\"session_id\":\"t\",\"session_id\":\"other\",\"turn_id\":\"u\"}")]
    [InlineData("not-json PRIVATE_PAYLOAD")]
    public async Task Non_stop_or_invalid_input_never_launches_bridge_or_blocks_turn(string payload)
    {
        var result = await RunAsync(payload);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("{}", result.Stdout.Trim());
        Assert.False(File.Exists(result.Observation));
        Assert.DoesNotContain("PRIVATE_PAYLOAD", result.Stderr, StringComparison.Ordinal);
    }

    private async Task<(int ExitCode, string Stdout, string Stderr, string Observation)> RunAsync(string input)
    {
        Directory.CreateDirectory(root);
        var output = Path.Combine(root, Guid.NewGuid().ToString("N") + ".json");
        var configuration = AppContext.BaseDirectory.Contains("Release", StringComparison.Ordinal) ? "Release" : "Debug";
        var vendor = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TestVendor", "bin", configuration, "net8.0-windows", "codex-computer-use.exe"));
        var script = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts", "Invoke-CodexStopHook.ps1"));
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("PWSH_PATH") ?? "pwsh")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", script, "-BridgePath", vendor })
        {
            start.ArgumentList.Add(argument);
        }
        start.Environment["CODEX_TELEGRAM_TEST_VENDOR_OUTPUT"] = output;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(input);
        process.StandardInput.Close();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        return (process.ExitCode, await stdout, await stderr, output);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
    }
}
