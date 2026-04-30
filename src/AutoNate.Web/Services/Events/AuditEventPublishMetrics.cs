using System.Diagnostics.Metrics;

namespace AutoNate.Web.Services.Events;

// Single Meter shared by every publisher (record/application/notification/
// audit) so a missing audit trail is observable. Wired to OpenTelemetry by
// the host's metric reader; for now anyone scraping the .NET runtime metrics
// endpoint can inspect this directly.
public static class AuditEventPublishMetrics
{
    public const string MeterName = "AutoNate.AuditEvents";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> Failures = Meter.CreateCounter<long>(
        "audit_publish_failures_total",
        unit: "{event}",
        description: "Number of bus publish attempts that did not return 2xx.");

    private static readonly Counter<long> Successes = Meter.CreateCounter<long>(
        "audit_publish_total",
        unit: "{event}",
        description: "Number of bus publish attempts that completed successfully.");

    private static readonly Counter<long> Enqueued = Meter.CreateCounter<long>(
        "audit_outbox_enqueued_total",
        unit: "{event}",
        description: "Number of events written to the audit outbox awaiting dispatch.");

    private static readonly Counter<long> Dispatched = Meter.CreateCounter<long>(
        "audit_outbox_dispatched_total",
        unit: "{event}",
        description: "Number of outbox rows the dispatcher successfully published to Dapr.");

    private static readonly Counter<long> DispatchFailures = Meter.CreateCounter<long>(
        "audit_outbox_dispatch_failures_total",
        unit: "{event}",
        description: "Number of outbox dispatch attempts that failed (will be retried).");

    public static void RecordFailure(string topic, string reason) =>
        Failures.Add(1,
            new KeyValuePair<string, object?>("topic", topic),
            new KeyValuePair<string, object?>("reason", reason));

    public static void RecordSuccess(string topic) =>
        Successes.Add(1, new KeyValuePair<string, object?>("topic", topic));

    public static void RecordEnqueue(string topic) =>
        Enqueued.Add(1, new KeyValuePair<string, object?>("topic", topic));

    public static void RecordDispatched(string topic) =>
        Dispatched.Add(1, new KeyValuePair<string, object?>("topic", topic));

    public static void RecordDispatchFailure(string topic, string reason) =>
        DispatchFailures.Add(1,
            new KeyValuePair<string, object?>("topic", topic),
            new KeyValuePair<string, object?>("reason", reason));
}
