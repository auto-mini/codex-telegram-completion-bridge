using CodexTelegramCommon;

namespace CodexTelegramUnitTests;

public sealed class TextNormalizerTests
{
    [Fact]
    public void Produces_exactly_three_lines_and_removes_injected_separators()
    {
        var title = TextNormalizer.NormalizeTitle("  제목\r\n둘\u2028셋\u202eABC  ");
        var pc = TextNormalizer.NormalizePcName("PC\tONE");
        var message = TextNormalizer.RenderCompletion(pc!, title!);

        Assert.Equal("제목 둘 셋 ABC", title);
        Assert.Equal("PC ONE", pc);
        Assert.Equal(3, message.Split('\n').Length);
        Assert.Equal("✅ Codex 응답 완료\nPC: PC ONE\n스레드: 제목 둘 셋 ABC", message);
    }

    [Fact]
    public void Preserves_emoji_zwj_sequence()
    {
        const string family = "👩‍💻";

        Assert.Equal(family, TextNormalizer.NormalizeTitle(family));
    }

    [Fact]
    public void Telegram_rendering_truncates_only_the_display_title_by_grapheme()
    {
        const string family = "👨‍👩‍👧‍👦";
        var fullTitle = string.Concat(Enumerable.Repeat(family, BridgeConstants.MaxTelegramTitleGraphemes + 3));
        var normalized = TextNormalizer.NormalizeTitle(fullTitle)!;

        var message = TextNormalizer.RenderCompletion("Test PC", normalized);
        var displayedTitle = message.Split('\n')[2]["스레드: ".Length..];

        Assert.Equal(BridgeConstants.MaxTelegramTitleGraphemes + 1, System.Globalization.StringInfo.ParseCombiningCharacters(displayedTitle).Length);
        Assert.EndsWith("…", displayedTitle, StringComparison.Ordinal);
        Assert.Equal(fullTitle, normalized);
    }

    [Fact]
    public void Telegram_rendering_leaves_short_title_unchanged()
    {
        const string title = "짧은 작업 제목";

        Assert.EndsWith("스레드: " + title, TextNormalizer.RenderCompletion("Test PC", title), StringComparison.Ordinal);
    }

    [Fact]
    public void Truncates_by_grapheme_and_adds_one_ellipsis()
    {
        var value = string.Concat(Enumerable.Repeat("가", BridgeConstants.MaxTitleGraphemes + 10));

        var normalized = TextNormalizer.NormalizeTitle(value)!;

        Assert.EndsWith("…", normalized, StringComparison.Ordinal);
        Assert.Equal(BridgeConstants.MaxTitleGraphemes + 1, System.Globalization.StringInfo.ParseCombiningCharacters(normalized).Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\r\n\t")]
    public void Empty_normalized_value_is_rejected(string? value) => Assert.Null(TextNormalizer.NormalizeTitle(value));

    [Theory]
    [InlineData("Mobile GPT notification title…", "Mobile GPT notification title")]
    [InlineData("Mobile GPT notification title...", "Mobile GPT notification title")]
    [InlineData("  Mobile\tGPT notification title  ", "Mobile GPT notification title")]
    public void Normalizes_visible_verification_prefix_and_removes_ui_ellipsis(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.NormalizeTitleVerificationPrefix(input));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("……")]
    [InlineData("...")]
    public void Rejects_verification_prefix_below_minimum_length(string input)
    {
        Assert.Null(TextNormalizer.NormalizeTitleVerificationPrefix(input));
    }
}
