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
    string? TimerCycleCron = null,
    string? TimerEndDate = null,
    string? TimerDuration = null,
    string? TimerDate = null);

// Pair extracted from a published workflow's BPMN XML: a signal start event's
// signal name (matched against the inbound message's `eventType`) and the Dapr
// pub/sub topic the bus subscriber should listen on for that signal.
public sealed record class WorkflowSignalRegistration(string SignalName, string Topic);
