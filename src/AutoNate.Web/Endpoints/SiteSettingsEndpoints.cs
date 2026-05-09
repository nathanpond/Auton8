using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.SiteSettings;

namespace AutoNate.Web.Endpoints;

public sealed record SettingDefinitionDto(
    string Key,
    string Type,
    string Group,
    string Label,
    string Description,
    JsonElement DefaultValue,
    bool IsPublic);

public sealed record AdminSiteSettingsDto(
    SettingDefinitionDto[] Definitions,
    Dictionary<string, JsonElement> Values);

public sealed record UpdateSiteSettingsRequest(Dictionary<string, JsonElement> Updates);

public static class SiteSettingsEndpoints
{
    public static IEndpointRouteBuilder MapSiteSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        // Public read: returns only IsPublic=true settings so non-admin code
        // (e.g. the SPA shell deciding whether to render the bell) can fetch
        // them without leaking admin-only flags.
        var publicGroup = app.MapGroup("/api/site-settings").AllowAnonymous();
        publicGroup.MapGet("/", async (ISiteSettingsStore store, CancellationToken ct) =>
        {
            var all = await store.GetAllAsync(ct);
            var publicValues = SiteSettingsRegistry.Public
                .ToDictionary(d => d.Key, d => all[d.Key]);
            return Results.Ok(publicValues);
        });

        var adminGroup = app.MapGroup("/api/admin/site-settings").RequireAuthorization();

        adminGroup.MapGet("/", async (
            ISiteSettingsStore store, IAuditEventPublisher auditPublisher, CancellationToken ct) =>
        {
            var values = await store.GetAllAsync(ct);
            var dto = new AdminSiteSettingsDto(
                SiteSettingsRegistry.All.Select(ToDto).ToArray(),
                values.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
            await auditPublisher.PublishAsync(
                SiteEventTopic.TopicName,
                SiteEventTypes.SettingsListViewed,
                SiteResourceKinds.Settings,
                resource: null,
                details: new { settingCount = values.Count },
                ct);
            return Results.Ok(dto);
        }).RequireKindPermission(EntityKinds.SiteConfig, Actions.View);

        adminGroup.MapPatch("/", async (
            UpdateSiteSettingsRequest request,
            HttpContext http,
            ISiteSettingsStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (request?.Updates is null || request.Updates.Count == 0)
            {
                return Results.BadRequest(new { error = "No updates supplied." });
            }

            var validated = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var (key, value) in request.Updates)
            {
                var definition = SiteSettingsRegistry.Find(key);
                if (definition is null)
                {
                    return Results.BadRequest(new { error = $"Unknown setting '{key}'." });
                }
                try
                {
                    validated[key] = SiteSettingsRegistry.ValidateValue(definition, value);
                }
                catch (SiteSettingsValidationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            }

            await store.ApplyUpdatesAsync(validated, http.GetActorId(), ct);
            await auditPublisher.PublishAsync(
                SiteEventTopic.TopicName,
                SiteEventTypes.SettingsUpdated,
                SiteResourceKinds.Settings,
                resource: null,
                details: new { keys = validated.Keys.ToArray(), count = validated.Count },
                ct);

            var values = await store.GetAllAsync(ct);
            return Results.Ok(new AdminSiteSettingsDto(
                SiteSettingsRegistry.All.Select(ToDto).ToArray(),
                values.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        return app;
    }

    private static SettingDefinitionDto ToDto(SettingDefinition definition) => new(
        definition.Key,
        TypeToWire(definition.Type),
        GroupToWire(definition.Group),
        definition.Label,
        definition.Description,
        definition.DefaultValue,
        definition.IsPublic);

    private static string TypeToWire(SettingType type) => type switch
    {
        SettingType.Bool => "bool",
        SettingType.String => "string",
        SettingType.Int => "int",
        _ => throw new InvalidOperationException($"Unknown setting type: {type}")
    };

    private static string GroupToWire(SettingGroup group) => group switch
    {
        SettingGroup.General => "general",
        SettingGroup.Features => "features",
        SettingGroup.Chatbot => "chatbot",
        _ => throw new InvalidOperationException($"Unknown setting group: {group}")
    };
}
