using System.Xml;
using System.Xml.Linq;

namespace CodexTelegramCommon;

public sealed record NotificationContent(bool ShouldNotify, string? Message);

public static class HeartbeatNotificationFilter
{
    public static NotificationContent Select(string? response)
    {
        var trimmed = response?.Trim();
        if (trimmed is null || !trimmed.StartsWith("<heartbeat>", StringComparison.Ordinal))
        {
            return new NotificationContent(true, response);
        }

        try
        {
            using var reader = XmlReader.Create(new StringReader(trimmed), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 2_097_152,
            });
            var root = XDocument.Load(reader).Root;
            if (root?.Name != "heartbeat")
            {
                return new NotificationContent(false, null);
            }

            var decisions = root.Elements("decision").ToArray();
            var messages = root.Elements("message").ToArray();
            var ids = root.Elements("automation_id").ToArray();
            if (ids.Length != 1 || string.IsNullOrWhiteSpace(ids[0].Value) ||
                decisions.Length != 1 || decisions[0].Value.Trim() != "NOTIFY" ||
                messages.Length != 1 || string.IsNullOrWhiteSpace(messages[0].Value))
            {
                // DONT_NOTIFY and unusable control envelopes must never become
                // completion-only noise or expose XML metadata to Telegram.
                return new NotificationContent(false, null);
            }

            return new NotificationContent(true, messages[0].Value);
        }
        catch (XmlException)
        {
            return new NotificationContent(false, null);
        }
    }
}
