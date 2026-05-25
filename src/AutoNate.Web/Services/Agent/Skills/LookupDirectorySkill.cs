using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Models;
using AutoNate.Web.Models.Authorization;
using AutoNate.Web.Services.Auth;
using AutoNate.Web.Services.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Read-only directory diagnostics: users, groups, role assignments.
//
// Two visibility tiers:
//  • Minimal (any authenticated user) — username + display name only, mirrors
//    the /api/users/directory endpoint used by Yjs and comment authors.
//  • Full (admin-gated on the entity kind's View action) — surfaces admin
//    fields like email, last login, group memberships.
public sealed class LookupDirectorySkill : IAgentSkill
{
    public string Name => "lookup-directory";

    public string Description =>
        "Look up users, groups, roles, and role assignments. Admin fields are gated by the corresponding entity-kind View permission.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public LookupDirectorySkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "find_user",
                Description: "Search users by username, first name, or last name. Returns up to 25 matches with display-only fields.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "query": { "type": "string", "description": "Optional free text matched against username and name fields." },
                        "take": { "type": "integer", "minimum": 1, "maximum": 50 }
                      },
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeFindUserAsync),

            new AgentTool(
                Name: "get_user",
                Description: "Fetch a single user by GUID. Returns admin fields (email, lock status, last login) only when the caller has User:view; otherwise display fields only.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "userId": { "type": "string", "description": "User GUID." }
                      },
                      "required": ["userId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeGetUserAsync),

            new AgentTool(
                Name: "list_groups",
                Description: "List groups the caller is permitted to view. Use includeArchived to include soft-deleted groups.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "includeArchived": { "type": "boolean", "description": "Defaults to false." },
                        "take": { "type": "integer", "minimum": 1, "maximum": 100 }
                      },
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListGroupsAsync),

            new AgentTool(
                Name: "get_group",
                Description: "Fetch a single group by GUID.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "groupId": { "type": "string", "description": "Group GUID." }
                      },
                      "required": ["groupId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeGetGroupAsync),

            new AgentTool(
                Name: "list_group_members",
                Description: "List user GUIDs that belong to a group. Requires Group:view.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "groupId": { "type": "string", "description": "Group GUID." }
                      },
                      "required": ["groupId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListGroupMembersAsync),

            new AgentTool(
                Name: "list_groups_for_user",
                Description: "List groups a user belongs to. Requires Group:view.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "userId": { "type": "string", "description": "User GUID." }
                      },
                      "required": ["userId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListGroupsForUserAsync),

            new AgentTool(
                Name: "list_roles",
                Description: "List roles the caller is permitted to view.",
                JsonSchema: ParseSchema("""
                    { "type": "object", "properties": {}, "additionalProperties": false }
                    """),
                Invoke: InvokeListRolesAsync),

            new AgentTool(
                Name: "list_role_assignments",
                Description: "List role assignments for a given role OR for a given principal. Requires Role:view.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "roleId": { "type": "string", "description": "Role GUID (mutually exclusive with principal*)." },
                        "principalKind": { "type": "string", "enum": ["user", "group"], "description": "Used with principalId." },
                        "principalId": { "type": "string", "description": "Principal GUID." }
                      },
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListRoleAssignmentsAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Directory tools return minimal display fields by default; admin fields (email, lock status, group/role membership) are gated by the corresponding entity-kind View permission. Use find_user before get_user when you only have a name.";

    private static async Task<bool> HasKindViewAsync(AgentToolContext context, string kind, CancellationToken ct)
    {
        var authorizer = context.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            context.Session.User, Actions.View, new EntityRef(kind, string.Empty), ct);
        return decision.IsAllowed;
    }

    private static async Task<JsonElement> InvokeFindUserAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var query = ReadString(args, "query");
        var take = ReadTake(args, 25, 50);
        var isAdmin = await HasKindViewAsync(context, EntityKinds.User, ct);

        var store = context.Services.GetRequiredService<ILocalUserStore>();
        var users = await store.ListAsync(ct);
        IEnumerable<LocalUser> filtered = users;
        if (!string.IsNullOrWhiteSpace(query))
        {
            var needle = query.Trim();
            filtered = filtered.Where(u =>
                u.Username.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                u.FirstName.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                u.LastName.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        var items = filtered.Take(take).Select(u => MapUser(u, isAdmin)).ToArray();
        return JsonSerializer.SerializeToElement(new
        {
            kind = "users",
            source = "ILocalUserStore",
            data = items
        });
    }

    private static async Task<JsonElement> InvokeGetUserAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var rawId = ReadString(args, "userId");
        if (!Guid.TryParse(rawId, out var userId))
        {
            return Error("get_user", "userId must be a GUID.");
        }
        var isAdmin = await HasKindViewAsync(context, EntityKinds.User, ct);
        var store = context.Services.GetRequiredService<ILocalUserStore>();
        var user = await store.GetByUserIdAsync(userId, ct);
        if (user is null) return Error("get_user", $"No user with id '{userId}'.");
        return JsonSerializer.SerializeToElement(new
        {
            kind = "user",
            source = "ILocalUserStore",
            data = MapUser(user, isAdmin)
        });
    }

    private static async Task<JsonElement> InvokeListGroupsAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var includeArchived = args.TryGetProperty("includeArchived", out var ia) && ia.ValueKind == JsonValueKind.True;
        var take = ReadTake(args, 50, 100);
        var store = context.Services.GetRequiredService<IGroupStore>();
        var groups = await store.ListAuthorizedAsync(context.Session.User, includeArchived, ct);
        var items = groups.Take(take).Select(MapGroup).ToArray();
        return JsonSerializer.SerializeToElement(new
        {
            kind = "groups",
            source = "IGroupStore",
            data = items
        });
    }

    private static async Task<JsonElement> InvokeGetGroupAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var rawId = ReadString(args, "groupId");
        if (!Guid.TryParse(rawId, out var groupId))
        {
            return Error("get_group", "groupId must be a GUID.");
        }
        if (!await HasKindViewAsync(context, EntityKinds.Group, ct))
        {
            return Error("get_group", "Requires Group:view permission.");
        }
        var store = context.Services.GetRequiredService<IGroupStore>();
        var group = await store.GetAsync(groupId, ct);
        if (group is null) return Error("get_group", $"No group with id '{groupId}'.");
        return JsonSerializer.SerializeToElement(new
        {
            kind = "group",
            source = "IGroupStore",
            data = MapGroup(group)
        });
    }

    private static async Task<JsonElement> InvokeListGroupMembersAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var rawId = ReadString(args, "groupId");
        if (!Guid.TryParse(rawId, out var groupId))
        {
            return Error("list_group_members", "groupId must be a GUID.");
        }
        if (!await HasKindViewAsync(context, EntityKinds.Group, ct))
        {
            return Error("list_group_members", "Requires Group:view permission.");
        }
        var store = context.Services.GetRequiredService<IGroupStore>();
        var members = await store.ListMembersAsync(groupId, ct);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "group_members",
            source = "IGroupStore",
            data = members.Select(m => new
            {
                groupId = m.GroupId,
                userId = m.UserId,
                addedAtUtc = m.AddedAtUtc
            }).ToArray()
        });
    }

    private static async Task<JsonElement> InvokeListGroupsForUserAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var rawId = ReadString(args, "userId");
        if (!Guid.TryParse(rawId, out var userId))
        {
            return Error("list_groups_for_user", "userId must be a GUID.");
        }
        if (!await HasKindViewAsync(context, EntityKinds.Group, ct))
        {
            return Error("list_groups_for_user", "Requires Group:view permission.");
        }
        var store = context.Services.GetRequiredService<IGroupStore>();
        var groups = await store.ListGroupsForUserAsync(userId, ct);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "groups_for_user",
            source = "IGroupStore",
            data = groups.Select(MapGroup).ToArray()
        });
    }

    private static async Task<JsonElement> InvokeListRolesAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var store = context.Services.GetRequiredService<IRoleStore>();
        var roles = await store.ListAuthorizedAsync(context.Session.User, ct);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "roles",
            source = "IRoleStore",
            data = roles.Select(MapRole).ToArray()
        });
    }

    private static async Task<JsonElement> InvokeListRoleAssignmentsAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        if (!await HasKindViewAsync(context, EntityKinds.Role, ct))
        {
            return Error("list_role_assignments", "Requires Role:view permission.");
        }
        var store = context.Services.GetRequiredService<IRoleAssignmentStore>();

        var roleIdRaw = ReadString(args, "roleId");
        if (!string.IsNullOrEmpty(roleIdRaw))
        {
            if (!Guid.TryParse(roleIdRaw, out var roleId))
            {
                return Error("list_role_assignments", "roleId must be a GUID.");
            }
            var rows = await store.ListByRoleAsync(roleId, ct);
            return JsonSerializer.SerializeToElement(new
            {
                kind = "role_assignments_by_role",
                source = "IRoleAssignmentStore",
                data = new { roleId, items = rows.Select(MapAssignment).ToArray() }
            });
        }

        var principalKind = ReadString(args, "principalKind");
        var principalId = ReadString(args, "principalId");
        if (string.IsNullOrWhiteSpace(principalKind) || string.IsNullOrWhiteSpace(principalId))
        {
            return Error("list_role_assignments", "Either roleId, or both principalKind and principalId, are required.");
        }
        var rows2 = await store.ListForPrincipalAsync(principalKind, principalId, ct);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "role_assignments_for_principal",
            source = "IRoleAssignmentStore",
            data = new { principalKind, principalId, items = rows2.Select(MapAssignment).ToArray() }
        });
    }

    private static object MapUser(LocalUser u, bool isAdmin) => isAdmin
        ? new
        {
            id = u.UserId,
            username = u.Username,
            firstName = u.FirstName,
            lastName = u.LastName,
            email = (string?)u.Email,
            lastLoginDate = u.LastLoginDate,
            failedLoginAttempts = (int?)u.FailedLoginAttempts,
            isLocked = (bool?)u.IsLocked,
            lockedAtUtc = u.LockedAtUtc,
            createdDate = u.CreatedDate
        }
        : new
        {
            id = u.UserId,
            username = u.Username,
            firstName = u.FirstName,
            lastName = u.LastName,
            email = (string?)null,
            lastLoginDate = (DateTimeOffset?)null,
            failedLoginAttempts = (int?)null,
            isLocked = (bool?)null,
            lockedAtUtc = (DateTimeOffset?)null,
            createdDate = u.CreatedDate
        };

    private static object MapGroup(Group g) => new
    {
        id = g.Id,
        name = g.Name,
        description = g.Description,
        isArchived = g.IsArchived,
        createdAtUtc = g.CreatedAtUtc,
        updatedAtUtc = g.UpdatedAtUtc
    };

    private static object MapRole(Role r) => new
    {
        id = r.Id,
        name = r.Name,
        description = r.Description,
        isSystem = r.IsSystem,
        createdAtUtc = r.CreatedAtUtc,
        updatedAtUtc = r.UpdatedAtUtc
    };

    private static object MapAssignment(RoleAssignment a) => new
    {
        id = a.Id,
        roleId = a.RoleId,
        principalKind = a.PrincipalKind,
        principalId = a.PrincipalId,
        scopeString = a.ScopeString,
        createdAtUtc = a.CreatedAtUtc
    };

    private static int ReadTake(JsonElement args, int defaultValue, int max) =>
        args.TryGetProperty("take", out var t) && t.ValueKind == JsonValueKind.Number
            ? Math.Clamp(t.GetInt32(), 1, max)
            : defaultValue;

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
