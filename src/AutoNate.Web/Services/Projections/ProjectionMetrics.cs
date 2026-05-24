using System.Diagnostics.Metrics;

namespace AutoNate.Web.Services.Projections;

// OpenTelemetry-friendly meters following the same shape as
// AuditEventPublishMetrics. Wired to the same global meter provider so
// Prometheus scraping picks them up alongside the rest of the platform.
internal static class ProjectionMetrics
{
    private static readonly Meter Meter = new("AutoNate.Projections", "1.0");

    private static readonly Counter<long> AppliedCounter =
        Meter.CreateCounter<long>("projection.events_applied_total");

    private static readonly Counter<long> FailureCounter =
        Meter.CreateCounter<long>("projection.batch_failures_total");

    private static readonly Counter<long> DriftCounter =
        Meter.CreateCounter<long>("projection.reconcile_drift_total");

    // Lag is wired lazily from the health service — the gauge calls back
    // into the captured snapshot delegate so the meter publishes whatever
    // the current health state says without per-tick callbacks.
    private static Func<IReadOnlyList<(string Name, double Seconds)>>? _lagSampler;

    private static readonly ObservableGauge<double> LagGauge =
        Meter.CreateObservableGauge<double>("projection.lag_seconds", () =>
        {
            var sampler = _lagSampler;
            if (sampler is null) return Array.Empty<Measurement<double>>();
            return sampler().Select(sample => new Measurement<double>(
                sample.Seconds,
                new KeyValuePair<string, object?>("projection", sample.Name))).ToArray();
        });

    // Called once from DI wiring so the gauge knows where to pull samples.
    // Tests can call this multiple times; last writer wins.
    public static void ConfigureLagSampler(Func<IReadOnlyList<(string Name, double Seconds)>> sampler) =>
        _lagSampler = sampler;

    public static void RecordApplied(string projection, string feed, int eventCount) =>
        AppliedCounter.Add(eventCount,
            new KeyValuePair<string, object?>("projection", projection),
            new KeyValuePair<string, object?>("feed", feed));

    public static void RecordFailure(string projection, string feed) =>
        FailureCounter.Add(1,
            new KeyValuePair<string, object?>("projection", projection),
            new KeyValuePair<string, object?>("feed", feed));

    public static void RecordDrift(string projection, string kind, int count) =>
        DriftCounter.Add(count,
            new KeyValuePair<string, object?>("projection", projection),
            new KeyValuePair<string, object?>("kind", kind));
}
