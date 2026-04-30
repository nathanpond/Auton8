using System;

namespace AutoNate.Web.Persistence.Scaffolded;

// Phase 5 of the audit-events plan: durable outbox between the event publishers
// and the bus. Every event written here is guaranteed to land on the bus
// eventually (or an operator-visible alert fires). See EfCoreAuditEventOutbox
// (writer) and AuditOutboxDispatcher (reader/dispatcher).
public partial class AuditOutboxEntry
{
    public long Id { get; set; }

    public string Topic { get; set; } = null!;

    public string EventType { get; set; } = null!;

    // Serialized event envelope — the bytes that will be POSTed to Dapr verbatim.
    public string PayloadJson { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    // Null until the dispatcher successfully publishes to Dapr.
    public DateTime? DispatchedAtUtc { get; set; }

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }

    // The dispatcher claims rows where dispatched_at_utc IS NULL AND
    // next_attempt_after_utc <= now(). Initialized to created_at_utc so the
    // first attempt fires immediately.
    public DateTime NextAttemptAfterUtc { get; set; }
}
