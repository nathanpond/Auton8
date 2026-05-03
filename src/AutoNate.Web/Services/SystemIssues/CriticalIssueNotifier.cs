using AutoNate.Web.Authorization;
using AutoNate.Web.Models.Notifications;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.SystemIssues;

// Fans out an in-app notification when a fresh issue with severity error or
// critical opens. The plan calls for "addressed to every user with
// system-issue:read"; today we approximate that as every direct super-admin
// user assignment (super-admins implicitly have all permissions). Group-
// scoped super-admins are out of scope for Phase 3 — expanding groups →
// members lands as a follow-up when permission-aware fan-out grows up.
//
// The recorder is a singleton; INotificationStore and IRoleAssignmentStore
// are scoped. We resolve them through IServiceScopeFactory per call so the
// recorder doesn't leak scoped lifetimes.
public interface ICriticalIssueNotifier
{
    Task NotifyOpenedAsync(
        Guid issueId,
        string severity,
        string title,
        string? summary,
        CancellationToken cancellationToken);
}

public sealed class CriticalIssueNotifier(
    IServiceScopeFactory scopeFactory,
    ILogger<CriticalIssueNotifier> logger) : ICriticalIssueNotifier
{
    public async Task NotifyOpenedAsync(
        Guid issueId,
        string severity,
        string title,
        string? summary,
        CancellationToken cancellationToken)
    {
        // Only error and critical fan out — info / warning would create a
        // notification storm during a degraded period (the System Issues
        // page is the primary surface for those).
        if (!IsHighSeverity(severity)) return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var roleAssignments = scope.ServiceProvider.GetRequiredService<IRoleAssignmentStore>();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationStore>();

            var assignments = await roleAssignments.ListByRoleAsync(SystemRoles.SuperAdminId, cancellationToken);
            var userIds = new HashSet<Guid>();
            foreach (var assignment in assignments)
            {
                // Direct user assignments only — group expansion is a
                // follow-up. Most installs grant SuperAdmin to a small set
                // of named users so this covers the practical case.
                if (!string.Equals(assignment.PrincipalKind, "user", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!Guid.TryParse(assignment.PrincipalId, out var userId)) continue;
                userIds.Add(userId);
            }

            if (userIds.Count == 0)
            {
                logger.LogDebug(
                    "No direct super-admin user assignments — skipping critical-issue notification fan-out for {IssueId}.",
                    issueId);
                return;
            }

            var body = string.IsNullOrWhiteSpace(summary)
                ? $"A {severity} system issue was opened."
                : summary;

            foreach (var userId in userIds)
            {
                try
                {
                    await notifications.CreateAsync(new CreateNotificationInput(
                        UserId: userId,
                        Kind: NotificationKinds.SystemIssueOpened,
                        Title: title,
                        Body: body,
                        RelatedEntityKind: NotificationEntityKinds.SystemIssue,
                        RelatedEntityId: issueId.ToString(),
                        LinkPath: "/admin/config/system-issues"
                    ), cancellationToken);
                }
                catch (Exception ex)
                {
                    // Don't let one bad user fail the rest of the fan-out.
                    logger.LogWarning(ex,
                        "Failed to deliver critical-issue notification for issue {IssueId} to user {UserId}.",
                        issueId, userId);
                }
            }
        }
        catch (Exception ex)
        {
            // Notification fan-out is best-effort — the issue row is the
            // source of truth and the SPA will pick it up regardless.
            logger.LogError(ex,
                "Critical-issue notification fan-out failed for issue {IssueId}.", issueId);
        }
    }

    private static bool IsHighSeverity(string severity) =>
        string.Equals(severity, SystemIssueSeverities.Error, StringComparison.Ordinal)
        || string.Equals(severity, SystemIssueSeverities.Critical, StringComparison.Ordinal);
}

// Test default — the issue store always calls into the notifier; tests that
// don't want fan-out plug this in.
public sealed class NoopCriticalIssueNotifier : ICriticalIssueNotifier
{
    public Task NotifyOpenedAsync(
        Guid issueId,
        string severity,
        string title,
        string? summary,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
