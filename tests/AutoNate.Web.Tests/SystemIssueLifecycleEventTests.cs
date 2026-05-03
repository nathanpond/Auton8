using System.Reflection;
using AutoNate.Web.Services.SystemIssues;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class SystemIssueLifecycleEventTests
{
    [Fact]
    public async Task RecordAsync_publishes_opened_on_first_insert()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var publisher = new RecordingAuditEventPublisher();
        var store = new EfCoreSystemIssueStore(db.CreateDbContextFactory(), publisher, new NoopCriticalIssueNotifier());

        var result = await store.RecordAsync(NewDraft("test:opened", SystemIssueSeverities.Warning));

        var ev = Assert.Single(publisher.Events);
        Assert.Equal(SystemIssueEventTopic.TopicName, ev.Topic);
        Assert.Equal(SystemIssueEventTypes.Opened, ev.EventType);
        Assert.Equal(SystemIssueEventTopic.ResourceKind, ev.ResourceKind);
        Assert.Equal(result.IssueId, ReadGuidProperty(ev.Resource, "id"));
        Assert.Equal("test:opened", ReadStringProperty(ev.Resource, "fingerprint"));
        Assert.Equal(SystemIssueSeverities.Warning, ReadStringProperty(ev.Resource, "severity"));
    }

    [Fact]
    public async Task RecordAsync_dedup_without_severity_change_does_not_republish()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var publisher = new RecordingAuditEventPublisher();
        var store = new EfCoreSystemIssueStore(db.CreateDbContextFactory(), publisher, new NoopCriticalIssueNotifier());

        await store.RecordAsync(NewDraft("test:dedup", SystemIssueSeverities.Warning));
        await store.RecordAsync(NewDraft("test:dedup", SystemIssueSeverities.Warning));
        await store.RecordAsync(NewDraft("test:dedup", SystemIssueSeverities.Warning));

        // One Opened, no SeverityEscalated — bumps without severity change
        // are silent so a chronic issue doesn't dominate the audit firehose.
        Assert.Single(publisher.Events);
        Assert.Equal(SystemIssueEventTypes.Opened, publisher.Events[0].EventType);
    }

    [Fact]
    public async Task RecordAsync_severity_change_publishes_escalated()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var publisher = new RecordingAuditEventPublisher();
        var store = new EfCoreSystemIssueStore(db.CreateDbContextFactory(), publisher, new NoopCriticalIssueNotifier());

        await store.RecordAsync(NewDraft("test:escalate", SystemIssueSeverities.Warning));
        await store.RecordAsync(NewDraft("test:escalate", SystemIssueSeverities.Error));

        Assert.Equal(2, publisher.Events.Count);
        Assert.Equal(SystemIssueEventTypes.Opened, publisher.Events[0].EventType);
        Assert.Equal(SystemIssueEventTypes.SeverityEscalated, publisher.Events[1].EventType);
        Assert.Equal(SystemIssueSeverities.Warning, ReadStringProperty(publisher.Events[1].Details, "previousSeverity"));
        Assert.Equal(SystemIssueSeverities.Error, ReadStringProperty(publisher.Events[1].Resource, "severity"));
    }

    [Fact]
    public async Task AcknowledgeAsync_publishes_acknowledged_with_actor()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var publisher = new RecordingAuditEventPublisher();
        var store = new EfCoreSystemIssueStore(db.CreateDbContextFactory(), publisher, new NoopCriticalIssueNotifier());

        var insert = await store.RecordAsync(NewDraft("test:ack", SystemIssueSeverities.Warning));
        publisher.Clear();
        var actor = Guid.NewGuid();

        var ack = await store.AcknowledgeAsync(insert.IssueId, actor);

        Assert.NotNull(ack);
        Assert.Equal(SystemIssueStates.Acknowledged, ack!.State);
        var ev = Assert.Single(publisher.Events);
        Assert.Equal(SystemIssueEventTypes.Acknowledged, ev.EventType);
        Assert.Equal(actor, ReadGuidProperty(ev.Details, "acknowledgedBy"));
    }

    [Fact]
    public async Task AcknowledgeAsync_returns_null_when_not_open()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var publisher = new RecordingAuditEventPublisher();
        var store = new EfCoreSystemIssueStore(db.CreateDbContextFactory(), publisher, new NoopCriticalIssueNotifier());

        var insert = await store.RecordAsync(NewDraft("test:ack-twice", SystemIssueSeverities.Warning));
        await store.AcknowledgeAsync(insert.IssueId, Guid.NewGuid());

        // Second ack on an already-acknowledged row is a no-op.
        var second = await store.AcknowledgeAsync(insert.IssueId, Guid.NewGuid());
        Assert.Null(second);
    }

    [Fact]
    public async Task ResolveAsync_publishes_resolved_with_notes()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var publisher = new RecordingAuditEventPublisher();
        var store = new EfCoreSystemIssueStore(db.CreateDbContextFactory(), publisher, new NoopCriticalIssueNotifier());

        var insert = await store.RecordAsync(NewDraft("test:resolve", SystemIssueSeverities.Warning));
        publisher.Clear();
        var actor = Guid.NewGuid();

        var resolved = await store.ResolveAsync(insert.IssueId, actor, notes: "investigated, false alarm");

        Assert.NotNull(resolved);
        Assert.Equal(SystemIssueStates.Resolved, resolved!.State);
        Assert.Equal(SystemIssueResolutionKinds.Manual, resolved.ResolutionKind);
        var ev = Assert.Single(publisher.Events);
        Assert.Equal(SystemIssueEventTypes.Resolved, ev.EventType);
        Assert.Equal("investigated, false alarm", ReadStringProperty(ev.Details, "notes"));
    }

    [Fact]
    public async Task MarkResolvedByFingerprintAsync_publishes_auto_resolved_for_machine_close()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var publisher = new RecordingAuditEventPublisher();
        var store = new EfCoreSystemIssueStore(db.CreateDbContextFactory(), publisher, new NoopCriticalIssueNotifier());

        await store.RecordAsync(NewDraft("test:auto", SystemIssueSeverities.Warning));
        publisher.Clear();

        var resolved = await store.MarkResolvedByFingerprintAsync(
            "test:auto", SystemIssueResolutionKinds.NoLongerPresent, notes: "component back up");

        Assert.NotNull(resolved);
        Assert.Equal(SystemIssueStates.AutoResolved, resolved!.State);
        var ev = Assert.Single(publisher.Events);
        Assert.Equal(SystemIssueEventTypes.AutoResolved, ev.EventType);
        Assert.Equal(SystemIssueResolutionKinds.NoLongerPresent, ReadStringProperty(ev.Details, "resolutionKind"));
    }

    private static SystemIssueDraft NewDraft(string fingerprint, string severity) => new(
        DetectorId: "test.detector",
        Category: SystemIssueCategories.Bus,
        Severity: severity,
        Fingerprint: fingerprint,
        Title: "test issue");

    // Anonymous-type payloads pass through IAuditEventPublisher; the recorder
    // captures them verbatim. Reflect to read the fields without taking a
    // dependency on the (private) anonymous types.
    private static string? ReadStringProperty(object? obj, string name)
    {
        if (obj is null) return null;
        var prop = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        return prop?.GetValue(obj) as string;
    }

    private static Guid ReadGuidProperty(object? obj, string name)
    {
        if (obj is null) return Guid.Empty;
        var prop = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        return prop?.GetValue(obj) is Guid g ? g : Guid.Empty;
    }
}
