using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Services.SystemIssues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class SystemIssueEndpointsTests
{
    [Fact]
    public async Task List_returns_open_issues_by_default()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var recorder = factory.Services.GetRequiredService<ISystemIssueRecorder>();
        await recorder.RecordAsync(NewDraft("e2e:open", SystemIssueSeverities.Warning, "Open one"));
        var resolved = await recorder.RecordAsync(NewDraft("e2e:resolved", SystemIssueSeverities.Warning, "Resolved one"));
        await recorder.MarkResolvedByFingerprintAsync(
            "e2e:resolved", SystemIssueResolutionKinds.NoLongerPresent, notes: null);

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me"); // prime auto-login

        var resp = await client.GetFromJsonAsync<ListResponse>("/api/system-issues");
        Assert.NotNull(resp);
        // Default state filter is "open" — the resolved row must not appear.
        Assert.Single(resp!.Items);
        Assert.Equal("Open one", resp.Items[0].Title);
        Assert.NotEqual(resolved.IssueId, resp.Items[0].Id);
    }

    [Fact]
    public async Task Get_returns_404_for_missing_issue()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync($"/api/system-issues/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Acknowledge_flips_state_and_records_actor()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var recorder = factory.Services.GetRequiredService<ISystemIssueRecorder>();
        var inserted = await recorder.RecordAsync(NewDraft("e2e:ack", SystemIssueSeverities.Warning, "Ack me"));

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsync($"/api/system-issues/{inserted.IssueId}/acknowledge", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<IssueDto>();
        Assert.Equal(SystemIssueStates.Acknowledged, dto!.State);
        Assert.NotNull(dto.AcknowledgedBy);

        // System-of-record check: the row in the DB matches the response.
        await using var db = factory.Services.GetRequiredService<IDbContextFactory<AutoNate.Web.Persistence.AutoNateDbContext>>()
            .CreateDbContext();
        var row = await db.SystemIssues.FirstAsync(i => i.Id == inserted.IssueId);
        Assert.Equal(SystemIssueStates.Acknowledged, row.State);
        Assert.Equal(dto.AcknowledgedBy, row.AcknowledgedBy);
    }

    [Fact]
    public async Task Acknowledge_returns_409_when_already_past_open()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var recorder = factory.Services.GetRequiredService<ISystemIssueRecorder>();
        var inserted = await recorder.RecordAsync(NewDraft("e2e:ack-twice", SystemIssueSeverities.Warning, "Ack twice"));

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var first = await client.PostAsync($"/api/system-issues/{inserted.IssueId}/acknowledge", content: null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsync($"/api/system-issues/{inserted.IssueId}/acknowledge", content: null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Resolve_with_notes_closes_issue_with_manual_kind()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var recorder = factory.Services.GetRequiredService<ISystemIssueRecorder>();
        var inserted = await recorder.RecordAsync(NewDraft("e2e:resolve", SystemIssueSeverities.Error, "Resolve me"));

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            $"/api/system-issues/{inserted.IssueId}/resolve",
            new { notes = "investigated, took action" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<IssueDto>();
        Assert.Equal(SystemIssueStates.Resolved, dto!.State);
        Assert.Equal(SystemIssueResolutionKinds.Manual, dto.ResolutionKind);
        Assert.Equal("investigated, took action", dto.ResolutionNotes);
    }

    [Fact]
    public async Task Resolve_with_no_body_still_works()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var recorder = factory.Services.GetRequiredService<ISystemIssueRecorder>();
        var inserted = await recorder.RecordAsync(NewDraft("e2e:resolve-no-body", SystemIssueSeverities.Warning, "No body"));

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsync($"/api/system-issues/{inserted.IssueId}/resolve", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<IssueDto>();
        Assert.Equal(SystemIssueStates.Resolved, dto!.State);
        Assert.Null(dto.ResolutionNotes);
    }

    [Fact]
    public async Task Remediate_returns_404_when_no_remediator_matches_detector()
    {
        // No remediator handles a synthetic test detector id, even with the
        // Phase 4 remediators registered. The endpoint should surface that.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var recorder = factory.Services.GetRequiredService<ISystemIssueRecorder>();
        var inserted = await recorder.RecordAsync(new SystemIssueDraft(
            DetectorId: "test.no-remediator-for-this",
            Category: SystemIssueCategories.Bus,
            Severity: SystemIssueSeverities.Warning,
            Fingerprint: "e2e:remediate-no-handler",
            Title: "no handler",
            RemediationDueAtUtc: DateTime.UtcNow));

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsync($"/api/system-issues/{inserted.IssueId}/remediate", content: null);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("no_remediator_registered", body);
    }

    [Fact]
    public async Task Remediate_runs_park_remediator_on_demand_for_dead_letter_issue()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();

        // Seed a dead-lettered audit_outbox row + open the matching issue
        // through the recorder so the dispatcher will route it to the
        // registered AuditOutboxDeadLetterParkRemediator.
        var dbFactory = factory.Services.GetRequiredService<
            IDbContextFactory<AutoNate.Web.Persistence.AutoNateDbContext>>();
        long deadId;
        await using (var seed = await dbFactory.CreateDbContextAsync())
        {
            var row = new AutoNate.Web.Persistence.Scaffolded.AuditOutboxEntry
            {
                Topic = "test.events",
                EventType = "test.dead",
                PayloadJson = "{}",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-30),
                NextAttemptAfterUtc = DateTime.UtcNow,
                AttemptCount = 50
            };
            seed.AuditOutbox.Add(row);
            await seed.SaveChangesAsync();
            deadId = row.Id;
        }

        var recorder = factory.Services.GetRequiredService<ISystemIssueRecorder>();
        var inserted = await recorder.RecordAsync(new SystemIssueDraft(
            DetectorId: AutoNate.Web.Services.SystemIssues.Detectors.AuditOutboxDeadLetterDetector.DetectorIdValue,
            Category: SystemIssueCategories.Bus,
            Severity: SystemIssueSeverities.Error,
            Fingerprint: $"audit_outbox:dead_letter:{deadId}",
            Title: $"dead {deadId}",
            RelatedEntityKind: "audit_outbox",
            RelatedEntityId: deadId.ToString(),
            FactsJson: $"{{\"outboxRowId\":{deadId}}}",
            RemediationDueAtUtc: DateTime.UtcNow));

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsync($"/api/system-issues/{inserted.IssueId}/remediate", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("\"outcome\":\"success\"", body);

        // Side effects: outbox row gone, issue auto-resolved.
        await using var read = await dbFactory.CreateDbContextAsync();
        Assert.Equal(0, await read.AuditOutbox.AsNoTracking().CountAsync(r => r.Id == deadId));
        var issue = await read.SystemIssues.AsNoTracking().FirstAsync(i => i.Id == inserted.IssueId);
        Assert.Equal(SystemIssueStates.AutoResolved, issue.State);
        Assert.Equal(SystemIssueResolutionKinds.AutoRemediated, issue.ResolutionKind);
    }

    [Fact]
    public async Task Acknowledge_returns_403_without_permission()
    {
        // Authorization on, no super-admin backfill — admin user has no grants.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(new Dictionary<string, string?>
        {
            ["Authorization:Enabled"] = "true",
            ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
            ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
        });
        var recorder = factory.Services.GetRequiredService<ISystemIssueRecorder>();
        var inserted = await recorder.RecordAsync(NewDraft("e2e:perm", SystemIssueSeverities.Warning, "Need perm"));

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsync($"/api/system-issues/{inserted.IssueId}/acknowledge", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    private static SystemIssueDraft NewDraft(string fingerprint, string severity, string title) => new(
        DetectorId: "test.detector",
        Category: SystemIssueCategories.Bus,
        Severity: severity,
        Fingerprint: fingerprint,
        Title: title);

    private sealed record ListResponse(IssueDto[] Items);

    private sealed record IssueDto(
        Guid Id,
        string DetectorId,
        string Category,
        string Severity,
        string Fingerprint,
        string Title,
        string? Summary,
        string State,
        Guid? AcknowledgedBy,
        string? ResolutionKind,
        string? ResolutionNotes);
}
