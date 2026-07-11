using System.Runtime.InteropServices;
using System.Security;

namespace CodexTelegramCommon;

public sealed record ScheduledTaskStatus(string Name, bool Exists, bool Enabled, int? LastTaskResult, int? State);

public interface IScheduledTaskManager
{
    void StageDisabled(InstallationLayout layout, string userSid, DateTimeOffset nowUtc);

    void EnableAll();

    void DisableAll();

    void RemoveAll();

    IReadOnlyList<ScheduledTaskStatus> GetStatuses();
}

public sealed class WindowsScheduledTaskManager : IScheduledTaskManager
{
    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonInteractiveToken = 3;
    private static readonly string[] TaskNames = ["Drain", "Repair"];
    private readonly string folderName;

    public WindowsScheduledTaskManager(string folderName = "CodexTelegramBridge")
    {
        if (string.IsNullOrWhiteSpace(folderName) || folderName.Length > 64 ||
            folderName.Any(character => !(character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-')))
        {
            throw new ArgumentException("Task Scheduler folder name is invalid.", nameof(folderName));
        }

        this.folderName = folderName;
    }

    public void StageDisabled(InstallationLayout layout, string userSid, DateTimeOffset nowUtc)
    {
        CurrentUserContext.EnsureSupportedHost();
        WithFolder(create: true, folder =>
        {
            folder.RegisterTask(
                "Drain",
                TaskDefinitionBuilder.Build(layout, userSid, nowUtc, "worker", 5),
                TaskCreateOrUpdate,
                userSid,
                null,
                TaskLogonInteractiveToken,
                null);
            folder.RegisterTask(
                "Repair",
                TaskDefinitionBuilder.Build(layout, userSid, nowUtc, "repair-check", 15),
                TaskCreateOrUpdate,
                userSid,
                null,
                TaskLogonInteractiveToken,
                null);
        });
    }

    public void EnableAll() => SetEnabled(true);

    public void DisableAll() => SetEnabled(false);

    public void RemoveAll()
    {
        object? service = null;
        object? root = null;
        object? folder = null;
        try
        {
            (service, root, folder) = OpenFolder(create: false);
            if (folder is null)
            {
                return;
            }

            dynamic dynamicFolder = folder;
            foreach (var name in TaskNames)
            {
                try
                {
                    dynamicFolder.DeleteTask(name, 0);
                }
                catch (Exception exception) when (IsNotFound(exception))
                {
                }
            }

            dynamic dynamicRoot = root!;
            try
            {
                dynamicRoot.DeleteFolder(folderName, 0);
            }
            catch (Exception exception) when (IsNotFound(exception))
            {
            }
        }
        finally
        {
            Release(folder);
            Release(root);
            Release(service);
        }
    }

    public IReadOnlyList<ScheduledTaskStatus> GetStatuses()
    {
        var statuses = new List<ScheduledTaskStatus>();
        WithFolder(create: false, folder =>
        {
            foreach (var name in TaskNames)
            {
                object? task = null;
                try
                {
                    task = folder.GetTask(name);
                    dynamic dynamicTask = task;
                    statuses.Add(new ScheduledTaskStatus(
                        name,
                        true,
                        (bool)dynamicTask.Enabled,
                        (int)dynamicTask.LastTaskResult,
                        (int)dynamicTask.State));
                }
                catch (Exception exception) when (IsNotFound(exception))
                {
                    statuses.Add(new ScheduledTaskStatus(name, false, false, null, null));
                }
                finally
                {
                    Release(task);
                }
            }
        });
        if (statuses.Count == 0)
        {
            statuses.AddRange(TaskNames.Select(name => new ScheduledTaskStatus(name, false, false, null, null)));
        }

        return statuses;
    }

    private void SetEnabled(bool enabled) => WithFolder(create: false, folder =>
    {
        foreach (var name in TaskNames)
        {
            object? task = null;
            try
            {
                task = folder.GetTask(name);
                ((dynamic)task).Enabled = enabled;
            }
            catch (Exception exception) when (IsNotFound(exception))
            {
                if (enabled)
                {
                    throw new InvalidOperationException($"Scheduled task {name} is missing.", exception);
                }
            }
            finally
            {
                Release(task);
            }
        }
    });

    private void WithFolder(bool create, Action<dynamic> action)
    {
        object? service = null;
        object? root = null;
        object? folder = null;
        try
        {
            (service, root, folder) = OpenFolder(create);
            if (folder is not null)
            {
                action(folder);
            }
        }
        finally
        {
            Release(folder);
            Release(root);
            Release(service);
        }
    }

    private (object Service, object Root, object? Folder) OpenFolder(bool create)
    {
        var type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)
                   ?? throw new PlatformNotSupportedException("Task Scheduler COM service is unavailable.");
        var service = Activator.CreateInstance(type)
                      ?? throw new InvalidOperationException("Task Scheduler COM service could not be created.");
        dynamic dynamicService = service;
        dynamicService.Connect();
        object root = dynamicService.GetFolder("\\");
        dynamic dynamicRoot = root;
        try
        {
            return (service, root, dynamicRoot.GetFolder(folderName));
        }
        catch (Exception exception) when (IsNotFound(exception))
        {
            return create
                ? (service, root, dynamicRoot.CreateFolder(folderName, Type.Missing))
                : (service, root, null);
        }
    }

    private static bool IsNotFound(Exception exception) =>
        exception is FileNotFoundException or DirectoryNotFoundException ||
        exception.HResult is unchecked((int)0x80070002) or unchecked((int)0x80070003);

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}

internal static class TaskDefinitionBuilder
{
    public static string Build(InstallationLayout layout, string userSid, DateTimeOffset nowUtc, string mode, int intervalMinutes)
    {
        if (mode is not ("worker" or "repair-check") || intervalMinutes is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var command = SecurityElement.Escape(Path.Combine(layout.Bin, "CodexTelegramBridge.exe"));
        var working = SecurityElement.Escape(layout.Bin);
        var sid = SecurityElement.Escape(userSid);
        var start = nowUtc.ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:ss");
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Codex Telegram completion bridge {mode}</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{sid}</UserId></LogonTrigger>
                <CalendarTrigger>
                  <Repetition><Interval>PT{intervalMinutes}M</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition>
                  <StartBoundary>{start}</StartBoundary><Enabled>true</Enabled>
                  <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>
                </CalendarTrigger>
              </Triggers>
              <Principals><Principal id="Author"><UserId>{sid}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>false</Enabled><Hidden>true</Hidden><RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT1H</ExecutionTimeLimit><Priority>7</Priority>
              </Settings>
              <Actions Context="Author"><Exec><Command>{command}</Command><Arguments>{mode}</Arguments><WorkingDirectory>{working}</WorkingDirectory></Exec></Actions>
            </Task>
            """;
    }
}
