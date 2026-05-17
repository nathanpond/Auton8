using System.Text.Json;
using AutoNate.Web.Services.Notifications;

namespace AutoNate.Web.Services.BusWatcher.Subscriptions.Resolvers;

// Maps a notification.events message to its per-user channel. The channel
// name encodes the userId, so the SubscriptionRegistry's name-keyed lookup
// already partitions delivery to the right recipient; no per-message fast
// gate is needed (defense-in-depth would be redundant with the subscribe-time
// gate that pins the userId).
public sealed class NotificationChannelResolver : IChannelResolver
{
    public string Topic => DaprNotificationEventPublisher.TopicName;

    public IReadOnlyList<ResolvedDelivery> Resolve(BusWatcherStreamService.BusWatcherMessage message)
    {
        if (!TryExtractUserId(message.Payload, out var userId))
        {
            return Array.Empty<ResolvedDelivery>();
        }

        var channel = $"{NotificationChannelNames.Kind}:user:{userId}";
        return new[] { new ResolvedDelivery(channel, GateTarget: null, FastGate: null) };
    }

    private static bool TryExtractUserId(string payload, out string userId)
    {
        userId = string.Empty;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!document.RootElement.TryGetProperty("userId", out var idElement))
            {
                return false;
            }

            var raw = idElement.GetString();
            if (!Guid.TryParse(raw, out var parsed))
            {
                return false;
            }

            userId = parsed.ToString();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public static class NotificationChannelNames
{
    public const string Kind = "notification";
}
