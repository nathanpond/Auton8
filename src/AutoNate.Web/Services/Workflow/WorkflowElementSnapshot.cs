namespace AutoNate.Web.Services.Workflow;

public sealed record class WorkflowElementSnapshot(
    string Id,
    string Type,
    string? Name,
    string? ScriptFormat = null,
    string? Script = null,
    string? ResultVariable = null,
    string? ConditionExpression = null,
    string? Assignee = null,
    IReadOnlyList<string>? CandidateUsers = null,
    IReadOnlyList<string>? CandidateGroups = null,
    string? DueDate = null,
    string? SignalName = null,
    string? SignalTopic = null,
    IReadOnlyList<string>? RecordTypeShortCodes = null,
    string? TimerCycleCron = null,
    string? TimerEndDate = null,
    string? TimerDuration = null,
    string? TimerDate = null,
    string? ServiceTaskKind = null,
    string? BehaviorKey = null,
    // #158. Appended, never inserted — this is a positional record and existing
    // callers bind by position.
    //
    // ConditionExpression above is reused for a conditional event's condition
    // rather than a new field: it is the same concept, and the studio routes on
    // $type plus key presence, so a sequence flow and an intermediate catch event
    // cannot be confused.
    bool? CancelActivity = null,
    // #157. Appended, never inserted — positional record.
    //
    // Separate from TimerDuration/TimerDate/TimerCycleCron rather than reusing them:
    // describeBusinessObject's output IS the snapshot wire format, and the studio
    // routes on $type PLUS key presence. Reusing the start-event and
    // intermediate-catch keys would send a timer boundary to whichever of those
    // editors matched first.
    string? BoundaryTimerDuration = null,
    string? BoundaryTimerDate = null,
    string? BoundaryTimerCycle = null,
    // #168. Appended, never inserted — positional record.
    //
    // Nullable rather than bool so "the studio did not send this" and "the
    // author turned it off" stay distinguishable: a snapshot from an older SPA
    // build must not silently clear a retry point someone set.
    bool? RetryPoint = null);

// Pair extracted from a published workflow's BPMN XML: a signal start event's
// signal name (matched against the inbound message's `eventType`) and the Dapr
// pub/sub topic the bus subscriber should listen on for that signal. The
// process definition key identifies the workflow to start; the
// RecordTypeShortCodes set is empty for unfiltered registrations and contains
// shortcodes the payload's `recordTypeId` must resolve to for filtered ones.
public sealed record class WorkflowSignalRegistration(
    string SignalName,
    string Topic,
    string ProcessDefinitionKey,
    IReadOnlySet<string> RecordTypeShortCodes);
