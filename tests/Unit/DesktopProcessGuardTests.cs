using CodexTelegramCommon;

namespace CodexTelegramUnitTests;

public sealed class DesktopProcessGuardTests
{
    private const string Roaming = @"C:\Users\example\AppData\Roaming";

    [Theory]
    [InlineData("codex", @"C:\Users\example\AppData\Roaming\npm\node_modules\@openai\codex\vendor\x64\bin\codex.exe", false)]
    [InlineData("Codex", @"C:\Users\example\AppData\Local\OpenAI\Codex\bin\version\codex.exe", true)]
    [InlineData("Codex", @"C:\Program Files\WindowsApps\OpenAI.Codex\Codex.exe", true)]
    [InlineData("codex", @"C:\Users\example\AppData\Roaming\npm\node_modules\@openai\codex-other\codex.exe", true)]
    [InlineData("codex", null, true)]
    [InlineData("codex", "relative.exe", true)]
    [InlineData("ChatGPT", null, true)]
    [InlineData("codex-app-server", null, true)]
    [InlineData("CodexTelegramBridge", null, false)]
    public void Distinguishes_desktop_from_the_separate_npm_cli(string name, string? path, bool expected)
    {
        Assert.Equal(expected, DesktopProcessGuard.BlocksDesktopInstall(name, path, Roaming));
    }
}
