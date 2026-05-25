using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Services.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Read-only permission diagnostics. Mirrors the gating used by the SPA's
// admin/grants and admin/explain pages — every tool checks SiteConfig:View
// on the caller before returning data so a non-admin can't enumerate the
// grant graph through the chatbot. Pairs with Phase 3's ManagePermissionsSkill
// for grant/revoke operations.
public sealed class LookupPermissionsSkill : IAgentSkill
{
    public string Name => "lookup-permissions";

    public string Description =>
        "Inspect permission grants, the entity-kind catalog, and effective-permissions explanations.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public LookupPermissionsSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "list_permission_grants",
                Description: "List permission grants. Supports principal-kind, effect, and free-text filters. Up to 100 rows.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "principalKind": { "type": "string", "description": "Filter by principal kind: user | group | role." },
                        "effect": { "type": "string", "description": "Filter by effect: allow | deny." },
                        "search": { "type": "string", "description": "Free text matched against action and selector." },
                        "take": { "type": "integer", "minimum": 1, "maximum": 100 }
                      },
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListGrantsAsync),

            new AgentTool(
                Name: "list_grants_for_principal",
                Description: "List every grant attached to a specific principal (user / group / role).",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "principalKind": { "type": "string", "enum": ["user", "group", "role"] },
                        "principalId": { "type": "string", "description": "Principal GUID." }
                      },
                      "required": ["principalKind", "principalId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListGrantsForPrincipalAsync),

            new AgentTool(
                Name: "describe_entity_kind",
                Description: "Describe one entity kind from the authorization registry: actions it supports and tags grant selectors can match against. Pass no arguments to list every kind.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "kind": { "type": ["string", "null"], "description": "Entity kind, e.g. record / page / workflow_execution. Omit to list all." }
                      },
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeDescribeKindAsync),

            new AgentTool(
                Name: "explain_authorization",
                Description: "Explain why a user is or is not allowed to perform an action on a target. Returns the full per-grant trace.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "userId": { "type": "string", "description": "User GUID to evaluate the decision as." },
                        "action": { "type": "string", "description": "Action verb, e.g. view / edit / delete." },
                        "targetKind": { "type": "string", "description": "Entity kind from the authorization registry." },
                        "targetId": { "type": ["string", "null"], "description": "Target id; omit for kind-level checks." }
                      },
                      "required": ["userId", "action", "targetKind"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeExplainAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Permissions tools require SiteConfig:view on the caller; non-admins will receive permission errors. Grant rows are (principal_kind, principal_id, action, selector, effect). Use describe_entity_kind to learn the allowed action verbs before calling explain_authorization.";

    private static async Task<bool> RequireAdminAsync(AgentToolContext context, CancellationToken ct)
    {
        var authorizer = context.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            context.Session.User, Actions.View, new EntityRef(EntityKinds.SiteConfig, string.Empty), ct);
        return decision.IsAllowed;
    }

    private static async Task<JsonElement> InvokeListGrantsAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        if (!await RequireAdminAsync(context, ct))
        {
            return Error("list_permission_grants", "Requires SiteConfig:view permission.");
        }

        var principalKind = ReadString(args, "principalKind");
        var effect = ReadString(args, "effect");
        var search = ReadString(args, "search");
        var take = args.TryGetProperty("take", out var t) && t.ValueKind == JsonValueKind.Number
            ? Math.Clamp(t.GetInt32(), 1, 100)
            : 25;

        var store = context.Services.GetRequiredService<IPermissionGrantStore>();
        var page = await store.ListPagedAsync(new ListPermissionGrantsRequest(
            Page: 0,
            PageSize: take,
            Search: search,
            SortBy: null,
            SortDir: null,
            PrincipalKind: principalKind,
            Effect: effect), ct);

        return JsonSerializer.SerializeToElement(new
        {
            kind = "permission_grants",
            source = "IPermissionGrantStore",
            data = new
            {
                totalCount = page.TotalCount,
                items = page.Items.Select(MapGrant).ToArray()
            }
        });
    }

    private static async Task<JsonElement> InvokeListGrantsForPrincipalAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        if (!await RequireAdminAsync(context, ct))
        {
            return Error("list_grants_for_principal", "Requires SiteConfig:view permission.");
        }

        var principalKind = ReadString(args, "principalKind");
        var principalId = ReadString(args, "principalId");
        if (string.IsNullOrWhiteSpace(principalKind) || string.IsNullOrWhiteSpace(principalId))
        {
            return Error("list_grants_for_principal", "principalKind and principalId are required.");
        }

        var store = context.Services.GetRequiredService<IPermissionGrantStore>();
        var rows = await store.ListForPrincipalAsync(principalKind, principalId, ct);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "permission_grants_for_principal",
            source = "IPermissionGrantStore",
            data = new
            {
                principalKind,
                principalId,
                items = rows.Select(MapGrant).ToArray()
            }
        });
    }

    private static Task<JsonElement> InvokeDescribeKindAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var registry = context.Services.GetRequiredService<IEntityRegistry>();
        var kind = ReadString(args, "kind");
        if (string.IsNullOrWhiteSpace(kind))
        {
            var all = registry.All.Select(e => new
            {
                kind = e.Kind,
                actions = e.Actions.OrderBy(a => a, StringComparer.Ordinal).ToArray(),
                tags = e.Tags.OrderBy(t => t, StringComparer.Ordinal).ToArray()
            }).OrderBy(e => e.kind, StringComparer.Ordinal).ToArray();

            return Task.FromResult(JsonSerializer.SerializeToElement(new
            {
                kind = "entity_kinds",
                source = "IEntityRegistry",
                data = all
            }));
        }

        if (!registry.TryGet(kind, out var entity) || entity is null)
        {
            return Task.FromResult(Error("describe_entity_kind", $"Unknown entity kind '{kind}'. Call without arguments to list all kinds."));
        }

        return Task.FromResult(JsonSerializer.SerializeToElement(new
        {
            kind = "entity_kind",
            source = "IEntityRegistry",
            data = new
            {
                kind = entity.Kind,
                actions = entity.Actions.OrderBy(a => a, StringComparer.Ordinal).ToArray(),
                tags = entity.Tags.OrderBy(t => t, StringComparer.Ordinal).ToArray(),
                clrType = entity.ClrType.FullName,
                idClrType = entity.IdClrType.FullName
            }
        }));
    }

    private static async Task<JsonElement> InvokeExplainAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        if (!await RequireAdminAsync(context, ct))
        {
            return Error("explain_authorization", "Requires SiteConfig:view permission.");
        }

        var userIdRaw = ReadString(args, "userId");
        if (!Guid.TryParse(userIdRaw, out var userId))
        {
            return Error("explain_authorization", "userId must be a GUID.");
        }
        var action = ReadString(args, "action");
        if (string.IsNullOrWhiteSpace(action))
        {
            return Error("explain_authorization", "action is required.");
        }
        var targetKind = ReadString(args, "targetKind");
        if (string.IsNullOrWhiteSpace(targetKind))
        {
            return Error("explain_authorization", "targetKind is required.");
        }
        var targetId = ReadString(args, "targetId") ?? string.Empty;

        var authorizer = context.Services.GetRequiredService<IAuthorizer>();
        var explanation = await authorizer.ExplainAsync(
            userId, action, new EntityRef(targetKind, targetId), ct);

        return JsonSerializer.SerializeToElement(new
        {
            kind = "authorization_explanation",
            source = "IAuthorizer",
            data = new
            {
                effect = explanation.Effect.ToString().ToLowerInvariant(),
                reason = explanation.Reason,
                asUserId = explanation.AsUserId,
                isSuperAdmin = explanation.IsSuperAdmin,
                groupIds = explanation.GroupIds,
                roleIds = explanation.RoleIds,
                grants = explanation.Grants.Select(g => new
                {
                    principalKind = g.PrincipalKind,
                    principalId = g.PrincipalId,
                    principalName = g.PrincipalName,
                    action = g.Action,
                    selectorString = g.SelectorString,
                    effect = g.Effect.ToString().ToLowerInvariant(),
                    matched = g.Matched,
                    error = g.Error
                }).ToArray()
            }
        });
    }

    private static object MapGrant(Models.Authorization.PermissionGrant g) => new
    {
        id = g.Id,
        principalKind = g.PrincipalKind,
        principalId = g.PrincipalId,
        action = g.Action,
        selectorString = g.SelectorString,
        effect = g.Effect,
        priority = g.Priority,
        createdAtUtc = g.CreatedAtUtc,
        updatedAtUtc = g.UpdatedAtUtc
    };

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
