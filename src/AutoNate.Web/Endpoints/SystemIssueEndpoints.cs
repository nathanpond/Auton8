using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.SystemIssues;
using AutoNate.Web.Services.SystemIssues.Detectors;

namespace AutoNate.Web.Endpoints;

public sealed record SystemIssueResolveRequest(string? Notes);

public sealed record MenuRenderFailureReport(Guid MenuItemId);

public sealed record MenuRenderFailureResponse(int IssuesOpened, IReadOnlyList<string> Problems);

public sealed record SystemIssueDto(
    Guid Id,
    string DetectorId,
    string Category,
    string Severity,
    string Fingerprint,
    string Title,
    string? Summary,
    string? RelatedEntityKind,
    string? RelatedEntityId,
    string FactsJson,
    string State,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc,
    int OccurrenceCount,
    DateTimeOffset? AcknowledgedAtUtc,
    Guid? AcknowledgedBy,
    DateTimeOffset? ResolvedAtUtc,
    string? ResolutionKind,
    string? ResolutionNotes,
    int AutoRemediationAttemptCount,
    string? AutoRemediationLastError,
    DateTimeOffset? NextRemediationAfterUtc);

public sealed record SystemIssueListResponse(SystemIssueDto[] Items);

public static class SystemIssueEndpoints
{
    public static IEndpointRouteBuilder MapSystemIssueEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/system-issues").RequireAuthorization();

        group.MapGet("/", async (
            string? state,
            string? severity,
            string? category,
            int? skip,
            int? take,
            ISystemIssueStore store,
            CancellationToken ct) =>
        {
            // Default surface = "what needs attention right now": open issues
            // newest-first. Pass state="" to disable the filter and see the
            // full history.
            var query = new SystemIssueListQuery(
                State: state ?? SystemIssueStates.Open,
                Severity: string.IsNullOrEmpty(severity) ? null : severity,
                Category: string.IsNullOrEmpty(category) ? null : category,
                Skip: Math.Max(0, skip ?? 0),
                Take: Math.Clamp(take ?? 100, 1, 500));
            var results = await store.ListAsync(NormalizeQuery(query), ct);
            return Results.Ok(new SystemIssueListResponse(results.Select(ToDto).ToArray()));
        }).RequireKindPermission(EntityKinds.SystemIssue, Actions.View);

        group.MapGet("/{id:guid}", async (
            Guid id,
            ISystemIssueStore store,
            CancellationToken ct) =>
        {
            var issue = await store.GetAsync(id, ct);
            return issue is null ? Results.NotFound() : Results.Ok(ToDto(issue));
        }).RequireKindPermission(EntityKinds.SystemIssue, Actions.View);

        group.MapPost("/{id:guid}/acknowledge", async (
            Guid id,
            HttpContext http,
            ISystemIssueRecorder recorder,
            ISystemIssueStore store,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var updated = await recorder.AcknowledgeAsync(id, actorId, ct);
            if (updated is not null) return Results.Ok(ToDto(updated));
            // Distinguish "not found" from "already past Open" so the SPA
            // can render a useful message (e.g. someone else acked first).
            var existing = await store.GetAsync(id, ct);
            return existing is null
                ? Results.NotFound()
                : Results.Conflict(new { reason = "not_open", currentState = existing.State });
        }).RequireKindPermission(EntityKinds.SystemIssue, Actions.Acknowledge)
          .DisableAntiforgery();

        group.MapPost("/{id:guid}/resolve", async (
            Guid id,
            SystemIssueResolveRequest? body,
            HttpContext http,
            ISystemIssueRecorder recorder,
            ISystemIssueStore store,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var notes = body?.Notes;
            var updated = await recorder.ResolveAsync(id, actorId, notes, ct);
            if (updated is not null) return Results.Ok(ToDto(updated));
            var existing = await store.GetAsync(id, ct);
            return existing is null
                ? Results.NotFound()
                : Results.Conflict(new { reason = "already_resolved", currentState = existing.State });
        }).RequireKindPermission(EntityKinds.SystemIssue, Actions.Resolve)
          .DisableAntiforgery();

        // On-demand remediation. Bypasses the dispatcher's poll cadence so
        // an operator who's actively triaging gets immediate feedback.
        // Returns 404 if no IIssueRemediator matches, 409 if the issue is
        // not in the open state, otherwise the remediator's verdict.
        group.MapPost("/{id:guid}/remediate", async (
            Guid id,
            SystemIssueRemediationDispatcher dispatcher,
            ISystemIssueStore store,
            CancellationToken ct) =>
        {
            var outcome = await dispatcher.RemediateNowAsync(id, ct);
            switch (outcome)
            {
                case SystemIssueRemediationDispatcher.RemediationOutcome.NotFoundOutcome:
                    return Results.NotFound();
                case SystemIssueRemediationDispatcher.RemediationOutcome.NotEligibleOutcome:
                    var current = await store.GetAsync(id, ct);
                    return Results.Conflict(new
                    {
                        reason = "not_open",
                        currentState = current?.State
                    });
                case SystemIssueRemediationDispatcher.RemediationOutcome.NoRemediatorOutcome:
                    var detectorIssue = await store.GetAsync(id, ct);
                    return Results.NotFound(new
                    {
                        reason = "no_remediator_registered",
                        detectorId = detectorIssue?.DetectorId,
                        hint = "No IIssueRemediator is registered for this detector / fingerprint class."
                    });
                case SystemIssueRemediationDispatcher.RemediationOutcome.ResultOutcome r:
                    var refreshed = await store.GetAsync(id, ct);
                    return Results.Ok(new
                    {
                        outcome = r.Result switch
                        {
                            RemediationResult.Success => "success",
                            RemediationResult.Failure => "failure",
                            RemediationResult.Skip => "skip",
                            _ => "unknown"
                        },
                        notes = r.Result switch
                        {
                            RemediationResult.Success s => s.Notes,
                            RemediationResult.Failure f => f.Error,
                            RemediationResult.Skip s => s.Reason,
                            _ => null
                        },
                        issue = refreshed is null ? null : ToDto(refreshed)
                    });
                default:
                    return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }
        }).RequireKindPermission(EntityKinds.SystemIssue, Actions.Remediate)
          .DisableAntiforgery();

        // SPA-driven render-failure report. The nav silently drops menu
        // items whose config is incomplete; when that happens client-side,
        // the SPA POSTs the offending menu item id here and the backend
        // re-validates server-side via MisconfiguredMenuItemDetector.
        // Server-side re-validation means a hostile client can't spoof an
        // issue — only genuinely broken rows produce one. Open to any
        // authenticated user (no system-issue:* permission needed) because
        // anyone hitting a broken nav should be able to report it; the
        // re-validation guard makes the endpoint safe to expose.
        group.MapPost("/menu-render-failure", async (
            MenuRenderFailureReport report,
            MisconfiguredMenuItemDetector detector,
            CancellationToken ct) =>
        {
            if (report is null || report.MenuItemId == Guid.Empty)
            {
                return Results.BadRequest(new { reason = "menuItemId is required." });
            }
            var result = await detector.ScanItemAsync(report.MenuItemId, ct);
            return Results.Ok(new MenuRenderFailureResponse(result.Matched, result.Problems));
        }).DisableAntiforgery();

        return app;
    }
    private static SystemIssueListQuery NormalizeQuery(SystemIssueListQuery query)
    {
        // Treat state="" as "no filter" so the SPA can request full history.
        return query with { State = string.IsNullOrEmpty(query.State) ? null : query.State };
    }

    private static SystemIssueDto ToDto(SystemIssue m) => new(
        m.Id, m.DetectorId, m.Category, m.Severity, m.Fingerprint,
        m.Title, m.Summary, m.RelatedEntityKind, m.RelatedEntityId,
        m.FactsJson, m.State, m.FirstSeenAtUtc, m.LastSeenAtUtc, m.OccurrenceCount,
        m.AcknowledgedAtUtc, m.AcknowledgedBy, m.ResolvedAtUtc,
        m.ResolutionKind, m.ResolutionNotes,
        m.AutoRemediationAttemptCount, m.AutoRemediationLastError,
        m.NextRemediationAfterUtc);
}
