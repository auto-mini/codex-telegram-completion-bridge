using System.Buffers;
using System.Text;
using System.Text.Json;

namespace CodexTelegramCommon;

public enum NotifyParseKind
{
    Completion,
    Ignored,
    Invalid,
}

public sealed record NotifyParseResult(
    NotifyParseKind Kind,
    string? ThreadId = null,
    string? TurnId = null,
    string? ErrorCode = null);

public static class NotifyPayloadParser
{
    public static NotifyParseResult Parse(string? json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > BridgeConstants.MaxNotifyPayloadUtf16Length)
        {
            return new NotifyParseResult(NotifyParseKind.Invalid, ErrorCode: "PAYLOAD_SIZE_INVALID");
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetUniqueProperty(root, "type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                return new NotifyParseResult(NotifyParseKind.Invalid, ErrorCode: "PAYLOAD_ENVELOPE_INVALID");
            }

            if (!string.Equals(typeElement.GetString(), BridgeConstants.NotifyEventType, StringComparison.Ordinal))
            {
                return new NotifyParseResult(NotifyParseKind.Ignored);
            }

            if (!TryGetOpaqueId(root, "thread-id", out var threadId) ||
                !TryGetOpaqueId(root, "turn-id", out var turnId))
            {
                return new NotifyParseResult(NotifyParseKind.Invalid, ErrorCode: "PAYLOAD_ID_INVALID");
            }

            return new NotifyParseResult(NotifyParseKind.Completion, threadId, turnId);
        }
        catch (JsonException)
        {
            return new NotifyParseResult(NotifyParseKind.Invalid, ErrorCode: "PAYLOAD_JSON_INVALID");
        }
    }

    internal static bool IsValidOpaqueId(string? value)
    {
        if (value is null || value.Length is < 1 or > BridgeConstants.MaxOpaqueIdUtf16Length)
        {
            return false;
        }

        var span = value.AsSpan();
        while (!span.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(span, out var rune, out var consumed);
            if (status != OperationStatus.Done || Rune.IsControl(rune) || rune.Value == 0)
            {
                return false;
            }

            span = span[consumed..];
        }

        return true;
    }

    private static bool TryGetOpaqueId(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        if (!TryGetUniqueProperty(root, propertyName, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return IsValidOpaqueId(value);
    }

    private static bool TryGetUniqueProperty(JsonElement root, string name, out JsonElement value)
    {
        value = default;
        var found = false;
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            if (found)
            {
                return false;
            }

            value = property.Value;
            found = true;
        }

        return found;
    }
}
