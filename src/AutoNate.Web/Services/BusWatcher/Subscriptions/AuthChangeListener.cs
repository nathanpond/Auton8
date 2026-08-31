using System.Text.Json;
using AutoNate.Web.Services.Authorization;

namespace AutoNate.Web.Services.BusWatcher.Subscriptions;

// Bridges iam.events mutations into SubscriptionManager.BroadcastAuthInvalidationAsync.
// Conservative: any mutation event causes every connection to rebuild its
// ActorAuthSnapshot, drop its AuthGate cache, and receive an `invalidate`
// frame listing its currently-subscribed channels.
//
// Per-event targeting (only rebuild affected actors) is a follow-up
// optimization — the current rate of permission changes is low enough that
// the broadcast cost is negligible, and the correctness story is simple:
// after any auth-relevant write, every connection re-evaluates from scratch.
public sealed class AuthChangeListener : IDisposable
{
    private static readonly HashSet<string> MutationEventTypes = new(StringComparer.Ordinal)
    {
        IamEventTypes.UserDeleted,
        IamEventTypes.SupervisorSet,
        IamEventTypes.SupervisorCleared,
        IamEventTypes.GroupArchived,
        IamEventTypes.GroupRestored,
        IamEventTypes.GroupDeleted,
        IamEventTypes.GroupMemberAdded,
        IamEventTypes.GroupMemberRemoved,
        IamEventTypes.RoleDeleted,
        IamEventTypes.RoleAssignmentGranted,
        IamEventTypes.RoleAssignmentRevoked,
        IamEventTypes.PermissionGrantCreated,
        IamEventTypes.PermissionGrantDeleted,
    };

    private readonly SubscriptionManager _manager;
    private readonly ILogger<AuthChangeListener> _logger;
    private IDisposable? _subscription;

    public AuthChangeListener(SubscriptionManager manager, ILogger<AuthChangeListener> logger)
    {
        _manager = manager;
        _logger = logger;
    }

    public void Start(BusWatcherStreamService bus)
    {
        if (_subscription is not null) return;
        _subscription = bus.Subscribe(HandleAsync);
    }

    private async Task HandleAsync(BusWatcherStreamService.BusWatcherMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsAuthMutation(message)) return;
        try
        {
            await _manager.BroadcastAuthInvalidationAsync(
                SubscriptionInvalidateReason.PermissionGrantChanged, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AuthChangeListener failed to broadcast invalidation for topic {Topic}.",
                message.Topic);
        }
    }

    private static bool IsAuthMutation(BusWatcherStreamService.BusWatcherMessage message)
    {
        if (!string.Equals(message.Topic, IamEventTopic.TopicName, StringComparison.Ordinal)) return false;
        if (string.IsNullOrWhiteSpace(message.Payload)) return false;
        try
        {
            using var document = JsonDocument.Parse(message.Payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!document.RootElement.TryGetProperty("eventType", out var eventType)) return false;
            return MutationEventTypes.Contains(eventType.GetString() ?? string.Empty);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public void Dispose() => _subscription?.Dispose();
}
