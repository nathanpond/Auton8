using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Services.Agent.Skills.Internal;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Records;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Phase 5a — record-edge instance CRUD. Edge-type schema authoring is
// admin-heavy and stays in the SPA; the chatbot's value here is in
// connecting records together ("link incident INC-12 to incident INC-15
// as depends-on"). Authorization mirrors RecordEdgeEndpoints: list reads
// route through Record:View per endpoint; creates / deletes require
// Record:Edit on BOTH endpoints to avoid privilege escalation.
public sealed class ManageRecordEdgesSkill : IAgentSkill
{
    public string Name => "manage-record-edges";

    public string Description =>
        "List and modify typed relationships between records. Linking requires Record:Edit on both endpoints.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public ManageRecordEdgesSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "list_record_edges",
                Description: "List the edges attached to a record. Filter direction (both / outgoing / incoming) and / or edgeTypeId. Other-endpoint records hidden when the actor cannot view them.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "recordId": { "type": "string", "description": "Record GUID." },
                        "direction": { "type": ["string", "null"], "enum": ["both", "outgoing", "incoming", null] },
                        "edgeTypeId": { "type": ["string", "null"], "description": "Optional edge-type GUID filter." }
                      },
                      "required": ["recordId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListAsync),

            new AgentTool(
                Name: "create_record_edge",
                Description: "Link two records via a typed edge. Requires Record:Edit on BOTH endpoints. Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "edgeTypeId": { "type": "string" },
                        "fromRecordId": { "type": "string" },
                        "toRecordId": { "type": "string" },
                        "data": { "type": ["object", "null"], "description": "Optional edge-data JSON object matching the edge type's field schema." },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["edgeTypeId", "fromRecordId", "toRecordId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeCreateAsync),

            new AgentTool(
                Name: "delete_record_edge",
                Description: "Delete a record edge by id. Requires Record:Edit on both endpoints. Confirm-gated.",
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
        "Record edges are typed relationships (depends-on, references, etc.). Use lookup-records to resolve record GUIDs first. Creating an edge requires Edit on BOTH endpoints — the chatbot will refuse if either fails.";

    private static EdgeDirection ParseDirection(string? raw) => raw?.ToLowerInvariant() switch
    {
        "outgoing" or "out" => EdgeDirection.Outgoing,
        "incoming" or "in" => EdgeDirection.Incoming,
        _ => EdgeDirection.Both
    };

    private static async Task<bool> CanRecordAsync(AgentToolContext ctx, Guid recordId, string action, CancellationToken ct)
    {
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, action, new EntityRef(EntityKinds.Record, recordId.ToString()), ct);
        return decision.IsAllowed;
    }

    private static async Task<JsonElement> InvokeListAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "list_record_edges";
        if (!TryReadGuid(args, "recordId", out var recordId))
            return Error(action, "recordId is required and must be a GUID.");
        if (!await CanRecordAsync(ctx, recordId, Actions.View, ct))
            return Error(action, $"Record:view required on {recordId}.");
        var direction = ParseDirection(ReadString(args, "direction"));
        Guid? edgeTypeId = TryReadGuid(args, "edgeTypeId", out var et) ? et : null;

        var store = ctx.Services.GetRequiredService<IRecordEdgeStore>();
        var edges = await store.ListForRecordAsync(recordId, direction, edgeTypeId, ct);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "record_edges",
            source = "IRecordEdgeStore",
            data = edges.Select(e => new
            {
                id = e.Id,
                edgeTypeId = e.EdgeTypeId,
                fromRecordId = e.FromRecordId,
                toRecordId = e.ToRecordId,
                data = e.Data,
                createdAtUtc = e.CreatedAtUtc
            }).ToArray()
        });
    }

    private static async Task<JsonElement> InvokeCreateAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "create_record_edge";
        if (!TryReadGuid(args, "edgeTypeId", out var edgeTypeId))
            return ConfirmGate.Rejected(action, "edgeTypeId is required and must be a GUID.");
        if (!TryReadGuid(args, "fromRecordId", out var fromId))
            return ConfirmGate.Rejected(action, "fromRecordId is required and must be a GUID.");
        if (!TryReadGuid(args, "toRecordId", out var toId))
            return ConfirmGate.Rejected(action, "toRecordId is required and must be a GUID.");

        if (!await CanRecordAsync(ctx, fromId, Actions.Edit, ct))
            return ConfirmGate.Rejected(action, $"Record:edit required on fromRecordId {fromId}.");
        if (!await CanRecordAsync(ctx, toId, Actions.Edit, ct))
            return ConfirmGate.Rejected(action, $"Record:edit required on toRecordId {toId}.");

        var data = args.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object
            ? d.Clone()
            : ParseSchema("{}");

        var preview = new { edgeTypeId, fromRecordId = fromId, toRecordId = toId, data };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("record_edge_create_proposal", action, preview);

        var store = ctx.Services.GetRequiredService<IRecordEdgeStore>();
        var audit = ctx.Services.GetRequiredService<IAuditEventPublisher>();
        try
        {
            var created = await store.CreateAsync(
                new CreateRecordEdgeInput(edgeTypeId, fromId, toId, data),
                ctx.Session.UserId, ct);
            await audit.PublishAsync(
                RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordEdgeCreated,
                RecordSchemaResourceKinds.RecordEdge,
                resource: new { id = created.Id, edgeTypeId, fromRecordId = fromId, toRecordId = toId },
                details: new { source = "chatbot" }, ct);
            return ConfirmGate.Committed("record_edge_create_committed", action, new
            {
                id = created.Id,
                edgeTypeId,
                fromRecordId = fromId,
                toRecordId = toId
            });
        }
        catch (RecordEdgeTypeNotFoundException)
        {
            return ConfirmGate.Failed("record_edge_create_failed", action, $"Edge type {edgeTypeId} not found.");
        }
        catch (RecordEdgeValidationException ex)
        {
            return ConfirmGate.Failed("record_edge_create_failed", action, ex.Message);
        }
    }

    private static async Task<JsonElement> InvokeDeleteAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "delete_record_edge";
        if (!TryReadGuid(args, "id", out var id))
            return ConfirmGate.Rejected(action, "id is required and must be a GUID.");
        var store = ctx.Services.GetRequiredService<IRecordEdgeStore>();
        var edge = await store.GetAsync(id, ct);
        if (edge is null) return ConfirmGate.Rejected(action, $"Edge {id} not found.");
        if (!await CanRecordAsync(ctx, edge.FromRecordId, Actions.Edit, ct))
            return ConfirmGate.Rejected(action, $"Record:edit required on fromRecordId {edge.FromRecordId}.");
        if (!await CanRecordAsync(ctx, edge.ToRecordId, Actions.Edit, ct))
            return ConfirmGate.Rejected(action, $"Record:edit required on toRecordId {edge.ToRecordId}.");

        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("record_edge_delete_proposal", action,
                new { id, edge.FromRecordId, edge.ToRecordId, edge.EdgeTypeId });

        var audit = ctx.Services.GetRequiredService<IAuditEventPublisher>();
        await store.DeleteAsync(id, ct);
        await audit.PublishAsync(
            RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordEdgeDeleted,
            RecordSchemaResourceKinds.RecordEdge,
            resource: new { id },
            details: new { source = "chatbot" }, ct);
        return ConfirmGate.Committed("record_edge_delete_committed", action, new { id });
    }

    private static bool TryReadGuid(JsonElement args, string name, out Guid id)
    {
        id = Guid.Empty;
        if (!args.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String) return false;
        return Guid.TryParse(v.GetString(), out id);
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
