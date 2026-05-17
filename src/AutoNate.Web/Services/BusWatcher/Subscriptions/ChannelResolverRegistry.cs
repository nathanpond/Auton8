namespace AutoNate.Web.Services.BusWatcher.Subscriptions;

// Routes inbound bus messages to the resolvers that should run for them.
// A message's topic-specific resolvers run first (in registration order),
// followed by every "any topic" resolver — the latter exists so the firehose
// fallback always sees every message regardless of new topics being added.
public sealed class ChannelResolverRegistry
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<IChannelResolver>> _byTopic;
    private readonly IReadOnlyList<IChannelResolver> _anyTopic;

    public ChannelResolverRegistry(IEnumerable<IChannelResolver> resolvers)
    {
        var topicLookup = new Dictionary<string, List<IChannelResolver>>(StringComparer.Ordinal);
        var anyTopic = new List<IChannelResolver>();

        foreach (var resolver in resolvers)
        {
            if (string.Equals(resolver.Topic, ChannelResolverTopics.AnyTopic, StringComparison.Ordinal))
            {
                anyTopic.Add(resolver);
                continue;
            }

            if (!topicLookup.TryGetValue(resolver.Topic, out var list))
            {
                list = new List<IChannelResolver>();
                topicLookup[resolver.Topic] = list;
            }
            list.Add(resolver);
        }

        _byTopic = topicLookup.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<IChannelResolver>)pair.Value,
            StringComparer.Ordinal);
        _anyTopic = anyTopic;
    }

    public IReadOnlyList<ResolvedDelivery> Resolve(BusWatcherStreamService.BusWatcherMessage message)
    {
        var deliveries = new List<ResolvedDelivery>();

        if (_byTopic.TryGetValue(message.Topic, out var topicResolvers))
        {
            foreach (var resolver in topicResolvers)
            {
                deliveries.AddRange(resolver.Resolve(message));
            }
        }

        foreach (var resolver in _anyTopic)
        {
            deliveries.AddRange(resolver.Resolve(message));
        }

        return deliveries;
    }
}
