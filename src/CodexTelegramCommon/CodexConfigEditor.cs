using System.Text;
using System.Text.Json;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace CodexTelegramCommon;

public sealed record CodexConfigDocument(
    string Content,
    IReadOnlyList<string>? NotifyArgv,
    ConfigTextSpan? NotifyValueSpan,
    int FirstTableOffset,
    string NewLine,
    bool HasUtf8Bom)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static CodexConfigDocument Parse(ReadOnlySpan<byte> bytes)
    {
        var hasBom = bytes.StartsWith(Encoding.UTF8.Preamble);
        if (hasBom)
        {
            bytes = bytes[Encoding.UTF8.Preamble.Length..];
        }

        string content;
        try
        {
            content = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Codex config is not valid UTF-8.", exception);
        }

        return Parse(content, hasBom);
    }

    public static CodexConfigDocument Parse(string content, bool hasUtf8Bom = false)
    {
        ArgumentNullException.ThrowIfNull(content);
        DocumentSyntax syntax;
        TomlTable model;
        try
        {
            syntax = SyntaxParser.ParseStrict(content, sourceName: "config.toml", validate: true);
            model = TomlSerializer.Deserialize<TomlTable>(content)
                    ?? throw new InvalidDataException("Codex config model is empty.");
        }
        catch (TomlException exception)
        {
            throw new InvalidDataException("Codex config TOML is invalid.", exception);
        }

        IReadOnlyList<string>? notify = null;
        if (model.TryGetValue("notify", out var rawNotify))
        {
            if (rawNotify is not TomlArray array || array.Count == 0 || array.Any(value => value is not string))
            {
                throw new InvalidDataException("Top-level notify must be a non-empty string array.");
            }

            notify = array.Cast<string>().ToArray();
        }

        var notifyNodes = syntax.KeyValues
            .Where(node => node.Key is { } key && !key.DotKeys.Any() && string.Equals(ReadSimpleKey(key.Key), "notify", StringComparison.Ordinal))
            .ToArray();
        if (notifyNodes.Length > 1 || (notify is null) != (notifyNodes.Length == 0))
        {
            throw new InvalidDataException("Top-level notify syntax is ambiguous.");
        }

        var valueSyntax = notifyNodes.Length == 1
            ? notifyNodes[0].Value ?? throw new InvalidDataException("Top-level notify has no value syntax.")
            : null;
        ConfigTextSpan? valueSpan = valueSyntax is null
            ? null
            : new ConfigTextSpan(valueSyntax.Span.Offset, valueSyntax.Span.Length);
        var firstTableOffset = !syntax.Tables.Any()
            ? content.Length
            : syntax.Tables.Min(table => table.Span.Offset);
        var newLine = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return new CodexConfigDocument(content, notify, valueSpan, firstTableOffset, newLine, hasUtf8Bom);
    }

    public byte[] RenderWithNotify(IReadOnlyList<string>? argv)
    {
        if (argv is not null && (argv.Count == 0 || argv.Any(value => value is null || value.IndexOf('\0') >= 0)))
        {
            throw new ArgumentException("Notify argv must contain non-null strings without NUL.", nameof(argv));
        }

        string edited;
        if (NotifyValueSpan is { } span)
        {
            edited = argv is null
                ? RemoveNotifyAssignment()
                : string.Concat(Content.AsSpan(0, span.Offset), RenderArray(argv), Content.AsSpan(span.End));
        }
        else if (argv is null)
        {
            edited = Content;
        }
        else
        {
            var insertion = $"notify = {RenderArray(argv)}{NewLine}";
            var needsLeadingNewLine = FirstTableOffset > 0 && Content[FirstTableOffset - 1] is not ('\r' or '\n');
            edited = Content.Insert(FirstTableOffset, (needsLeadingNewLine ? NewLine : string.Empty) + insertion);
        }

        var reparsed = Parse(edited, HasUtf8Bom);
        if (!SequenceEqual(reparsed.NotifyArgv, argv))
        {
            throw new InvalidDataException("Edited Codex config did not produce the requested notify value.");
        }

        var body = StrictUtf8.GetBytes(edited);
        if (!HasUtf8Bom)
        {
            return body;
        }

        var output = new byte[Encoding.UTF8.Preamble.Length + body.Length];
        Encoding.UTF8.Preamble.CopyTo(output);
        body.CopyTo(output, Encoding.UTF8.Preamble.Length);
        return output;
    }

    private string RemoveNotifyAssignment()
    {
        var span = NotifyValueSpan!.Value;
        var lineStart = Content.LastIndexOf('\n', Math.Max(0, span.Offset - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var lineEnd = Content.IndexOf('\n', span.End);
        lineEnd = lineEnd < 0 ? Content.Length : lineEnd + 1;
        var prefix = Content[lineStart..span.Offset];
        if (prefix.Contains('#', StringComparison.Ordinal))
        {
            throw new InvalidDataException("Notify assignment cannot be safely removed.");
        }

        return Content.Remove(lineStart, lineEnd - lineStart);
    }

    private static string RenderArray(IReadOnlyList<string> argv) =>
        string.Concat("[", string.Join(", ", argv.Select(value => JsonSerializer.Serialize(value))), "]");

    private static string? ReadSimpleKey(BareKeyOrStringValueSyntax? key) => key switch
    {
        BareKeySyntax bare => bare.Key?.Text,
        StringValueSyntax text => text.Value,
        _ => null,
    };

    private static bool SequenceEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right) =>
        left is null ? right is null : right is not null && left.SequenceEqual(right, StringComparer.Ordinal);
}

public readonly record struct ConfigTextSpan(int Offset, int Length)
{
    public int End => checked(Offset + Length);
}
