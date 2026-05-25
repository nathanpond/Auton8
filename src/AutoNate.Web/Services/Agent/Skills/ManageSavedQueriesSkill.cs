using System.Text.Json;
using AutoNate.Web.Services.Agent.Skills.Internal;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Query;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Phase 3 saved-query CRUD. Owner-only enforcement is in ISavedQueryStore
// (UpdateAsync / DeleteAsync route by owner_user_id, throwing SavedQueryNotFound
// for non-owners and missing rows alike). The skill is a thin wrapper around
// the same store that powers /api/saved-queries, so the chatbot's saves
// land in the same catalog the SPA Select reads from.
public sealed class ManageSavedQueriesSkill : IAgentSkill
{
    public string Name => "manage-saved-queries";

    public string Description =>
        "Save, update, and delete AQL saved queries. Owner-only.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public ManageSavedQueriesSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "save_query",
                Description: "Save a new AQL query for the current user. Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "name": { "type": "string" },
                        "description": { "type": ["string", "null"] },
                        "queryText": { "type": "string", "description": "The AQL query text." },
                        "isShared": { "type": "boolean", "description": "If true, every authenticated user can list and execute this saved query. Defaults to false." },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["name", "queryText"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeSaveAsync),

            new AgentTool(
                Name: "update_saved_query",
                Description: "Update an existing saved query owned by the current user.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "id": { "type": "string", "description": "Saved-query GUID." },
                        "name": { "type": ["string", "null"] },
                        "description": { "type": ["string", "null"] },
                        "queryText": { "type": ["string", "null"] },
                        "isShared": { "type": ["boolean", "null"] },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeUpdateAsync),

            new AgentTool(
                Name: "delete_saved_query",
                Description: "Delete a saved query owned by the current user.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "id": { "type": "string" },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeDeleteAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Saved queries are owner-scoped. Non-owners can read shared rows via lookup-aql.list_saved_queries but only the owner can update or delete. Set isShared=true to share with every authenticated user.";

    private static async Task<JsonElement> InvokeSaveAsync(JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        const string action = "save_query";
        var name = ReadString(args, "name");
        var queryText = ReadString(args, "queryText");
        if (string.IsNullOrWhiteSpace(name))
            return ConfirmGate.Rejected(action, "name is required.");
        if (string.IsNullOrWhiteSpace(queryText))
            return ConfirmGate.Rejected(action, "queryText is required.");
        var description = ReadString(args, "description");
        var isShared = args.TryGetProperty("isShared", out var s) && s.ValueKind == JsonValueKind.True;

        var preview = new { name = name.Trim(), description, isShared, queryText };
        if (!ConfirmGate.IsConfirmed(args))
        {
            return ConfirmGate.Proposal("saved_query_save_proposal", action, preview);
        }

        var store = context.Services.GetRequiredService<ISavedQueryStore>();
        var auditPublisher = context.Services.GetRequiredService<IAuditEventPublisher>();
        try
        {
            var saved = await store.CreateAsync(
                new CreateSavedQueryInput(name.Trim(), description?.Trim(), queryText, isShared),
                context.Session.UserId, ct);
            await auditPublisher.PublishAsync(
                QueryEventTopic.TopicName,
                QueryEventTypes.SavedQuerySaved,
                QueryResourceKinds.SavedQuery,
                resource: new { id = saved.Id, name = saved.Name },
                details: new { source = "chatbot", isShared = saved.IsShared, queryText = saved.QueryText },
                ct);
            return ConfirmGate.Committed("saved_query_save_committed", action, new
            {
                id = saved.Id,
                name = saved.Name,
                isShared = saved.IsShared
            });
        }
        catch (SavedQueryNameConflictException ex)
        {
            return ConfirmGate.Failed("saved_query_save_failed", action, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return ConfirmGate.Failed("saved_query_save_failed", action, ex.Message);
        }
    }

    private static async Task<JsonElement> InvokeUpdateAsync(JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        const string action = "update_saved_query";
        if (!TryReadGuid(args, "id", out var id))
        {
            return ConfirmGate.Rejected(action, "id is required and must be a GUID.");
        }
        var name = ReadString(args, "name");
        var description = ReadString(args, "description");
        var queryText = ReadString(args, "queryText");
        bool? isShared = args.TryGetProperty("isShared", out var s)
            && (s.ValueKind == JsonValueKind.True || s.ValueKind == JsonValueKind.False)
            ? s.GetBoolean() : null;

        var store = context.Services.GetRequiredService<ISavedQueryStore>();
        var existing = await store.GetForActorAsync(id, context.Session.UserId, ct);
        if (existing is null || existing.OwnerUserId != context.Session.UserId)
        {
            return ConfirmGate.Rejected(action, $"Saved query {id} not owned by current user (or not found).");
        }

        var preview = new { id, before = new { existing.Name, existing.IsShared, existing.QueryText }, patch = new { name, description, queryText, isShared } };
        if (!ConfirmGate.IsConfirmed(args))
        {
            return ConfirmGate.Proposal("saved_query_update_proposal", action, preview);
        }

        var auditPublisher = context.Services.GetRequiredService<IAuditEventPublisher>();
        try
        {
            var saved = await store.UpdateAsync(id,
                new UpdateSavedQueryInput(name?.Trim(), description?.Trim(), queryText, isShared),
                context.Session.UserId, ct);
            await auditPublisher.PublishAsync(
                QueryEventTopic.TopicName,
                QueryEventTypes.SavedQueryUpdated,
                QueryResourceKinds.SavedQuery,
                resource: new { id = saved.Id, name = saved.Name },
                details: new { source = "chatbot", isShared = saved.IsShared, queryText = saved.QueryText },
                ct);
            return ConfirmGate.Committed("saved_query_update_committed", action, new
            {
                id = saved.Id,
                name = saved.Name,
                isShared = saved.IsShared
            });
        }
        catch (SavedQueryNotFoundException)
        {
            return ConfirmGate.Failed("saved_query_update_failed", action, $"Saved query {id} not found or not owned by current user.");
        }
        catch (SavedQueryNameConflictException ex)
        {
            return ConfirmGate.Failed("saved_query_update_failed", action, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return ConfirmGate.Failed("saved_query_update_failed", action, ex.Message);
        }
    }

    private static async Task<JsonElement> InvokeDeleteAsync(JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        const string action = "delete_saved_query";
        if (!TryReadGuid(args, "id", out var id))
        {
            return ConfirmGate.Rejected(action, "id is required and must be a GUID.");
        }
        var store = context.Services.GetRequiredService<ISavedQueryStore>();
        var existing = await store.GetForActorAsync(id, context.Session.UserId, ct);
        if (existing is null || existing.OwnerUserId != context.Session.UserId)
        {
            return ConfirmGate.Rejected(action, $"Saved query {id} not owned by current user (or not found).");
        }
        var preview = new { id, name = existing.Name };
        if (!ConfirmGate.IsConfirmed(args))
        {
            return ConfirmGate.Proposal("saved_query_delete_proposal", action, preview);
        }
        var auditPublisher = context.Services.GetRequiredService<IAuditEventPublisher>();
        var ok = await store.DeleteAsync(id, context.Session.UserId, ct);
        if (!ok)
        {
            return ConfirmGate.Failed("saved_query_delete_failed", action, $"Saved query {id} not found.");
        }
        await auditPublisher.PublishAsync(
            QueryEventTopic.TopicName,
            QueryEventTypes.SavedQueryDeleted,
            QueryResourceKinds.SavedQuery,
            resource: new { id, name = existing.Name },
            details: new { source = "chatbot" },
            ct);
        return ConfirmGate.Committed("saved_query_delete_committed", action, new { id });
    }

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
