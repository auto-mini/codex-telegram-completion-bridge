using CodexTelegramCommon;

namespace CodexTelegramIntegrationTests;

public sealed class TaskSchedulerManualIntegrationTests
{
    [Fact]
    public void Registers_enables_queries_and_removes_real_current_user_tasks_when_opted_in()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("CODEX_TELEGRAM_RUN_TASK_SCHEDULER_TEST"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var folder = $"CodexTelegramBridge-Test-{Guid.NewGuid():N}";
        var manager = new WindowsScheduledTaskManager(folder);
        var layout = new InstallationLayout(Path.Combine(Path.GetTempPath(), "Task Scheduler XML & 한글"));
        try
        {
            manager.StageDisabled(layout, CurrentUserContext.Sid, DateTimeOffset.Now);
            Assert.All(manager.GetStatuses(), status =>
            {
                Assert.True(status.Exists);
                Assert.False(status.Enabled);
            });

            manager.EnableAll();
            Assert.All(manager.GetStatuses(), status => Assert.True(status.Enabled));
            manager.DisableAll();
        }
        finally
        {
            manager.RemoveAll();
        }

        Assert.All(manager.GetStatuses(), status => Assert.False(status.Exists));
    }
}
