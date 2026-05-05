using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class WorkflowExecutionError
{
    public Guid Id { get; set; }

    public string ProcessInstanceId { get; set; } = null!;

    public string ActivityId { get; set; } = null!;

    public string? ActivityName { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ErrorStackTrace { get; set; }

    public string? RawFlowableEventType { get; set; }

    public DateTime OccurredAtUtc { get; set; }
}
