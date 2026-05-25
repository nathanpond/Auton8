using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Services.Agent.Skills.Internal;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Events;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Phase 3 IAM write skill. Mirrors the endpoint-side gating:
//   - grant_permission / revoke_permission: SiteConfig:Edit (kind-level)
//   - add_user_to_group / remove_user_from_group: per-group Group:AddMember /
//     Group:RemoveMember (per-instance)
//   - assign_role / unassign_role: Role:Assign (kind-level)
//
// These tools are high-impact — the agent should always narrate the proposal
// and require explicit user approval before re-calling with confirmed=true.
public sealed class ManagePermissionsSkill : IAgentSkill
{
    public string Name => "manage-permissions";

    public string Description =>
        "Grant and revoke permission grants, manage group membership, assign and unassign roles. All actions confirm-gated and audit-logged.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public ManagePermissionsSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "grant_permission",
                Description: "Create a permission grant (principal × action × selector → allow/deny). Requires SiteConfig:edit on the caller.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "principalKind": { "type": "string", "enum": ["user", "group", "role"] },
                        "principalId": { "type": "string", "description": "Principal GUID." },
                        "action": { "type": "string", "description": "Action verb, e.g. view / edit / delete / manage." },
                        "selectorString": { "type": "string", "description": "Authorization selector targeting the resources this grant covers." },
                        "effect": { "type": "string", "enum": ["allow", "deny"] },
                        "priority": { "type": "integer", "description": "Higher priority wins on conflicts. Defaults to 100." },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["principalKind", "principalId", "action", "selectorString", "effect"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeGrantAsync),

            new AgentTool(
                Name: "revoke_permission",
                Description: "Delete a permission grant by id. Requires SiteConfig:edit.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "id": { "type": "string", "description": "Grant GUID." },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeRevokeAsync),

            new AgentTool(
                Name: "add_user_to_group",
                Description: "Add a user to a group. Requires Group:addmember on the target group.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "groupId": { "type": "string" },
                        "userId": { "type": "string" },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["groupId", "userId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeAddMemberAsync),

            new AgentTool(
                Name: "remove_user_from_group",
                Description: "Remove a user from a group. Requires Group:removemember on the target group.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "groupId": { "type": "string" },
                        "userId": { "type": "string" },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["groupId", "userId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeRemoveMemberAsync),

            new AgentTool(
                Name: "assign_role",
                Description: "Assign a role to a principal (user or group). Requires Role:assign on the caller.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "roleId": { "type": "string" },
                        "principalKind": { "type": "string", "enum": ["user", "group"] },
                        "principalId": { "type": "string" },
                        "scopeString": { "type": ["string", "null"], "description": "Optional selector scoping the assignment." },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["roleId", "principalKind", "principalId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeAssignRoleAsync),

            new AgentTool(
                Name: "unassign_role",
                Description: "Revoke a role assignment by id. Requires Role:assign on the caller.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "assignmentId": { "type": "string" },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["assignmentId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeUnassignRoleAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Permission writes are high-impact. Always: (1) look up the target principal / role / group via lookup-directory or lookup-permissions; (2) summarize the proposed change in plain language; (3) get the user's explicit yes before re-calling with confirmed=true.";

    // ---- helpers ---------------------------------------------------------

    private static async Task<bool> CanKindAsync(AgentToolContext ctx, string kind, string action, CancellationToken ct)
    {
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, action, new EntityRef(kind, string.Empty), ct);
        return decision.IsAllowed;
    }

    private static async Task<bool> CanInstanceAsync(AgentToolContext ctx, string kind, string action, string targetId, CancellationToken ct)
    {
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, action, new EntityRef(kind, targetId), ct);
        return decision.IsAllowed;
    }

    // ---- grant / revoke -------------------------------------------------

    private static async Task<JsonElement> InvokeGrantAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "grant_permission";
        var principalKind = ReadString(args, "principalKind");
        var principalId = ReadString(args, "principalId");
        var actionName = ReadString(args, "action");
        var selectorString = ReadString(args, "selectorString");
        var effect = ReadString(args, "effect");
        if (string.IsNullOrWhiteSpace(principalKind)) return ConfirmGate.Rejected(action, "principalKind is required.");
        if (string.IsNullOrWhiteSpace(principalId)) return ConfirmGate.Rejected(action, "principalId is required.");
        if (string.IsNullOrWhiteSpace(actionName)) return ConfirmGate.Rejected(action, "action is required.");
        if (string.IsNullOrWhiteSpace(selectorString)) return ConfirmGate.Rejected(action, "selectorString is required.");
        if (effect is not ("allow" or "deny")) return ConfirmGate.Rejected(action, "effect must be 'allow' or 'deny'.");
        int priority = args.TryGetProperty("priority", out var p) && p.ValueKind == JsonValueKind.Number
            ? p.GetInt32() : 100;

        if (!await CanKindAsync(ctx, EntityKinds.SiteConfig, Actions.Edit, ct))
        {
            return ConfirmGate.Rejected(action, "SiteConfig:edit permission required.");
        }

        var preview = new { principalKind, principalId, action = actionName, selectorString, effect, priority };
        if (!ConfirmGate.IsConfirmed(args))
        {
            return ConfirmGate.Proposal("grant_create_proposal", action, preview);
        }

        var store = ctx.Services.GetRequiredService<IPermissionGrantStore>();
        var audit = ctx.Services.GetRequiredService<IAuditEventPublisher>();
        try
        {
            var grant = await store.CreateAsync(
                new CreatePermissionGrantInput(principalKind, principalId, actionName, selectorString, effect, priority),
                ctx.Session.UserId, ct);
            await audit.PublishAsync(
                IamEventTopic.TopicName, IamEventTypes.PermissionGrantCreated,
                IamResourceKinds.PermissionGrant,
                resource: new { id = grant.Id, principalKind, principalId, action = actionName, effect },
                details: new { source = "chatbot", selectorString, priority }, ct);
            return ConfirmGate.Committed("grant_create_committed", action, new
            {
                id = grant.Id, principalKind, principalId, action = actionName, effect, priority
            });
        }
        catch (PermissionGrantValidationException ex)
        {
            return ConfirmGate.Failed("grant_create_failed", action, ex.Message);
        }
    }

    private static async Task<JsonElement> InvokeRevokeAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "revoke_permission";
        if (!TryReadGuid(args, "id", out var id))
        {
            return ConfirmGate.Rejected(action, "id is required and must be a GUID.");
        }
        if (!await CanKindAsync(ctx, EntityKinds.SiteConfig, Actions.Edit, ct))
        {
            return ConfirmGate.Rejected(action, "SiteConfig:edit permission required.");
        }
        if (!ConfirmGate.IsConfirmed(args))
        {
            return ConfirmGate.Proposal("grant_revoke_proposal", action, new { id });
        }
        var store = ctx.Services.GetRequiredService<IPermissionGrantStore>();
        var audit = ctx.Services.GetRequiredService<IAuditEventPublisher>();
        var ok = await store.DeleteAsync(id, ct);
        if (!ok)
        {
            return ConfirmGate.Failed("grant_revoke_failed", action, $"Grant {id} not found.");
        }
        await audit.PublishAsync(
            IamEventTopic.TopicName, IamEventTypes.PermissionGrantDeleted,
            IamResourceKinds.PermissionGrant,
            resource: new { id }, details: new { source = "chatbot" }, ct);
        return ConfirmGate.Committed("grant_revoke_committed", action, new { id });
    }

    // ---- group membership -----------------------------------------------

    private static async Task<JsonElement> InvokeAddMemberAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "add_user_to_group";
        if (!TryReadGuid(args, "groupId", out var groupId))
            return ConfirmGate.Rejected(action, "groupId is required and must be a GUID.");
        if (!TryReadGuid(args, "userId", out var userId))
            return ConfirmGate.Rejected(action, "userId is required and must be a GUID.");

        if (!await CanInstanceAsync(ctx, EntityKinds.Group, Actions.AddMember, groupId.ToString(), ct))
        {
            return ConfirmGate.Rejected(action, $"Group:addmember permission required on group {groupId}.");
        }
        if (!ConfirmGate.IsConfirmed(args))
        {
            return ConfirmGate.Proposal("group_add_member_proposal", action, new { groupId, userId });
        }
        var store = ctx.Services.GetRequiredService<IGroupStore>();
        var audit = ctx.Services.GetRequiredService<IAuditEventPublisher>();
        var added = await store.AddMemberAsync(groupId, userId, ctx.Session.UserId, ct);
        if (!added)
        {
            return ConfirmGate.Failed("group_add_member_failed", action, "User is already a member of this group.");
        }
        await audit.PublishAsync(
            IamEventTopic.TopicName, IamEventTypes.GroupMemberAdded,
            IamResourceKinds.GroupMember,
            resource: new { groupId, userId }, details: new { source = "chatbot" }, ct);
        return ConfirmGate.Committed("group_add_member_committed", action, new { groupId, userId });
    }

    private static async Task<JsonElement> InvokeRemoveMemberAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "remove_user_from_group";
        if (!TryReadGuid(args, "groupId", out var groupId))
            return ConfirmGate.Rejected(action, "groupId is required and must be a GUID.");
        if (!TryReadGuid(args, "userId", out var userId))
            return ConfirmGate.Rejected(action, "userId is required and must be a GUID.");

        if (!await CanInstanceAsync(ctx, EntityKinds.Group, Actions.RemoveMember, groupId.ToString(), ct))
        {
            return ConfirmGate.Rejected(action, $"Group:removemember permission required on group {groupId}.");
        }
        if (!ConfirmGate.IsConfirmed(args))
        {
            return ConfirmGate.Proposal("group_remove_member_proposal", action, new { groupId, userId });
        }
        var store = ctx.Services.GetRequiredService<IGroupStore>();
        var audit = ctx.Services.GetRequiredService<IAuditEventPublisher>();
        var removed = await store.RemoveMemberAsync(groupId, userId, ct);
        if (!removed)
        {
            return ConfirmGate.Failed("group_remove_member_failed", action, "User was not a member of this group.");
        }
        await audit.PublishAsync(
            IamEventTopic.TopicName, IamEventTypes.GroupMemberRemoved,
            IamResourceKinds.GroupMember,
            resource: new { groupId, userId }, details: new { source = "chatbot" }, ct);
        return ConfirmGate.Committed("group_remove_member_committed", action, new { groupId, userId });
    }

    // ---- role assignments -----------------------------------------------

    private static async Task<JsonElement> InvokeAssignRoleAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "assign_role";
        if (!TryReadGuid(args, "roleId", out var roleId))
            return ConfirmGate.Rejected(action, "roleId is required and must be a GUID.");
        var principalKind = ReadString(args, "principalKind");
        var principalId = ReadString(args, "principalId");
        if (string.IsNullOrWhiteSpace(principalKind)) return ConfirmGate.Rejected(action, "principalKind is required.");
        if (string.IsNullOrWhiteSpace(principalId)) return ConfirmGate.Rejected(action, "principalId is required.");
        var scopeString = ReadString(args, "scopeString");

        if (!await CanKindAsync(ctx, EntityKinds.Role, Actions.Assign, ct))
        {
            return ConfirmGate.Rejected(action, "Role:assign permission required.");
        }
        var preview = new { roleId, principalKind, principalId, scopeString };
        if (!ConfirmGate.IsConfirmed(args))
        {
            return ConfirmGate.Proposal("role_assign_proposal", action, preview);
        }
        var store = ctx.Services.GetRequiredService<IRoleAssignmentStore>();
        var audit = ctx.Services.GetRequiredService<IAuditEventPublisher>();
        try
        {
            var assignment = await store.AssignAsync(
                new CreateRoleAssignmentInput(roleId, principalKind, principalId, scopeString),
                ctx.Session.UserId, ct);
            await audit.PublishAsync(
                IamEventTopic.TopicName, IamEventTypes.RoleAssignmentGranted,
                IamResourceKinds.RoleAssignment,
                resource: new { id = assignment.Id, roleId, principalKind, principalId },
                details: new { source = "chatbot", scopeString }, ct);
            return ConfirmGate.Committed("role_assign_committed", action, new
            {
                id = assignment.Id, roleId, principalKind, principalId
            });
        }
        catch (RoleAssignmentValidationException ex)
        {
            return ConfirmGate.Failed("role_assign_failed", action, ex.Message);
        }
    }

    private static async Task<JsonElement> InvokeUnassignRoleAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "unassign_role";
        if (!TryReadGuid(args, "assignmentId", out var assignmentId))
            return ConfirmGate.Rejected(action, "assignmentId is required and must be a GUID.");
        if (!await CanKindAsync(ctx, EntityKinds.Role, Actions.Assign, ct))
        {
            return ConfirmGate.Rejected(action, "Role:assign permission required.");
        }
        if (!ConfirmGate.IsConfirmed(args))
        {
            return ConfirmGate.Proposal("role_unassign_proposal", action, new { assignmentId });
        }
        var store = ctx.Services.GetRequiredService<IRoleAssignmentStore>();
        var audit = ctx.Services.GetRequiredService<IAuditEventPublisher>();
        var ok = await store.RevokeAsync(assignmentId, ct);
        if (!ok)
        {
            return ConfirmGate.Failed("role_unassign_failed", action, $"Role assignment {assignmentId} not found.");
        }
        await audit.PublishAsync(
            IamEventTopic.TopicName, IamEventTypes.RoleAssignmentRevoked,
            IamResourceKinds.RoleAssignment,
            resource: new { id = assignmentId }, details: new { source = "chatbot" }, ct);
        return ConfirmGate.Committed("role_unassign_committed", action, new { id = assignmentId });
    }

    // ---- arg helpers ----------------------------------------------------

    private static bool TryReadGuid(JsonElement args, string name, out Guid id)
    {
        id = Guid.Empty;
        if (!args.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        return Guid.TryParse(v.GetString(), out id);
    }

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
