using System.Text;

namespace CodexTelegramCommon;

internal static class WindowsCommandLine
{
    public static string Build(IReadOnlyList<string> argv)
    {
        if (argv.Count == 0)
        {
            throw new ArgumentException("At least one argument is required.", nameof(argv));
        }

        return string.Join(' ', argv.Select(QuoteArgument));
    }

    public static string QuoteArgument(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > 0 && value.All(character => character is not (' ' or '\t' or '\n' or '\v' or '"')))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }

            builder.Append('\\', backslashes);
            backslashes = 0;
            builder.Append(character);
        }

        builder.Append('\\', backslashes * 2);
        builder.Append('"');
        return builder.ToString();
    }
}
