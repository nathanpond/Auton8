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

    public string? LinkPath { get; init; }

    public bool IsRead { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? ReadAtUtc { get; init; }
}

public static class NotificationKinds
{
    public const string RecordAssigned = "record.assigned";
    public const string WorkflowTaskAssigned = "workflow.task.assigned";
}

public static class NotificationEntityKinds
{
    public const string Record = "record";
    public const string WorkflowTask = "workflow_task";
}
