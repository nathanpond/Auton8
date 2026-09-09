using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Workflow;

namespace AutoNate.Web.Endpoints;

// #112. The one way in from outside: an external system saying "payment
// cleared". API only for v1.0 — the real caller is a machine, and an operator
// who needs to unstick a process uses the existing execution admin controls.
public static class WorkflowMessageEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowMessageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workflow-messages").RequireAuthorization();

        group.MapPost("/", async (
            SendWorkflowMessageRequest request,
            WorkflowMessageCorrelator correlator,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var processKey = request.ProcessKey?.Trim();
            if (string.IsNullOrWhiteSpace(processKey))
            {
                return Results.BadRequest(new { error = "processKey is required." });
            }

            var result = await correlator.CorrelateAsync(
                processKey,
                request.MessageName?.Trim(),
                request.CorrelationValue,
                request.Variables,
                ct);

            // Every outcome is audited, refusals included. A multi-match means a
            // process is modelled wrong, and nobody learns that from a 409 alone.
            var eventType = result.Outcome is WorkflowMessageCorrelator.Outcome.Delivered
                                           or WorkflowMessageCorrelator.Outcome.Started
                ? WorkflowAdminEventTypes.MessageDelivered
                : WorkflowAdminEventTypes.MessageRefused;

            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                eventType,
                WorkflowResourceKinds.Message,
                resource: new
                {
                    processKey,
                    messageName = result.MessageName,
                    processInstanceId = result.ProcessInstanceId
                },
                details: new
                {
                    outcome = result.Outcome.ToString(),
                    matchCount = result.MatchCount,
                    // The correlation VALUE is deliberately absent: it is a
                    // business identifier supplied by a caller and can carry
                    // personal data. Which key was used is on the diagram already.
                    correlated = request.CorrelationValue is not null
                },
                ct);

            return result.Outcome switch
            {
                WorkflowMessageCorrelator.Outcome.Delivered => Results.Ok(new
                {
                    outcome = "delivered",
                    processInstanceId = result.ProcessInstanceId,
                    messageName = result.MessageName
                }),

                WorkflowMessageCorrelator.Outcome.Started => Results.Ok(new
                {
                    outcome = "started",
                    processInstanceId = result.ProcessInstanceId,
                    messageName = result.MessageName
                }),

                WorkflowMessageCorrelator.Outcome.UnknownProcess => Results.NotFound(new
                {
                    error = $"No published workflow with process key '{processKey}'."
                }),

                WorkflowMessageCorrelator.Outcome.UnknownMessage => Results.NotFound(new
                {
                    error = "That workflow declares no such message or receive task.",
                    available = result.AvailableMessages
                }),

                // 409 rather than 400: the request is well-formed, the definition
                // is ambiguous.
                WorkflowMessageCorrelator.Outcome.AmbiguousMessage => Results.Conflict(new
                {
                    error = "This workflow has more than one way in; name the one you mean.",
                    available = result.AvailableMessages
                }),

                // 404 and said out loud. A no-match that returned 200 would be
                // indistinguishable from the feature being broken.
                WorkflowMessageCorrelator.Outcome.NoMatch => Results.NotFound(new
                {
                    outcome = "no-match",
                    error = "No waiting process instance matched that correlation value.",
                    messageName = result.MessageName
                }),

                // Refused, with the count, and nothing advanced.
                WorkflowMessageCorrelator.Outcome.MultipleMatches => Results.Conflict(new
                {
                    outcome = "multiple-matches",
                    error =
                        $"{result.MatchCount} waiting process instances matched that correlation " +
                        "value. A correlation key must be unique among waiting instances, so " +
                        "nothing was advanced.",
                    matchCount = result.MatchCount,
                    messageName = result.MessageName
                }),

                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        }).DisableAntiforgery()
          // Kind-level: a message is addressed by process key and correlation
          // value, never by instance id, so there is no route id to gate on. The
          // instance-level overload would resolve an empty id and 403 everyone,
          // super-admins included.
          .RequireKindPermission(EntityKinds.WorkflowMessage, Actions.Send);

        return app;
    }
}

public sealed record class SendWorkflowMessageRequest(
    string? ProcessKey,
    // Optional: inferred when the workflow has exactly one way in. Required when
    // it has several, because picking one would deliver to whichever the parser
    // saw first.
    string? MessageName,
    string? CorrelationValue,
    IReadOnlyDictionary<string, object?>? Variables);
