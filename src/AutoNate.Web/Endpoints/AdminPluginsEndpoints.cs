using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Plugins;
using AutoNate.Web.Services.ApplicationEvents;
using AutoNate.Web.Services.Events;
using Microsoft.AspNetCore.Http;

namespace AutoNate.Web.Endpoints;

public static class AdminPluginsEndpoints
{
    public static IEndpointRouteBuilder MapAdminPluginsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/plugins").RequireAuthorization();

        group.MapGet("/", async (
                IPluginManagementService svc,
                IAuditEventPublisher auditPublisher,
                CancellationToken ct) =>
            {
                var plugins = await svc.ListAsync(ct);
                await auditPublisher.PublishAsync(
                    DaprApplicationEventPublisher.TopicName,
                    ApplicationEventTypes.PluginListViewed,
                    ApplicationResourceKinds.Plugin,
                    resource: null,
                    details: new { resultCount = plugins.Count },
                    ct);
                return Results.Ok(plugins);
            })
            .RequireKindPermission(EntityKinds.Plugin, Actions.Manage);

        group.MapGet("/{id:guid}", async (
                Guid id,
                IPluginManagementService svc,
                IAuditEventPublisher auditPublisher,
                CancellationToken ct) =>
            {
                var p = await svc.GetAsync(id, ct);
                if (p is null) return Results.NotFound();
                await auditPublisher.PublishAsync(
                    DaprApplicationEventPublisher.TopicName,
                    ApplicationEventTypes.PluginViewed,
                    ApplicationResourceKinds.Plugin,
                    resource: new { id = p.Id, name = p.Name, version = p.Version },
                    details: null,
                    ct);
                return Results.Ok(p);
            })
            .RequireKindPermission(EntityKinds.Plugin, Actions.Manage);

        group.MapPost("/", async (
                HttpContext http,
                IFormFile file,
                IPluginManagementService svc,
                CancellationToken ct) =>
            {
                if (file is null || file.Length == 0)
                {
                    return Results.BadRequest(new { error = "file is required" });
                }
                await using var stream = file.OpenReadStream();
                var outcome = await svc.UploadAsync(stream, ActorId(http), ct);
                if (!outcome.Success)
                {
                    return outcome.ErrorCode switch
                    {
                        "uncompressed_too_large" => Results.StatusCode(StatusCodes.Status413PayloadTooLarge),
                        _ => Results.BadRequest(new { error = outcome.ErrorMessage, code = outcome.ErrorCode }),
                    };
                }
                return Results.Created($"/api/admin/plugins/{outcome.Plugin!.Id}", outcome.Plugin);
            })
            .DisableAntiforgery()
            .RequireKindPermission(EntityKinds.Plugin, Actions.Manage);

        group.MapPost("/{id:guid}/enable", async (
                Guid id,
                HttpContext http,
                IPluginManagementService svc,
                CancellationToken ct) =>
            {
                var outcome = await svc.EnableAsync(id, ActorId(http), ct);
                if (outcome.ErrorCode == "not_found") return Results.NotFound();
                if (!outcome.Success)
                {
                    return Results.BadRequest(new { error = outcome.ErrorMessage, code = outcome.ErrorCode, plugin = outcome.Plugin });
                }
                return Results.Ok(outcome.Plugin);
            })
            .DisableAntiforgery()
            .RequireKindPermission(EntityKinds.Plugin, Actions.Manage);

        group.MapPost("/{id:guid}/disable", async (
                Guid id,
                HttpContext http,
                IPluginManagementService svc,
                CancellationToken ct) =>
            {
                var outcome = await svc.DisableAsync(id, ActorId(http), ct);
                if (outcome.ErrorCode == "not_found") return Results.NotFound();
                return Results.Ok(outcome.Plugin);
            })
            .DisableAntiforgery()
            .RequireKindPermission(EntityKinds.Plugin, Actions.Manage);

        group.MapDelete("/{id:guid}", async (
                Guid id,
                HttpContext http,
                IPluginManagementService svc,
                CancellationToken ct) =>
            {
                var outcome = await svc.DeleteAsync(id, ActorId(http), ct);
                if (outcome.ErrorCode == "not_found") return Results.NotFound();
                return Results.NoContent();
            })
            .DisableAntiforgery()
            .RequireKindPermission(EntityKinds.Plugin, Actions.Manage);

        return app;
    }

    private static Guid ActorId(HttpContext http)
    {
        var raw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}
