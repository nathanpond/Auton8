using AutoNate.Web.Services.Events;

namespace AutoNate.Web.Tests;

// Test fixture for IAuditEventPublisher. Records every PublishAsync call so
// per-endpoint tests can assert "this route published exactly these events
// with these payloads" — that's the only way to keep the EventCatalog and
// the runtime in sync as new domains land in subsequent phases.
public sealed class RecordingAuditEventPublisher : IAuditEventPublisher
{
    private readonly List<RecordedAuditEvent> _events = new();
    private readonly object _gate = new();

    public IReadOnlyList<RecordedAuditEvent> Events
    {
        get
        {
            lock (_gate) { return _events.ToArray(); }
        }
    }

    public void Clear()
    {
        lock (_gate) { _events.Clear(); }
    }

    public Task PublishAsync(
        string topicName,
        string eventType,
        string resourceKind,
        object? resource,
        object? details,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _events.Add(new RecordedAuditEvent(
                Topic: topicName,
                EventType: eventType,
                ResourceKind: resourceKind,
                Resource: resource,
                Details: details,
                CapturedAtUtc: DateTimeOffset.UtcNow));
        }
        return Task.CompletedTask;
    }
}

public sealed record RecordedAuditEvent(
    string Topic,
    string EventType,
    string ResourceKind,
    object? Resource,
    object? Details,
    DateTimeOffset CapturedAtUtc);
