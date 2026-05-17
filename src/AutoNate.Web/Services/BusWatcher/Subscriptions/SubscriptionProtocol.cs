using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoNate.Web.Services.BusWatcher.Subscriptions;

// Wire-frame DTOs for /ws/bus-watcher. JSON text frames; envelope shape
// `{ "type": "...", ... }` for both directions. Outbound frames are sealed
// records (one per type). Inbound frames share a single POCO because
// peeking the discriminator before deserializing the payload is wasteful for
// our 3 client frame types.
public static class SubscriptionFrameType
{
    public const string Subscribe = "subscribe";
    public const string Unsubscribe = "unsubscribe";
    public const string Ping = "ping";

    public const string Ack = "ack";
    public const string Event = "event";
    public const string Invalidate = "invalidate";
    public const string Pong = "pong";
    public const string Error = "error";
}

public static class SubscriptionRejectCode
{
    public const string Forbidden = "forbidden";
    public const string UnknownChannel = "unknown-channel";
    public const string Malformed = "malformed";
    public const string RateLimit = "rate-limit";
}

public static class SubscriptionInvalidateReason
{
    public const string PermissionGrantChanged = "permission-grant.changed";
    public const string MembershipChanged = "membership.changed";
}

public sealed class ClientFrame
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("channels")]
    public IReadOnlyList<string>? Channels { get; set; }

    [JsonPropertyName("ts")]
    public long? Ts { get; set; }
}

public sealed record RejectedChannel(
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("reason")] string? Reason);

public sealed record ServerAckFrame(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("subscribed")] IReadOnlyList<string>? Subscribed,
    [property: JsonPropertyName("unsubscribed")] IReadOnlyList<string>? Unsubscribed,
    [property: JsonPropertyName("rejected")] IReadOnlyList<RejectedChannel>? Rejected)
{
    public static ServerAckFrame ForSubscribe(string id, IReadOnlyList<string>? subscribed, IReadOnlyList<RejectedChannel>? rejected) =>
        new(SubscriptionFrameType.Ack, id, subscribed, null, rejected);

    public static ServerAckFrame ForUnsubscribe(string id, IReadOnlyList<string> unsubscribed) =>
        new(SubscriptionFrameType.Ack, id, null, unsubscribed, null);
}

public sealed record ServerEventFrame(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("receivedAtUtc")] DateTimeOffset ReceivedAtUtc,
    [property: JsonPropertyName("topic")] string Topic,
    [property: JsonPropertyName("contentType")] string? ContentType,
    [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string> Headers,
    [property: JsonPropertyName("payload")] string Payload)
{
    public static ServerEventFrame From(string channel, BusWatcherStreamService.BusWatcherMessage message) =>
        new(SubscriptionFrameType.Event, channel, message.ReceivedAtUtc, message.Topic, message.ContentType, message.Headers, message.Payload);
}

public sealed record ServerInvalidateFrame(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("channels")] IReadOnlyList<string> Channels,
    [property: JsonPropertyName("reason")] string? Reason)
{
    public static ServerInvalidateFrame For(IReadOnlyList<string> channels, string? reason) =>
        new(SubscriptionFrameType.Invalidate, channels, reason);
}

public sealed record ServerPongFrame(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("ts")] long Ts)
{
    public static ServerPongFrame For(string id, long ts) =>
        new(SubscriptionFrameType.Pong, id, ts);
}

public sealed record ServerErrorFrame(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("reason")] string? Reason)
{
    public static ServerErrorFrame For(string code, string? reason) =>
        new(SubscriptionFrameType.Error, code, reason);
}

public static class SubscriptionProtocol
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ClientFrame? TryParseClientFrame(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            return JsonSerializer.Deserialize<ClientFrame>(utf8Json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static byte[] Serialize<T>(T frame) =>
        JsonSerializer.SerializeToUtf8Bytes(frame, JsonOptions);
}
