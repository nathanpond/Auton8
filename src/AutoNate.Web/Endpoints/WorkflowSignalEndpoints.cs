using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Workflow;

namespace AutoNate.Web.Endpoints;

// #523. The other way in from outside, beside /api/workflow-messages. A message
// advances ONE process the caller names; a signal is broadcast BY NAME to
// everything listening, which is the whole difference and the reason this has no
// process key.
//
// `BroadcastSignalAsync` has existed since the bus path was built and had zero
// production callers -- every reference was a test. This exposes it.
public static class WorkflowSignalEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowSignalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workflow-signals").RequireAuthorization();

        group.MapPost("/", async (
            BroadcastWorkflowSignalRequest request,
            WorkflowSignalBroadcaster broadcaster,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var signalName = request.SignalName?.Trim();
            if (string.IsNullOrWhiteSpace(signalName))
            {
                return Results.BadRequest(new { error = "signalName is required." });
            }

            var result = await broadcaster.BroadcastAsync(signalName, request.Variables, ct);

            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                result.Outcome is WorkflowSignalBroadcaster.Outcome.Broadcast
                    ? WorkflowAdminEventTypes.SignalBroadcast
                    : WorkflowAdminEventTypes.SignalRefused,
                WorkflowResourceKinds.Signal,
                resource: new { signalName = result.SignalName },
                details: result.Outcome is WorkflowSignalBroadcaster.Outcome.Broadcast
                    ? new { declaring = result.Declaring }
                    : (object)new { available = result.Available ?? Array.Empty<string>() },
                ct);

            return result.Outcome switch
            {
                // `declaring` is who was LISTENING when the broadcast went out,
                // not who woke. A signal is addressed to everything waiting on the
                // name and the engine decides what that turns out to be, so
                // reporting a wake count would be inventing a number.
                WorkflowSignalBroadcaster.Outcome.Broadcast => Results.Ok(new
                {
                    outcome = "broadcast",
                    signalName = result.SignalName,
                    declaring = result.Declaring
                }),

                // 404 and said out loud, the same rule the message endpoint's
                // no-match follows. Flowable accepts a broadcast for a name
                // nothing subscribes to and answers 204; passing that through
                // would report success for a signal that reached nobody, which is
                // indistinguishable from the feature being broken.
                WorkflowSignalBroadcaster.Outcome.UnknownSignal => Results.NotFound(new
                {
                    outcome = "unknown-signal",
                    error = $"No published workflow catches a signal named '{result.SignalName}'.",
                    signalName = result.SignalName,
                    available = result.Available
                }),

                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        }).DisableAntiforgery()
          // Kind-level, and there is even less to gate on than for a message: a
          // signal carries no process key at all, so there is no route id and no
          // resource to resolve before the broadcast happens.
          .RequireKindPermission(EntityKinds.WorkflowSignal, Actions.Send);

        return app;
    }
}

public sealed record class BroadcastWorkflowSignalRequest(
    string? SignalName,
    IReadOnlyDictionary<string, object?>? Variables = null);
