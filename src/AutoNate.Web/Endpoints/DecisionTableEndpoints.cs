using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Decisions;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Flowable;
using AutoNate.Web.Services.Workflow;

namespace AutoNate.Web.Endpoints;

/// <summary>
/// Authoring, publishing and trying a decision table (#110).
/// </summary>
public static class DecisionTableEndpoints
{
    public static IEndpointRouteBuilder MapDecisionTableEndpoints(this IEndpointRouteBuilder app)
    {
        var tables = app.MapGroup("/api/decision-tables").RequireAuthorization();

        tables.MapGet("/", async (
            IDecisionTableStore store,
            IAuditEventPublisher audit,
            CancellationToken cancellationToken) =>
        {
            var found = await store.ListAsync(cancellationToken);
            await audit.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.DecisionTableListViewed,
                WorkflowResourceKinds.DecisionTable,
                resource: null,
                details: new { resultCount = found.Count },
                cancellationToken);
            return Results.Ok(found);
        }).RequireKindPermission(EntityKinds.DecisionTable, Actions.View);

        tables.MapGet("/{id:guid}", async (
            Guid id,
            IDecisionTableStore store,
            IAuditEventPublisher audit,
            CancellationToken cancellationToken) =>
        {
            var table = await store.GetAsync(id, cancellationToken);
            if (table is null) return Results.NotFound();

            await audit.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.DecisionTableViewed,
                WorkflowResourceKinds.DecisionTable,
                resource: new { id, decisionKey = table.DecisionKey },
                details: null,
                cancellationToken);
            return Results.Ok(table);
        }).RequirePermission(EntityKinds.DecisionTable, Actions.View, "id");

        tables.MapGet("/{id:guid}/versions", async (
            Guid id,
            IDecisionTableStore store,
            CancellationToken cancellationToken) =>
            Results.Ok(await store.ListVersionsAsync(id, cancellationToken)))
            .RequirePermission(EntityKinds.DecisionTable, Actions.View, "id");

        tables.MapPost("/", async (
            DecisionTableModel table,
            IDecisionTableStore store,
            IAuditEventPublisher audit,
            CancellationToken cancellationToken) =>
            await SaveAsync(table with { Id = Guid.NewGuid() }, store, audit, cancellationToken))
            .DisableAntiforgery()
            .RequireKindPermission(EntityKinds.DecisionTable, Actions.Create);

        tables.MapPut("/{id:guid}", async (
            Guid id,
            DecisionTableModel table,
            IDecisionTableStore store,
            IAuditEventPublisher audit,
            CancellationToken cancellationToken) =>
            await SaveAsync(table with { Id = id }, store, audit, cancellationToken))
            .DisableAntiforgery()
            .RequirePermission(EntityKinds.DecisionTable, Actions.Edit, "id");

        tables.MapDelete("/{id:guid}", async (
            Guid id,
            IDecisionTableStore store,
            IAuditEventPublisher audit,
            CancellationToken cancellationToken) =>
        {
            var table = await store.GetAsync(id, cancellationToken);
            if (table is null) return Results.NotFound();

            await store.DeleteAsync(id, cancellationToken);
            await audit.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.DecisionTableDeleted,
                WorkflowResourceKinds.DecisionTable,
                resource: new { id, decisionKey = table.DecisionKey, name = table.Name },
                details: null,
                cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.DecisionTable, Actions.Delete, "id");

        tables.MapPost("/{id:guid}/publish", async (
            Guid id,
            IDecisionTableStore store,
            IFlowableDecisionClient engine,
            IAuditEventPublisher audit,
            CancellationToken cancellationToken) =>
        {
            var table = await store.GetAsync(id, cancellationToken);
            if (table is null) return Results.NotFound();

            // Validated again before deploying. The store already refuses a bad
            // save, so this can only fire for a table stored before a rule
            // tightened -- exactly the case where deploying anyway would put a
            // version in the engine that the editor would then refuse to save.
            var errors = DecisionTableValidator.Validate(table);
            if (errors.Count > 0)
            {
                return Results.BadRequest(new { errors });
            }

            var dmn = DecisionTableDmn.Generate(table);

            DecisionDeploymentInfo deployment;
            try
            {
                deployment = await engine.DeployDecisionAsync(table.DecisionKey, dmn, cancellationToken);
            }
            catch (FlowableRequestException exception)
            {
                // THE DRAFT STAYS UNPUBLISHED. A version recorded here for a
                // deployment that did not happen would exist in Auton8 and not in
                // the engine -- and every later read would report a published
                // table that cannot be evaluated.
                return Results.Json(
                    new { errors = new[] { $"The engine refused the decision table. {exception.Message}" } },
                    statusCode: exception.IsCallerError ? StatusCodes.Status400BadRequest : StatusCodes.Status502BadGateway);
            }

            var published = await store.RecordPublishAsync(id, dmn, deployment, cancellationToken);

            await audit.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.DecisionTablePublished,
                WorkflowResourceKinds.DecisionTable,
                resource: new { id, decisionKey = table.DecisionKey, name = table.Name },
                details: new
                {
                    versionNumber = published.PublishedVersionNumber,
                    decisionId = deployment.DecisionId,
                    decisionVersion = deployment.DecisionVersion
                },
                cancellationToken);

            return Results.Ok(published);
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.DecisionTable, Actions.Publish, "id");

        // The try-it panel. THE SAME EVALUATION PATH A PROCESS USES -- the story's
        // own key link, and the reason it is not a separate "preview" evaluator: a
        // table that tests correctly could then behave differently in a process,
        // which is the exact class of thing a try-it panel exists to rule out.
        tables.MapPost("/{id:guid}/test", async (
            Guid id,
            TestDecisionTableRequest request,
            IDecisionTableStore store,
            IFlowableDecisionClient engine,
            IAuditEventPublisher audit,
            CancellationToken cancellationToken) =>
        {
            var table = await store.GetAsync(id, cancellationToken);
            if (table is null) return Results.NotFound();

            if (table.PublishedVersionNumber is null)
            {
                return Results.BadRequest(new
                {
                    errors = new[]
                    {
                        "This table has never been published, so there is nothing deployed to evaluate. "
                        + "Publish it first — the try-it panel runs the same path a process runs, which "
                        + "means it runs against the engine rather than against the draft."
                    }
                });
            }

            var inputs = request?.Inputs ?? new Dictionary<string, object?>();

            DecisionEvaluationResult result;
            try
            {
                result = await engine.EvaluateAsync(table.DecisionKey, inputs, cancellationToken);
            }
            catch (FlowableRequestException exception)
            {
                return Results.Json(
                    new { errors = new[] { exception.Message } },
                    statusCode: exception.IsCallerError
                        ? StatusCodes.Status400BadRequest
                        : StatusCodes.Status502BadGateway);
            }

            await audit.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.DecisionTableTested,
                WorkflowResourceKinds.DecisionTable,
                resource: new { id, decisionKey = table.DecisionKey },
                details: new
                {
                    inputCount = inputs.Count,
                    matched = result.Matched,
                    matchedRuleCount = result.Outputs.Count
                },
                cancellationToken);

            return Results.Ok(new TestDecisionTableResponse(result.Matched, result.Outputs));
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.DecisionTable, Actions.View, "id");

        return app;
    }

    private static async Task<IResult> SaveAsync(
        DecisionTableModel table,
        IDecisionTableStore store,
        IAuditEventPublisher audit,
        CancellationToken cancellationToken)
    {
        try
        {
            var saved = await store.SaveAsync(table, cancellationToken);
            await audit.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.DecisionTableSaved,
                WorkflowResourceKinds.DecisionTable,
                resource: new { id = saved.Id, decisionKey = saved.DecisionKey, name = saved.Name },
                details: new
                {
                    ruleCount = saved.Rules.Count,
                    inputCount = saved.Inputs.Count,
                    outputCount = saved.Outputs.Count
                },
                cancellationToken);
            return Results.Ok(saved);
        }
        catch (DecisionTableInvalidException invalid)
        {
            // EVERY error, not the first. An author fixing a forty-rule table one
            // message at a time is the experience this story exists to avoid.
            return Results.BadRequest(new { errors = invalid.Errors });
        }
    }

    public sealed record TestDecisionTableRequest(Dictionary<string, object?>? Inputs);

    public sealed record TestDecisionTableResponse(
        bool Matched,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Outputs);
}
