using System.Runtime.InteropServices;
using System.Text.Json;

namespace CodexTelegramTestVendor;

internal static class Program
{
    private const int StdInputHandle = -10;
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;

    private static int Main(string[] args)
    {
        var output = Environment.GetEnvironmentVariable("CODEX_TELEGRAM_TEST_VENDOR_OUTPUT");
        if (string.IsNullOrWhiteSpace(output) || !Path.IsPathFullyQualified(output))
        {
            return 2;
        }

        var observation = new VendorObservation(
            args,
            Environment.CurrentDirectory,
            Environment.GetEnvironmentVariable("CODEX_TELEGRAM_TEST_VENDOR_MARKER"),
            GetFileType(GetStdHandle(StdInputHandle)),
            GetFileType(GetStdHandle(StdOutputHandle)),
            GetFileType(GetStdHandle(StdErrorHandle)),
            GetConsoleWindow() != IntPtr.Zero);
        var temporaryOutput = $"{output}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryOutput, JsonSerializer.Serialize(observation));
        File.Move(temporaryOutput, output);
        return 0;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetFileType(IntPtr handle);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetConsoleWindow();

    private sealed record VendorObservation(
        IReadOnlyList<string> Arguments,
        string WorkingDirectory,
        string? Marker,
        uint StandardInputType,
        uint StandardOutputType,
        uint StandardErrorType,
        bool HasConsoleWindow);
}
