using CodexTelegramCommon;

namespace CodexTelegramUnitTests;

public sealed class ScheduledTaskDefinitionTests
{
    [Theory]
    [InlineData("worker", 5)]
    [InlineData("repair-check", 15)]
    public void Task_is_current_user_hidden_non_elevated_and_battery_safe(string mode, int interval)
    {
        var layout = new InstallationLayout(@"C:\Users\Sample User\AppData\Local\CodexTelegramBridge");
        var xml = TaskDefinitionBuilder.Build(layout, "S-1-5-21-123", DateTimeOffset.Now, mode, interval);

        Assert.Contains("<LogonType>InteractiveToken</LogonType>", xml, StringComparison.Ordinal);
        Assert.Contains("<RunLevel>LeastPrivilege</RunLevel>", xml, StringComparison.Ordinal);
        Assert.Contains("<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>", xml, StringComparison.Ordinal);
        Assert.Contains("<DisallowStartIfOnBatteries>false", xml, StringComparison.Ordinal);
        Assert.Contains("<StopIfGoingOnBatteries>false", xml, StringComparison.Ordinal);
        Assert.Contains("<StartWhenAvailable>true", xml, StringComparison.Ordinal);
        Assert.Contains("<WakeToRun>false", xml, StringComparison.Ordinal);
        Assert.Contains("<Enabled>false</Enabled>", xml, StringComparison.Ordinal);
        Assert.Contains($"<Interval>PT{interval}M</Interval>", xml, StringComparison.Ordinal);
        Assert.Contains($"<Arguments>{mode}</Arguments>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HighestAvailable", xml, StringComparison.OrdinalIgnoreCase);
    }
}
