using System;

namespace AutoNate.Web.Persistence.Scaffolded;

// Records the actor who actually completed a Flowable user task — needed
// because Flowable only persists the assignee on the historic task, not
// who triggered the completion. The execution log surfaces this so an
// admin force-completing someone else's task can be distinguished from
// the assignee completing their own.
public partial class WorkflowTaskCompletion
{
    // Flowable task id; globally unique. PK so a retry overwrites the
    // previous entry rather than producing duplicates.
    public string TaskId { get; set; } = null!;

    public string CompletedByUserId { get; set; } = null!;

    public DateTime CompletedAtUtc { get; set; }

    // True when the completion came through the override endpoint
    // (/api/executions/.../force-complete). False for normal completions.
    public bool WasOverride { get; set; }
}
