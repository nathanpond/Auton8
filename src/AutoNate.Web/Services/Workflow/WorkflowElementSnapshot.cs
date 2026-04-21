namespace AutoNate.Web.Services.Workflow;

public sealed record class WorkflowElementSnapshot(
    string Id,
    string Type,
    string? Name);
