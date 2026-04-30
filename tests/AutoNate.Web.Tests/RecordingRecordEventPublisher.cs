using AutoNate.Web.Services.Records;

namespace AutoNate.Web.Tests;

// Captures every RecordEventEnvelope published by EfCoreRecordStore so phase
// tests can assert "this mutation produced exactly these record events" —
// useful especially for Phase 3 gap-fill events (record.restored, record.
// assignees.changed) that flow through the typed IRecordEventPublisher
// rather than the cross-cutting IAuditEventPublisher.
public sealed class RecordingRecordEventPublisher : IRecordEventPublisher
{
    private readonly List<RecordEventEnvelope> _events = new();
    private readonly object _gate = new();

    public IReadOnlyList<RecordEventEnvelope> Events
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

    public Task PublishAsync(RecordEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        lock (_gate) { _events.Add(envelope); }
        return Task.CompletedTask;
    }
}
