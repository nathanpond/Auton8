using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Services.Agent.Skills.Internal;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.SiteSettings;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Phase 5a — site settings (typed key→value admin config) and the read-only
// event catalog. Settings reads are gated on SiteConfig:view; writes on
// SiteConfig:edit (mirrors SiteSettingsEndpoints). Event catalog is open to
// every authenticated user via the endpoint and gated similarly here.
public sealed class SiteSettingsSkill : IAgentSkill
{
    public string Name => "site-settings";

    public string Description =>
        "List site settings and update individual settings; read the event-bus catalog.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public SiteSettingsSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "list_site_settings",
                Description: "Return every site-setting definition (key, type, group, label, default) and the current value. Requires SiteConfig:view.",
                JsonSchema: ParseSchema("""{ "type": "object", "properties": {}, "additionalProperties": false }"""),
                Invoke: InvokeListSettingsAsync),

            new AgentTool(
                Name: "get_site_setting",
                Description: "Fetch one site setting by key. Returns definition + current value.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": { "key": { "type": "string" } },
                      "required": ["key"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeGetSettingAsync),

            new AgentTool(
                Name: "set_site_setting",
                Description: "Update one site setting. Value must match the setting's declared type (bool / string / int). Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "key": { "type": "string", "description": "Setting key. Look up via list_site_settings." },
                        "value": { "description": "New value. Type must match the setting definition." },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["key", "value"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeSetSettingAsync),

            new AgentTool(
                Name: "get_event_catalog",
                Description: "Read the platform event catalog: transports (NATS / Dapr), payload fields, and per-category event-type lists. Open to any authenticated user.",
                JsonSchema: ParseSchema("""{ "type": "object", "properties": {}, "additionalProperties": false }"""),
                Invoke: InvokeGetEventCatalogAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Site settings are typed (SettingType.Bool / String / Int) — call list_site_settings or get_site_setting to learn a key's expected shape before set_site_setting. Read-only event catalog (get_event_catalog) describes the event-bus topics workflows can subscribe to.";

    private static async Task<bool> CanViewAsync(AgentToolContext ctx, CancellationToken ct)
    {
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.SiteConfig, string.Empty), ct);
        return decision.IsAllowed;
    }

    private static async Task<bool> CanEditAsync(AgentToolContext ctx, CancellationToken ct)
    {
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Edit, new EntityRef(EntityKinds.SiteConfig, string.Empty), ct);
        return decision.IsAllowed;
    }

    private static async Task<JsonElement> InvokeListSettingsAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        if (!await CanViewAsync(ctx, ct))
            return Error("list_site_settings", "SiteConfig:view required.");
        var store = ctx.Services.GetRequiredService<ISiteSettingsStore>();
        var values = await store.GetAllAsync(ct);
        var items = SiteSettingsRegistry.All.Select(d => new
        {
            key = d.Key,
            type = d.Type.ToString().ToLowerInvariant(),
            group = d.Group.ToString().ToLowerInvariant(),
            label = d.Label,
            description = d.Description,
            isPublic = d.IsPublic,
            defaultValue = d.DefaultValue,
            currentValue = values.TryGetValue(d.Key, out var v) ? v : d.DefaultValue
        }).ToArray();
        return JsonSerializer.SerializeToElement(new
        {
            kind = "site_settings",
            source = "SiteSettingsRegistry+ISiteSettingsStore",
            data = items
        });
    }

    private static async Task<JsonElement> InvokeGetSettingAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        var key = ReadString(args, "key");
        if (string.IsNullOrWhiteSpace(key)) return Error("get_site_setting", "key is required.");
        if (!await CanViewAsync(ctx, ct))
            return Error("get_site_setting", "SiteConfig:view required.");
        var def = SiteSettingsRegistry.Find(key);
        if (def is null) return Error("get_site_setting", $"Unknown setting '{key}'.");
        var store = ctx.Services.GetRequiredService<ISiteSettingsStore>();
        var values = await store.GetAllAsync(ct);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "site_setting",
            source = "SiteSettingsRegistry+ISiteSettingsStore",
            data = new
            {
                key = def.Key,
                type = def.Type.ToString().ToLowerInvariant(),
                group = def.Group.ToString().ToLowerInvariant(),
                label = def.Label,
                description = def.Description,
                isPublic = def.IsPublic,
                defaultValue = def.DefaultValue,
                currentValue = values.TryGetValue(key, out var v) ? v : def.DefaultValue
            }
        });
    }

    private static async Task<JsonElement> InvokeSetSettingAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "set_site_setting";
        var key = ReadString(args, "key");
        if (string.IsNullOrWhiteSpace(key)) return ConfirmGate.Rejected(action, "key is required.");
        if (!args.TryGetProperty("value", out var value))
            return ConfirmGate.Rejected(action, "value is required.");
        var def = SiteSettingsRegistry.Find(key);
        if (def is null) return ConfirmGate.Rejected(action, $"Unknown setting '{key}'.");
        if (!await CanEditAsync(ctx, ct))
            return ConfirmGate.Rejected(action, "SiteConfig:edit required.");

        JsonElement validated;
        try
        {
            validated = SiteSettingsRegistry.ValidateValue(def, value);
        }
        catch (SiteSettingsValidationException ex)
        {
            return ConfirmGate.Rejected(action, ex.Message);
        }

        var preview = new { key, type = def.Type.ToString().ToLowerInvariant(), value = validated };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("site_setting_set_proposal", action, preview);

        var store = ctx.Services.GetRequiredService<ISiteSettingsStore>();
        var audit = ctx.Services.GetRequiredService<IAuditEventPublisher>();
        await store.ApplyUpdatesAsync(
            new Dictionary<string, JsonElement>(StringComparer.Ordinal) { [key] = validated },
            ctx.Session.UserId, ct);
        await audit.PublishAsync(
            SiteEventTopic.TopicName, SiteEventTypes.SettingsUpdated,
            SiteResourceKinds.Settings,
            resource: null,
            details: new { source = "chatbot", keys = new[] { key }, count = 1 }, ct);
        return ConfirmGate.Committed("site_setting_set_committed", action, new { key, value = validated });
    }

    private static Task<JsonElement> InvokeGetEventCatalogAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        return Task.FromResult(JsonSerializer.SerializeToElement(new
        {
            kind = "event_catalog",
            source = "EventCatalog",
            data = new
            {
                transports = EventCatalog.Transports,
                payloadFields = EventCatalog.PayloadFields,
                categories = EventCatalog.Categories
            }
        }));
    }

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static JsonElement Error(string source, string message) =>
        JsonSerializer.SerializeToElement(new { kind = "error", source, data = new { message } });

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
