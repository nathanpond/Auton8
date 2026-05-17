using AutoNate.Web.Services.BusWatcher.Subscriptions.Resolvers;

namespace AutoNate.Web.Services.BusWatcher.Subscriptions.Gates;

// `firehose:all` — superadmin only. Any other shape under the firehose kind
// is rejected as unknown.
public sealed class FirehoseChannelSubscribeGate : IChannelSubscribeGate
{
    public string Kind => FirehoseChannelNames.FirehoseKind;

    public Task<SubscribeGateResult> AuthorizeAsync(
        ChannelName channel,
        SubscriptionConnection connection,
        CancellationToken cancellationToken)
    {
        if (channel.Parts.Count != 1 || !string.Equals(channel.Parts[0], "all", StringComparison.Ordinal))
        {
            return Task.FromResult(SubscribeGateResult.Forbid(
                SubscriptionRejectCode.UnknownChannel,
                "expected firehose:all"));
        }

        return Task.FromResult(connection.Snapshot.IsSuperAdmin
            ? SubscribeGateResult.Allow()
            : SubscribeGateResult.Forbid(SubscriptionRejectCode.Forbidden, "superadmin only"));
    }
}

// `topic:{topicName}` — per-topic firehose. Superadmin only. Useful for
// future admin debug tooling; not currently consumed by the SPA.
public sealed class TopicChannelSubscribeGate : IChannelSubscribeGate
{
    public string Kind => FirehoseChannelNames.TopicKind;

    public Task<SubscribeGateResult> AuthorizeAsync(
        ChannelName channel,
        SubscriptionConnection connection,
        CancellationToken cancellationToken)
    {
        if (channel.Parts.Count != 1 || string.IsNullOrWhiteSpace(channel.Parts[0]))
        {
            return Task.FromResult(SubscribeGateResult.Forbid(
                SubscriptionRejectCode.UnknownChannel,
                "expected topic:{topicName}"));
        }

        return Task.FromResult(connection.Snapshot.IsSuperAdmin
            ? SubscribeGateResult.Allow()
            : SubscribeGateResult.Forbid(SubscriptionRejectCode.Forbidden, "superadmin only"));
    }
}
