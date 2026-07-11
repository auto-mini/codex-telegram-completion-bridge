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
                    if (ExactProcessNames.Contains(process.ProcessName))
                    {
                        names.Add(process.ProcessName);
                    }
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        return names.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
