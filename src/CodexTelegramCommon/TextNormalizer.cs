using System.Globalization;
using System.Text;

namespace CodexTelegramCommon;

public static class TextNormalizer
{
    public static string? NormalizeTitle(string? value) => Normalize(value, BridgeConstants.MaxTitleGraphemes);

    public static string? NormalizePcName(string? value) => Normalize(value, BridgeConstants.MaxPcNameGraphemes);

    public static string RenderCompletion(string pcName, string threadTitle) =>
        string.Concat(BridgeConstants.CompletionLine, "\nPC: ", pcName, "\n스레드: ", threadTitle);

    private static string? Normalize(string? value, int graphemeLimit)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        string normalized;
        try
        {
            normalized = value.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var builder = new StringBuilder(normalized.Length);
        var previousSpace = true;
        foreach (var rune in normalized.EnumerateRunes())
        {
            var replace = Rune.IsWhiteSpace(rune) || Rune.IsControl(rune) || IsBidiControl(rune.Value);
            if (replace)
            {
                if (!previousSpace)
                {
                    builder.Append(' ');
                    previousSpace = true;
                }

                continue;
            }

            builder.Append(rune);
            previousSpace = false;
        }

        var collapsed = builder.ToString().Trim();
        if (collapsed.Length == 0)
        {
            return null;
        }

        return TruncateGraphemes(collapsed, graphemeLimit);
    }

    private static string TruncateGraphemes(string value, int limit)
    {
        var indexes = StringInfo.ParseCombiningCharacters(value);
        if (indexes.Length <= limit)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, indexes[limit]), "…");
    }

    private static bool IsBidiControl(int value) =>
        value == 0x061C ||
        value is >= 0x200E and <= 0x200F ||
        value is >= 0x202A and <= 0x202E ||
        value is >= 0x2066 and <= 0x2069;
}
