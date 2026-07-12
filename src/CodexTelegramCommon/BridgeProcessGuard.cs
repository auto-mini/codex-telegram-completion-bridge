using System.Diagnostics;

namespace CodexTelegramCommon;

public static class BridgeProcessGuard
{
    public static bool RequestStopAndWait(InstallationLayout layout, string machineId, TimeSpan timeout)
    {
        AtomicFile.WriteUtf8(layout.WorkerStopMarkerPath, "STOP\n");
        try
        {
            WorkerCoordination.SignalExistingOrCreate(machineId);
            return WaitForWorkersToExit(layout, timeout);
        }
        finally
        {
            if (File.Exists(layout.WorkerStopMarkerPath))
            {
                File.Delete(layout.WorkerStopMarkerPath);
            }
        }
    }

    public static bool WaitForWorkersToExit(InstallationLayout layout, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var workers = FindWorkers(layout);
            if (workers.Count == 0)
            {
                return true;
            }

            foreach (var process in workers)
            {
                process.Dispose();
            }

            Thread.Sleep(100);
        }

        var remaining = FindWorkers(layout);
        foreach (var process in remaining)
        {
            process.Dispose();
        }

        return remaining.Count == 0;
    }

    public static void ClearStopRequest(InstallationLayout layout)
    {
        if (File.Exists(layout.WorkerStopMarkerPath))
        {
            File.Delete(layout.WorkerStopMarkerPath);
        }
    }

    private static IReadOnlyList<Process> FindWorkers(InstallationLayout layout)
    {
        var expected = Path.GetFullPath(Path.Combine(layout.Bin, "CodexTelegramBridge.exe"));
        var result = new List<Process>();
        foreach (var process in Process.GetProcessesByName("CodexTelegramBridge"))
        {
            if (process.Id == Environment.ProcessId)
            {
                process.Dispose();
                continue;
            }

            try
            {
                var path = process.MainModule?.FileName;
                if (path is not null && string.Equals(Path.GetFullPath(path), expected, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(process);
                }
                else
                {
                    process.Dispose();
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                process.Dispose();
            }
        }

        return result;
    }
}
