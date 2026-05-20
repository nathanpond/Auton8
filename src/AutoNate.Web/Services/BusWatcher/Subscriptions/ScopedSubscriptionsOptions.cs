namespace AutoNate.Web.Services.BusWatcher.Subscriptions;

// Gate for the scoped-subscription /ws/bus-watcher path. Default off; when
// on, the endpoint dispatches to SubscriptionManager and the SubscriptionManager
// is bridged to BusWatcherStreamService's in-process notifier so every Dapr
// message reaches the new fan-out path. When off, the Phase 1 superadmin-only
// broadcast loop in BusWatcherStreamService handles connections.
public sealed class ScopedSubscriptionsOptions
{
    public const string SectionName = "Features:ScopedSubscriptions";

    public bool Enabled { get; set; } = false;
}
