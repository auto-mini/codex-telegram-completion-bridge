using CodexTelegramCommon;

namespace CodexTelegramUnitTests;

public sealed class TextNormalizerTests
{
    [Theory]
    [InlineData("짧은 답변", "짧은 답변")]
    [InlineData("12345678901234567890123456789012345678901234567890", "12345678901234567890123456789012345678901234567890")]
    [InlineData("123456789012345678901234567890123456789012345678901", "12345678901234567890123456789012345678901234567890…")]
    [InlineData("  답변\r\n다음\t줄\u202e완료  ", "답변 다음 줄 완료")]
    [InlineData("가나다", "가나다")]
    public void Renders_bounded_answer_on_fourth_line(string answer, string expected)
    {
        var preview = TextNormalizer.NormalizeAnswerPreview(answer);
        Assert.Equal(expected, preview);
        Assert.Equal(preview, TextNormalizer.NormalizeAnswerPreview(preview));
        Assert.Equal("✅ Codex 응답 완료\nPC: PC\n스레드: 제목\n답변: " + expected,
            TextNormalizer.RenderCompletion("PC", "제목", answer));
    }

    [Theory]
    [InlineData("가")]
    [InlineData("👨‍👩‍👧‍👦")]
    [InlineData("🇰🇷")]
    [InlineData("👍🏽")]
    [InlineData("e\u0301")]
    public void Answer_preview_preserves_fifty_complete_graphemes(string grapheme)
    {
        var answer = string.Concat(Enumerable.Repeat(grapheme, 55));
        var preview = TextNormalizer.NormalizeAnswerPreview(answer)!;
        Assert.Equal(string.Concat(Enumerable.Repeat(grapheme, 50)).Normalize() + "…", preview);
        Assert.Equal(51, System.Globalization.StringInfo.ParseCombiningCharacters(preview).Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n\t\u202e")]
    public void Missing_answer_preserves_legacy_three_line_completion(string? answer)
    {
        Assert.Equal(TextNormalizer.RenderCompletion("PC", "제목"), TextNormalizer.RenderCompletion("PC", "제목", answer));
    }

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

        Assert.Equal(string.Concat(Enumerable.Repeat(family, 12)) + "…", displayedTitle);
        Assert.Equal(13, System.Globalization.StringInfo.ParseCombiningCharacters(displayedTitle).Length);
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
    public void Matches_visible_verification_prefix_and_removes_ui_ellipsis(string input, string expected)
    {
        Assert.True(TextNormalizer.MatchesTitleVerification(expected + " suffix long enough", input));
    }

    [Theory]
    [InlineData("test", "test", true)]
    [InlineData("test", "test…", true)]
    [InlineData("test", "tes", false)]
    [InlineData("abcdefghijkl", "abcdefghijkl", true)]
    [InlineData("abcdefghijkl", "abcdefghijkl…", true)]
    [InlineData("abcdefghijkl", "abcdefghijk", false)]
    [InlineData("short title that is long", "short", false)]
    [InlineData("...", "...", true)]
    [InlineData("test", "...", false)]
    public void Short_title_requires_full_match_and_long_title_requires_minimum_prefix(string title, string input, bool expected)
    {
        Assert.Equal(expected, TextNormalizer.MatchesTitleVerification(title, input));
    }
}
