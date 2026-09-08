using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Workflow.Behaviors;

namespace AutoNate.Web.Endpoints;

// Catalog DTO for the studio picker. Mirrors IWorkflowBehavior's public
// surface; deliberately minimal so adding new behavior metadata later
// doesn't ripple through clients.
public sealed record WorkflowBehaviorCatalogEntry(string Key, string DisplayName, string? Description);

public static class WorkflowBehaviorEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowBehaviorEndpoints(this IEndpointRouteBuilder app)
    {
        // /execute is anonymous + secret-gated: the call originates from the
        // Flowable JVM, not a browser, so there is no cookie auth to ride on.
        var executeGroup = app.MapGroup("/api/workflow-behaviors")
            .AllowAnonymous();

        executeGroup.MapPost("/{key}/execute", async (
            string key,
            BehaviorContext context,
            IWorkflowBehaviorRegistry registry,
            ILogger<WorkflowBehaviorRegistry> log,
            CancellationToken cancellationToken) =>
        {
            var behavior = registry.Get(key);
            if (behavior is null)
            {
                log.LogWarning(
                    "Workflow behavior callback for unknown key '{Key}' (process {ProcessInstanceId}, correlationId {CorrelationId}).",
                    key, context.ProcessInstanceId, context.CorrelationId);
                return Results.NotFound(new { error = "unknown_behavior", key });
            }

            log.LogInformation(
                "Executing workflow behavior '{Key}' for process {ProcessInstanceId} (correlationId {CorrelationId}).",
                key, context.ProcessInstanceId, context.CorrelationId);

            // Predictable failures come back as `Failed: true` on the result;
            // unhandled exceptions propagate to surface as a 500 so the
            // Flowable bridge can throw and the engine can retry the activity.
            var result = await behavior.ExecuteAsync(context, cancellationToken);

            return Results.Ok(EnforceDeclaredBusinessError(result, behavior, key, log));
        })
        .DisableAntiforgery()
        .AddEndpointFilter<SharedSecretEndpointFilter>();

        // /catalog (GET) is for the workflow studio: authors enumerating
        // available behaviors when wiring them into a BPMN node. The studio
        // is an authoring surface, so we gate on WorkflowModel:Edit to match
        // the create/upsert endpoints — view-only callers don't need the
        // behavior list.
        var catalogGroup = app.MapGroup("/api/workflow-behaviors")
            .RequireAuthorization();

        catalogGroup.MapGet("/", (IWorkflowBehaviorRegistry registry) =>
        {
            var entries = registry.GetAll()
                .Select(b => new WorkflowBehaviorCatalogEntry(b.Key, b.DisplayName, b.Description))
                .OrderBy(entry => entry.DisplayName, StringComparer.Ordinal)
                .ToArray();
            return Results.Ok(entries);
        }).RequireKindPermission(EntityKinds.WorkflowModel, Actions.Edit);

        return app;
    }

    /// <summary>
    /// A business error is catchable only if the behaviour declared it (#114).
    /// </summary>
    /// <remarks>
    /// Enforced here rather than trusted from the result, so a behaviour cannot
    /// make an arbitrary failure routable — including one that reached
    /// BusinessErrorCode by accident. Undeclared codes are stripped, which leaves
    /// the failure exactly what it was: unhandled, surfaced, and retryable.
    ///
    /// That asymmetry is the point. If every failure became a catchable BPMN
    /// error, "the database was briefly unreachable" would travel down the
    /// "payment declined" branch, and the process would look like it handled
    /// something it never understood.
    /// </remarks>
    internal static BehaviorResult EnforceDeclaredBusinessError(
        BehaviorResult result, IWorkflowBehavior behavior, string key, ILogger log)
    {
        if (result.BusinessErrorCode is not { Length: > 0 } code) return result;
        if (behavior.CatchableErrorCodes.Contains(code, StringComparer.Ordinal)) return result;

        log.LogWarning(
            "Behavior '{Key}' returned business error '{Code}' without declaring it in "
                + "CatchableErrorCodes; treating it as an ordinary failure so it stays retryable "
                + "rather than being routed to an error boundary.",
            key, code);

        return result with { BusinessErrorCode = null };
    }
}
