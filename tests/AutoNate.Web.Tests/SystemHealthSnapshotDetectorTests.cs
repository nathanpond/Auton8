using System.Text.Json;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.SystemHealth;
using AutoNate.Web.Services.SystemIssues;
using AutoNate.Web.Services.SystemIssues.Detectors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class SystemHealthSnapshotDetectorTests
{
    [Fact]
    public async Task Down_component_opens_an_issue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var detector = CreateDetector(db);

        var report = ReportWith(
            new ComponentHealth("postgres", "PostgreSQL", "database",
                HealthStatus.Down, "Refused connection", null, 12));
        await detector.ProcessReportAsync(report, CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal("health:component:postgres", issue.Fingerprint);
        Assert.Equal(SystemIssueSeverities.Error, issue.Severity);
        Assert.Equal(SystemIssueStates.Open, issue.State);
        Assert.Contains("PostgreSQL is Down", issue.Title);
        using var facts = JsonDocument.Parse(issue.FactsJson);
        Assert.Equal("postgres", facts.RootElement.GetProperty("componentId").GetString());
        Assert.Equal("Down", facts.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Component_back_up_on_next_tick_resolves_the_issue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var detector = CreateDetector(db);

        await detector.ProcessReportAsync(ReportWith(
            new ComponentHealth("postgres", "PostgreSQL", "database",
                HealthStatus.Down, "Refused connection", null, 12)),
            CancellationToken.None);
        await detector.ProcessReportAsync(ReportWith(
            new ComponentHealth("postgres", "PostgreSQL", "database",
                HealthStatus.Up, "Reachable", null, 8)),
            CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(SystemIssueStates.AutoResolved, issue.State);
        Assert.Equal(SystemIssueResolutionKinds.NoLongerPresent, issue.ResolutionKind);
    }

    [Fact]
    public async Task Degraded_component_opens_a_warning_severity_issue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var detector = CreateDetector(db);

        await detector.ProcessReportAsync(ReportWith(
            new ComponentHealth("nats", "NATS / JetStream", "broker",
                HealthStatus.Degraded, "High latency", null, 850)),
            CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(SystemIssueSeverities.Warning, issue.Severity);
        Assert.Contains("Degraded", issue.Title);
    }

    [Fact]
    public async Task Down_connection_opens_an_issue_with_directional_fingerprint()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var detector = CreateDetector(db);

        await detector.ProcessReportAsync(new SystemHealthReport(
                CheckedAtUtc: DateTimeOffset.UtcNow,
                Components: Array.Empty<ComponentHealth>(),
                Connections: new[]
                {
                    new ConnectionHealth("dapr-pubsub", "nats", "JetStream pub/sub",
                        HealthStatus.Down, "nats: connection closed", null)
                }),
            CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal("health:connection:dapr-pubsub->nats", issue.Fingerprint);
        Assert.Equal(SystemIssueSeverities.Error, issue.Severity);
    }

    [Fact]
    public async Task Repeated_down_status_dedups_to_one_row_with_bumped_count()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var detector = CreateDetector(db);

        var down = ReportWith(
            new ComponentHealth("flowable", "Flowable", "service",
                HealthStatus.Down, "HTTP 503", null, 25));
        await detector.ProcessReportAsync(down, CancellationToken.None);
        await detector.ProcessReportAsync(down, CancellationToken.None);
        await detector.ProcessReportAsync(down, CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(3, issue.OccurrenceCount);
        Assert.Equal(SystemIssueStates.Open, issue.State);
    }

    private static SystemHealthSnapshotDetector CreateDetector(PostgresTestDatabase db)
    {
        var store = new EfCoreSystemIssueStore(db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());
        return new SystemHealthSnapshotDetector(
            healthService: new StubHealthProbe(),
            recorder: store,
            issueStore: store,
            snapshotOptions: Options.Create(new SystemHealthSnapshotOptions()),
            systemIssueOptions: Options.Create(new SystemIssueOptions { DetectorsEnabled = false }),
            logger: NullLogger<SystemHealthSnapshotDetector>.Instance);
    }

    private static SystemHealthReport ReportWith(params ComponentHealth[] components) =>
        new(CheckedAtUtc: DateTimeOffset.UtcNow,
            Components: components,
            Connections: Array.Empty<ConnectionHealth>());

    // Tests drive ProcessReportAsync directly with constructed reports, so
    // CheckAsync is never called. This stub exists only to satisfy the
    // detector's constructor.
    private sealed class StubHealthProbe : ISystemHealthProbe
    {
        public Task<SystemHealthReport> CheckAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "StubHealthProbe.CheckAsync should not be called by tests that drive ProcessReportAsync directly.");
    }
}
