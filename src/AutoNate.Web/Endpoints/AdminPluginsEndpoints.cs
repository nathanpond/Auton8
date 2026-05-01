using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using AutoNate.Web.Plugins;
using AutoNate.Web.Services.ApplicationEvents;
using AutoNate.Web.Services.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoNate.Web.Endpoints;

public sealed record PluginSettingsDto(JsonElement? settings);

public sealed record PluginSettingsUpdateRequest(JsonElement settings);

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

        // Generic per-plugin settings KV. Plugins that need a settings page
        // ship a migration creating `plugin_settings_kv` (single-row JSONB
        // blob) inside their plg_<code> schema; these endpoints read/write
        // that row by opening a connection as the plugin's role. Lets every
        // plugin reuse the same JSX-page → host-API → own-schema flow without
        // each one needing bespoke endpoints.
        group.MapGet("/by-code/{code}/settings", async (
                string code,
                IDbContextFactory<AutoNateDbContext> dbFactory,
                PluginDataAccessRegistry dataRegistry,
                CancellationToken ct) =>
                await ReadPluginSettingsAsync(code, dbFactory, dataRegistry, ct))
            .RequireKindPermission(EntityKinds.Plugin, Actions.Manage);

        group.MapPut("/by-code/{code}/settings", async (
                string code,
                PluginSettingsUpdateRequest body,
                IDbContextFactory<AutoNateDbContext> dbFactory,
                PluginDataAccessRegistry dataRegistry,
                CancellationToken ct) =>
                await WritePluginSettingsAsync(code, body, dbFactory, dataRegistry, ct))
            .DisableAntiforgery()
            .RequireKindPermission(EntityKinds.Plugin, Actions.Manage);

        return app;
    }

    private static async Task<IResult> ReadPluginSettingsAsync(
        string code,
        IDbContextFactory<AutoNateDbContext> dbFactory,
        PluginDataAccessRegistry dataRegistry,
        CancellationToken ct)
    {
        if (!IsValidCode(code)) return Results.BadRequest(new { error = "Invalid plugin code." });

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var plugin = await db.Plugins.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == code, ct);
        if (plugin is null || plugin.RolePasswordEncrypted is null)
        {
            return Results.NotFound(new { error = "Plugin not found." });
        }

        var ds = dataRegistry.GetDataSource(code, plugin.RolePasswordEncrypted);
        await using var conn = ds.CreateConnection();
        await conn.OpenAsync(ct);

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT settings_json FROM plugin_settings_kv WHERE id = 1 LIMIT 1;";
            var raw = await cmd.ExecuteScalarAsync(ct);
            if (raw is null or DBNull)
            {
                return Results.Ok(new PluginSettingsDto(null));
            }
            using var parsed = JsonDocument.Parse(raw.ToString()!);
            return Results.Ok(new PluginSettingsDto(parsed.RootElement.Clone()));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // Plugin doesn't ship a settings table — surface a 404 so the SPA
            // can show a "no settings configured" empty state rather than 500.
            return Results.NotFound(new { error = "Plugin does not expose settings." });
        }
    }

    private static async Task<IResult> WritePluginSettingsAsync(
        string code,
        PluginSettingsUpdateRequest body,
        IDbContextFactory<AutoNateDbContext> dbFactory,
        PluginDataAccessRegistry dataRegistry,
        CancellationToken ct)
    {
        if (!IsValidCode(code)) return Results.BadRequest(new { error = "Invalid plugin code." });
        if (body.settings.ValueKind != JsonValueKind.Object)
        {
            return Results.BadRequest(new { error = "settings must be a JSON object." });
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var plugin = await db.Plugins.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == code, ct);
        if (plugin is null || plugin.RolePasswordEncrypted is null)
        {
            return Results.NotFound(new { error = "Plugin not found." });
        }

        var settingsJson = body.settings.GetRawText();
        var ds = dataRegistry.GetDataSource(code, plugin.RolePasswordEncrypted);
        await using var conn = ds.CreateConnection();
        await conn.OpenAsync(ct);

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO plugin_settings_kv (id, settings_json, updated_at)
                VALUES (1, @json::jsonb, NOW())
                ON CONFLICT (id) DO UPDATE
                SET settings_json = EXCLUDED.settings_json,
                    updated_at = NOW();
                """;
            cmd.Parameters.AddWithValue("@json", settingsJson);
            await cmd.ExecuteNonQueryAsync(ct);

            using var parsed = JsonDocument.Parse(settingsJson);
            return Results.Ok(new PluginSettingsDto(parsed.RootElement.Clone()));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return Results.NotFound(new { error = "Plugin does not expose settings." });
        }
    }

    private static bool IsValidCode(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length != 8) return false;
        if (!(code[0] >= 'a' && code[0] <= 'z')) return false;
        for (var i = 1; i < code.Length; i++)
        {
            var c = code[i];
            if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))) return false;
        }
        return true;
    }

    private static Guid ActorId(HttpContext http)
    {
        var raw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}
