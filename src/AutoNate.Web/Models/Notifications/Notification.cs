namespace AutoNate.Web.Models.Notifications;

public sealed record class Notification
{
    public Guid Id { get; init; }

    public Guid UserId { get; init; }

    public string Kind { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;

    public string? RelatedEntityKind { get; init; }

    public string? RelatedEntityId { get; init; }

    public string? ParentEntityKind { get; init; }

    public string? ParentEntityId { get; init; }

    public string? LinkPath { get; init; }

    public bool IsRead { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? ReadAtUtc { get; init; }
}

public static class NotificationKinds
{
    public const string RecordAssigned = "record.assigned";
    public const string WorkflowTaskAssigned = "workflow.task.assigned";

    // Self-healing platform: severity error/critical issues fan out an
    // in-app notification to operators (currently every super-admin) so
    // they hear about an outage even if they're not on the System Issues
    // page. Body carries the issue title; LinkPath deep-links to
    // /admin/config/system-issues.
    public const string SystemIssueOpened = "system.issue.opened";
}

public static class NotificationEntityKinds
{
    public const string Record = "record";
    public const string WorkflowTask = "workflow_task";
    public const string WorkflowExecution = "workflow_execution";
    public const string SystemIssue = "system_issue";
}
