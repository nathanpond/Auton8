using AutoNate.Web.Services.BusWatcher.Subscriptions.Gates;
using AutoNate.Web.Services.BusWatcher.Subscriptions.Resolvers;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.BusWatcher.Subscriptions;

public static class ScopedSubscriptionsServiceCollectionExtensions
{
    public static IServiceCollection AddScopedSubscriptions(this IServiceCollection services)
    {
        services.AddSingleton<SubscriptionRegistry>();
        services.AddSingleton<ChannelResolverRegistry>();
        services.AddSingleton<ChannelSubscribeGateRegistry>();
        services.AddSingleton<SubscriptionManager>();
        services.AddSingleton<AuthChangeListener>();

        // Resolvers (per-topic and the firehose fallback). New resolvers are
        // added here as later phases land their channels.
        services.AddSingleton<IChannelResolver, FirehoseFallbackResolver>();
        services.AddSingleton<IChannelResolver, NotificationChannelResolver>();
        services.AddSingleton<IChannelResolver, RecordChannelResolver>();
        services.AddSingleton<IChannelResolver, WorkflowChannelResolver>();
        services.AddSingleton<IChannelResolver, ContentChannelResolver>();
        services.AddSingleton<IChannelResolver, ExternalConnectionChannelResolver>();

        // Subscribe gates — keyed by channel kind.
        services.AddSingleton<IChannelSubscribeGate, FirehoseChannelSubscribeGate>();
        services.AddSingleton<IChannelSubscribeGate, TopicChannelSubscribeGate>();
        services.AddSingleton<IChannelSubscribeGate, NotificationChannelSubscribeGate>();
        services.AddSingleton<IChannelSubscribeGate, RecordInstanceChannelSubscribeGate>();
        services.AddSingleton<IChannelSubscribeGate, RecordsListChannelSubscribeGate>();
        services.AddSingleton<IChannelSubscribeGate, WorkflowExecutionInstanceChannelSubscribeGate>();
        services.AddSingleton<IChannelSubscribeGate, WorkflowTaskInstanceChannelSubscribeGate>();
        services.AddSingleton<IChannelSubscribeGate, WorkflowExecutionsListChannelSubscribeGate>();
        services.AddSingleton<IChannelSubscribeGate, WorkflowTasksListChannelSubscribeGate>();
        services.AddSingleton<IChannelSubscribeGate, MyTasksListChannelSubscribeGate>();
        services.AddSingleton<IChannelSubscribeGate, PageChannelSubscribeGate>();
        services.AddSingleton<IChannelSubscribeGate, ExternalConnectionChannelSubscribeGate>();

        return services;
    }
}
