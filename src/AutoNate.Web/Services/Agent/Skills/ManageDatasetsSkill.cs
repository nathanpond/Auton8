using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Services.Agent.Skills.Internal;
using AutoNate.Web.Services.Datasets;
using AutoNate.Web.Services.Datasets.Cached;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Confirm-gated dataset mutations: create / update / delete / refresh. Mirrors
// /api/datasets endpoints. Schema validation lives in IDatasetStore + the
// materializer — we surface their exceptions as ConfirmGate.Failed envelopes
// so the model can re-narrate without losing the user's intent.
public sealed class ManageDatasetsSkill : IAgentSkill
{
    public string Name => "manage-datasets";

    public string Description =>
        "Create, update, delete, and refresh datasets that front AQL `FROM Dataset(...)` queries.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public ManageDatasetsSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "create_dataset",
                Description: "Create a new dataset. `mode` is 'Virtual' (passthrough query) or 'Cached' (materialized). `sourceKind` is 'datastore' or 'dataconnector'; `sourceId` is the source's GUID; `sourceTableName` is required for datastore sources. `columns` is the locked schema: [{name, postgresType}]. `refreshCron` is optional (5-field cron, Cached only). Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "name": { "type": "string" },
                        "description": { "type": ["string", "null"] },
                        "mode": { "type": "string", "enum": ["Virtual", "Cached"] },
                        "sourceKind": { "type": "string", "enum": ["datastore", "dataconnector"] },
                        "sourceId": { "type": "string" },
                        "sourceTableName": { "type": ["string", "null"] },
                        "refreshCron": { "type": ["string", "null"] },
                        "columns": {
                          "type": "array",
                          "minItems": 1,
                          "items": {
                            "type": "object",
                            "properties": {
                              "name": { "type": "string" },
                              "postgresType": { "type": "string", "description": "One of: text, bigint, double precision, boolean, timestamptz." }
                            },
                            "required": ["name", "postgresType"],
                            "additionalProperties": false
                          }
                        },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["name", "mode", "sourceKind", "sourceId", "columns"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeCreateAsync),

            new AgentTool(
                Name: "update_dataset",
                Description: "Update a dataset's name, description, or refresh cron. Mode and source bindings are immutable — recreate to change those. Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "id": { "type": "string" },
                        "name": { "type": ["string", "null"] },
                        "description": { "type": ["string", "null"] },
                        "refreshCron": { "type": ["string", "null"], "description": "Set to empty string to clear (manual-refresh only). 5-field cron expression." },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeUpdateAsync),

            new AgentTool(
                Name: "delete_dataset",
                Description: "Delete a dataset. Saved queries and dashboard widgets that reference it will start returning errors. Irreversible; confirm-gated.",
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
                Invoke: InvokeDeleteAsync),

            new AgentTool(
                Name: "refresh_dataset",
                Description: "Manually refresh a Cached dataset's materialized rows. Synchronous; returns when the source has been re-read. Virtual datasets return an error. Confirm-gated because a refresh truncates and re-writes the cache.",
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
                Invoke: InvokeRefreshAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Datasets are addressed in AQL by name (`FROM Dataset(\"foo\")`). Source bindings are locked at creation — update_dataset only touches name/description/refreshCron. Refresh applies to Cached datasets only.";

    private static async Task<JsonElement> InvokeCreateAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "create_dataset";
        var name = ReadString(args, "name");
        if (string.IsNullOrWhiteSpace(name)) return ConfirmGate.Rejected(action, "name is required.");
        var modeRaw = ReadString(args, "mode");
        if (string.IsNullOrWhiteSpace(modeRaw) || !Enum.TryParse<DatasetMode>(modeRaw, ignoreCase: true, out var mode))
            return ConfirmGate.Rejected(action, "mode must be 'Virtual' or 'Cached'.");
        var sourceKind = ReadString(args, "sourceKind");
        if (string.IsNullOrWhiteSpace(sourceKind)) return ConfirmGate.Rejected(action, "sourceKind is required.");
        if (!TryReadGuid(args, "sourceId", out var sourceId))
            return ConfirmGate.Rejected(action, "sourceId is required and must be a GUID.");
        var sourceTableName = ReadString(args, "sourceTableName");
        if (string.Equals(sourceKind, "datastore", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(sourceTableName))
            return ConfirmGate.Rejected(action, "sourceTableName is required for datastore sources.");
        var refreshCron = ReadString(args, "refreshCron");
        var description = ReadString(args, "description");

        if (!args.TryGetProperty("columns", out var columnsEl) || columnsEl.ValueKind != JsonValueKind.Array || columnsEl.GetArrayLength() == 0)
            return ConfirmGate.Rejected(action, "columns is required and must have at least one entry.");
        List<DatasetColumn> columns;
        try
        {
            columns = JsonSerializer.Deserialize<List<DatasetColumn>>(
                columnsEl.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? new List<DatasetColumn>();
        }
        catch (JsonException ex)
        {
            return ConfirmGate.Rejected(action, "columns malformed: " + ex.Message);
        }
        if (columns.Count == 0) return ConfirmGate.Rejected(action, "columns is required.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Create, new EntityRef(EntityKinds.Dataset, string.Empty), ct);
        if (!decision.IsAllowed)
            return ConfirmGate.Rejected(action, "Dataset:Create permission required.");

        var preview = new
        {
            name = name.Trim(),
            description,
            mode = mode.ToString(),
            sourceKind,
            sourceId,
            sourceTableName,
            refreshCron,
            columnCount = columns.Count,
            columns
        };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("dataset_create_proposal", action, preview);

        var store = ctx.Services.GetRequiredService<IDatasetStore>();
        try
        {
            var row = await store.CreateAsync(
                new CreateDatasetInput(
                    name.Trim(), description?.Trim(), mode, columns,
                    sourceKind, sourceId, sourceTableName, refreshCron),
                ctx.Session.UserId, ct);
            return ConfirmGate.Committed("dataset_create_committed", action, new
            {
                id = row.Id,
                name = row.Name,
                mode = ((DatasetMode)row.Mode).ToString()
            });
        }
        catch (DatasetNameConflictException ex)
        {
            return ConfirmGate.Failed("dataset_create_failed", action, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return ConfirmGate.Failed("dataset_create_failed", action, ex.Message);
        }
    }

    private static async Task<JsonElement> InvokeUpdateAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "update_dataset";
        if (!TryReadGuid(args, "id", out var id))
            return ConfirmGate.Rejected(action, "id is required and must be a GUID.");
        var name = ReadString(args, "name");
        var description = ReadString(args, "description");
        var refreshCron = ReadString(args, "refreshCron");
        if (name is null && description is null && refreshCron is null)
            return ConfirmGate.Rejected(action, "At least one of name, description, or refreshCron must be set.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Edit, new EntityRef(EntityKinds.Dataset, id.ToString()), ct);
        if (!decision.IsAllowed)
            return ConfirmGate.Rejected(action, $"Dataset:Edit permission denied on {id}.");

        var store = ctx.Services.GetRequiredService<IDatasetStore>();
        var existing = await store.GetAsync(id, ct);
        if (existing is null) return ConfirmGate.Rejected(action, $"Dataset {id} not found.");

        var preview = new
        {
            id,
            before = new { existing.Name, existing.Description, existing.RefreshCron },
            patch = new { name, description, refreshCron }
        };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("dataset_update_proposal", action, preview);

        try
        {
            var row = await store.UpdateAsync(
                id, new UpdateDatasetInput(name?.Trim(), description?.Trim(), refreshCron), ctx.Session.UserId, ct);
            return ConfirmGate.Committed("dataset_update_committed", action, new
            {
                id = row.Id,
                name = row.Name,
                description = row.Description,
                refreshCron = row.RefreshCron
            });
        }
        catch (DatasetNotFoundException)
        {
            return ConfirmGate.Failed("dataset_update_failed", action, $"Dataset {id} not found.");
        }
        catch (DatasetNameConflictException ex)
        {
            return ConfirmGate.Failed("dataset_update_failed", action, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return ConfirmGate.Failed("dataset_update_failed", action, ex.Message);
        }
    }

    private static async Task<JsonElement> InvokeDeleteAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "delete_dataset";
        if (!TryReadGuid(args, "id", out var id))
            return ConfirmGate.Rejected(action, "id is required and must be a GUID.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Delete, new EntityRef(EntityKinds.Dataset, id.ToString()), ct);
        if (!decision.IsAllowed)
            return ConfirmGate.Rejected(action, $"Dataset:Delete permission denied on {id}.");

        var store = ctx.Services.GetRequiredService<IDatasetStore>();
        var existing = await store.GetAsync(id, ct);
        if (existing is null) return ConfirmGate.Rejected(action, $"Dataset {id} not found.");

        var preview = new
        {
            id,
            existing.Name,
            mode = ((DatasetMode)existing.Mode).ToString(),
            warning = "Irreversible. Saved queries and widgets that reference this dataset will start failing."
        };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("dataset_delete_proposal", action, preview);

        var deleted = await store.DeleteAsync(id, ct);
        if (!deleted) return ConfirmGate.Failed("dataset_delete_failed", action, $"Dataset {id} not found.");
        return ConfirmGate.Committed("dataset_delete_committed", action, new { id, name = existing.Name });
    }

    private static async Task<JsonElement> InvokeRefreshAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "refresh_dataset";
        if (!TryReadGuid(args, "id", out var id))
            return ConfirmGate.Rejected(action, "id is required.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Refresh, new EntityRef(EntityKinds.Dataset, id.ToString()), ct);
        if (!decision.IsAllowed)
            return ConfirmGate.Rejected(action, $"Dataset:Refresh permission denied on {id}.");

        var store = ctx.Services.GetRequiredService<IDatasetStore>();
        var existing = await store.GetAsync(id, ct);
        if (existing is null) return ConfirmGate.Rejected(action, $"Dataset {id} not found.");
        if (existing.Mode != (short)DatasetMode.Cached)
            return ConfirmGate.Rejected(action, $"Dataset '{existing.Name}' is Virtual; only Cached datasets can be refreshed.");

        var preview = new { id, existing.Name, lastRefreshedAtUtc = existing.LastRefreshedAtUtc };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("dataset_refresh_proposal", action, preview);

        var materializer = ctx.Services.GetRequiredService<ICachedDatasetMaterializer>();
        try
        {
            await materializer.RefreshAsync(id, ct);
            return ConfirmGate.Committed("dataset_refresh_committed", action, new { id, name = existing.Name });
        }
        catch (DatasetNotFoundException)
        {
            return ConfirmGate.Failed("dataset_refresh_failed", action, $"Dataset {id} not found.");
        }
        catch (InvalidOperationException ex)
        {
            return ConfirmGate.Failed("dataset_refresh_failed", action, ex.Message);
        }
    }

    private static bool TryReadGuid(JsonElement args, string name, out Guid id)
    {
        id = Guid.Empty;
        return args.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String
            && Guid.TryParse(v.GetString(), out id);
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
