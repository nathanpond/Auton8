namespace AutoNate.Web.Services.BusWatcher.Subscriptions.Resolvers;

// Every bus message also fans out to two admin-only channels:
//   firehose:all     — the BusWatcher admin page.
//   topic:{topic}    — per-topic admin debug (future).
// Subscribe-time gates ensure only SuperAdmins are ever subscribed; no
// per-message gating is needed here.
public sealed class FirehoseFallbackResolver : IChannelResolver
{
    public string Topic => ChannelResolverTopics.AnyTopic;

    public IReadOnlyList<ResolvedDelivery> Resolve(BusWatcherStreamService.BusWatcherMessage message) =>
        new[]
        {
            new ResolvedDelivery(FirehoseChannelNames.All, GateTarget: null, FastGate: null),
            new ResolvedDelivery($"{FirehoseChannelNames.TopicKind}:{message.Topic}", GateTarget: null, FastGate: null),
        };
}

public static class FirehoseChannelNames
{
    public const string FirehoseKind = "firehose";
    public const string TopicKind = "topic";

    public const string All = "firehose:all";
}
