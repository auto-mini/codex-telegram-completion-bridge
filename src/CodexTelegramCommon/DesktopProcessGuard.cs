using System.Diagnostics;

namespace CodexTelegramCommon;

public static class DesktopProcessGuard
{
    private static readonly IReadOnlySet<string> ExactProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Codex",
        "ChatGPT",
        "codex-app-server",
    };

    public static IReadOnlyList<string> FindRunning()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var name = process.ProcessName;
                    if (ExactProcessNames.Contains(name))
                    {
                        string? executablePath = null;
                        if (name.Equals("codex", StringComparison.OrdinalIgnoreCase))
                        {
                            try { executablePath = process.MainModule?.FileName; }
                            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or NotSupportedException) { }
                        }

                        if (BlocksDesktopInstall(name, executablePath, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)))
                        {
                            names.Add(name);
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        return names.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static bool BlocksDesktopInstall(string name, string? executablePath, string roamingAppData)
    {
        if (!ExactProcessNames.Contains(name)) { return false; }
        if (!name.Equals("codex", StringComparison.OrdinalIgnoreCase) ||
            executablePath is null || !Path.IsPathFullyQualified(executablePath) ||
            !Path.IsPathFullyQualified(roamingAppData)) { return true; }

        // The separately installed npm CLI may run background tools after the
        // desktop app exits. It is not the desktop app or its bundled server.
        var npmCliRoot = Path.Combine(roamingAppData, "npm", "node_modules", "@openai", "codex") + Path.DirectorySeparatorChar;
        return !Path.GetFullPath(executablePath).StartsWith(npmCliRoot, StringComparison.OrdinalIgnoreCase);
    }
}
