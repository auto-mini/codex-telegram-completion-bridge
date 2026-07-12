using CodexTelegramCommon;

namespace CodexTelegramUnitTests;

public sealed class WindowsCommandLineTests
{
    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("", "\"\"")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("a\\", "a\\")]
    [InlineData("a b\\", "\"a b\\\\\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData("한글 경로", "\"한글 경로\"")]
    public void Quotes_argument_for_windows_crt(string value, string expected) =>
        Assert.Equal(expected, WindowsCommandLine.QuoteArgument(value));

    [Fact]
    public void Builds_argument_vector_without_shell() =>
        Assert.Equal("program \"two words\" \"\"", WindowsCommandLine.Build(["program", "two words", ""]));
}
