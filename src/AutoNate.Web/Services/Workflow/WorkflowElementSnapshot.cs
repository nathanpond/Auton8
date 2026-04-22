namespace AutoNate.Web.Services.Workflow;

public sealed record class WorkflowElementSnapshot(
    string Id,
    string Type,
    string? Name,
    string? ScriptFormat = null,
    string? Script = null,
    string? ResultVariable = null,
    string? ConditionExpression = null);
