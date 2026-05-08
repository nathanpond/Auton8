using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.ExternalConnections;

namespace AutoNate.Web.Endpoints;

public static class ExternalConnectionEndpoints
{
    public static IEndpointRouteBuilder MapExternalConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/external-connections").RequireAuthorization();

        group.MapGet("/", async (
            string? kind,
            IExternalConnectionStore store,
            CancellationToken ct) =>
        {
            var rows = await store.ListAsync(kind, ct);
            return Results.Ok(rows);
        }).RequireKindPermission(EntityKinds.ExternalConnection, Actions.View);

        group.MapGet("/{id:guid}", async (
            Guid id,
            IExternalConnectionStore store,
            CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            return row is null ? Results.NotFound() : Results.Ok(row);
        }).RequirePermission(EntityKinds.ExternalConnection, Actions.View);

        group.MapPost("/", async (
            CreateExternalConnectionRequest request,
            HttpContext http,
            IExternalConnectionStore store,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = GetUserId(http);
            if (actorId == Guid.Empty) return Results.Unauthorized();

            try
            {
                var row = await store.CreateAsync(
                    new CreateExternalConnectionInput(
                        Kind: request.Kind,
                        Name: request.Name,
                        Description: request.Description,
                        IsEnabled: request.IsEnabled ?? true,
                        Metadata: request.Metadata ?? EmptyObject(),
                        Secret: request.Secret),
                    actorId,
                    ct);
                return Results.Created($"/api/external-connections/{row.Id}", row);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequireKindPermission(EntityKinds.ExternalConnection, Actions.Manage)
          .DisableAntiforgery();

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateExternalConnectionRequest request,
            HttpContext http,
            IExternalConnectionStore store,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = GetUserId(http);
            if (actorId == Guid.Empty) return Results.Unauthorized();

            try
            {
                var row = await store.UpdateAsync(
                    id,
                    new UpdateExternalConnectionInput(
                        Name: request.Name,
                        Description: request.Description,
                        IsEnabled: request.IsEnabled,
                        Metadata: request.Metadata,
                        Secret: request.Secret),
                    actorId,
                    ct);
                return row is null ? Results.NotFound() : Results.Ok(row);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequirePermission(EntityKinds.ExternalConnection, Actions.Manage)
          .DisableAntiforgery();

        group.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IExternalConnectionStore store,
            CancellationToken ct) =>
        {
            var actorId = GetUserId(http);
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var deleted = await store.DeleteAsync(id, actorId, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).RequirePermission(EntityKinds.ExternalConnection, Actions.Manage);

        group.MapPost("/{id:guid}/test", async (
            Guid id,
            ITestConnectionService tester,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var result = await tester.TestAsync(id, ct);
            await auditPublisher.PublishAsync(
                ExternalConnectionEventTopic.TopicName,
                ExternalConnectionEventTypes.Tested,
                ExternalConnectionEventTopic.ResourceKind,
                resource: new { id },
                details: new { ok = result.Ok, latencyMs = result.LatencyMs, modelEcho = result.ModelEcho, error = result.Error },
                ct);
            return Results.Ok(result);
        }).RequirePermission(EntityKinds.ExternalConnection, Actions.Manage)
          .DisableAntiforgery();

        group.MapPost("/{id:guid}/set-default", async (
            Guid id,
            HttpContext http,
            IExternalConnectionStore store,
            CancellationToken ct) =>
        {
            var actorId = GetUserId(http);
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var row = await store.SetDefaultAsync(id, actorId, ct);
            return row is null ? Results.NotFound() : Results.Ok(row);
        }).RequirePermission(EntityKinds.ExternalConnection, Actions.Manage)
          .DisableAntiforgery();

        return app;
    }

    private static Guid GetUserId(HttpContext context)
    {
        var raw = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    private static JsonElement EmptyObject()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }
}

public sealed record class CreateExternalConnectionRequest(
    string Kind,
    string Name,
    string? Description,
    bool? IsEnabled,
    JsonElement? Metadata,
    string? Secret);

public sealed record class UpdateExternalConnectionRequest(
    string? Name,
    string? Description,
    bool? IsEnabled,
    JsonElement? Metadata,
    string? Secret);
