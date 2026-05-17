using AutoNate.Web.Services.BusWatcher.Subscriptions.Resolvers;

namespace AutoNate.Web.Services.BusWatcher.Subscriptions.Gates;

// `notification:user:{userId}` — only the actor's own userId is allowed. The
// channel name partition means a permitted subscription only sees its own
// notifications; the resolver derives the channel name from payload.userId,
// so cross-user leakage requires both bypassing this gate AND faking the
// SubscriptionRegistry index.
public sealed class NotificationChannelSubscribeGate : IChannelSubscribeGate
{
    public string Kind => NotificationChannelNames.Kind;

    public Task<SubscribeGateResult> AuthorizeAsync(
        ChannelName channel,
        SubscriptionConnection connection,
        CancellationToken cancellationToken)
    {
        if (channel.Parts.Count != 2 || !string.Equals(channel.Parts[0], "user", StringComparison.Ordinal))
        {
            return Task.FromResult(SubscribeGateResult.Forbid(
                SubscriptionRejectCode.UnknownChannel,
                "expected notification:user:{userId}"));
        }

        if (!Guid.TryParse(channel.Parts[1], out var requestedUserId))
        {
            return Task.FromResult(SubscribeGateResult.Forbid(
                SubscriptionRejectCode.UnknownChannel,
                "userId is not a Guid"));
        }

        if (requestedUserId != connection.Snapshot.UserId)
        {
            return Task.FromResult(SubscribeGateResult.Forbid(
                SubscriptionRejectCode.Forbidden,
                "userId must match the connecting actor"));
        }

        return Task.FromResult(SubscribeGateResult.Allow());
    }
}
