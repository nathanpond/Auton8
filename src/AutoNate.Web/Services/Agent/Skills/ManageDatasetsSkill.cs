using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Agent.Skills.Internal;
using AutoNate.Web.Services.DataStores;
using AutoNate.Web.Services.DataStores.Sql;
using AutoNate.Web.Services.Datasets;
using AutoNate.Web.Services.Datasets.Cached;
using Microsoft.EntityFrameworkCore;
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
        "Datasets are addressed in AQL by name (`FROM Dataset(\"foo\")`). Source bindings are locked at creation — update_dataset only touches name/description/refreshCron. Refresh applies to Cached datasets only. " +
        "BEFORE create_dataset: call lookup-datasets.list_datasets first. If a dataset already covers the user's data (same source datastore/table or a name they referenced), reuse it — dataset names are uniquely indexed and create_dataset on an existing name returns a hard failure, not an upsert. Only create a new dataset when the user explicitly asks for one or no existing dataset fits. " +
        "When you do create one: NEVER fabricate sourceId, sourceTableName, or column names. Always look them up first — sourceId via lookup-datastores.list_datastores, the table + actual column list via lookup-datastores.list_data_store_tables(dataStoreId=...). The skill now rejects the proposal if sourceId, sourceTableName, or any declared column name doesn't exist in the underlying datastore.";

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

        // Validate the source resolves to a real datastore + table + column set
        // BEFORE proposing. Without this an LLM is free to fabricate sourceId,
        // sourceTableName, and column names — the row writes, the dataset
        // appears to exist, and every downstream AQL query fails opaquely
        // (which is exactly what happened with the first Weather Temperatures
        // dataset). dataconnector sources are validated elsewhere; only
        // 'datastore' source_kind is checked here.
        if (string.Equals(sourceKind, "datastore", StringComparison.OrdinalIgnoreCase))
        {
            var validationError = await ValidateDataStoreSourceAsync(
                ctx, sourceId, sourceTableName!, columns, ct);
            if (validationError is not null)
                return ConfirmGate.Rejected(action, validationError);
        }

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

    // Returns null when the source resolves cleanly; otherwise a user-facing
    // error string suitable for ConfirmGate.Rejected. Checks (in order):
    //   1. the referenced datastore exists,
    //   2. for SqlType datastores the table is registered in DataStoreTables,
    //   3. every declared column name appears in the table's column schema
    //      (case-insensitive). The cache materializer SELECTs by column name
    //      so any unknown column would blow up at refresh — surfacing it here
    //      means the agent sees the misnamed column at proposal time.
    // FileType datastores skip steps 2-3 because there's no row-shaped table
    // registered; the dataset executor handles them through its own path.
    private static async Task<string?> ValidateDataStoreSourceAsync(
        AgentToolContext ctx,
        Guid sourceId,
        string sourceTableName,
        IReadOnlyList<DatasetColumn> declaredColumns,
        CancellationToken ct)
    {
        var dataStoreStore = ctx.Services.GetRequiredService<IDataStoreStore>();
        var dataStore = await dataStoreStore.GetAsync(sourceId, ct);
        if (dataStore is null)
            return $"sourceId {sourceId} does not match any datastore. Call lookup-datastores.list_datastores to find the real id.";

        if (dataStore.Kind != (short)DataStoreKind.SqlType)
            return null;

        var dbFactory = ctx.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var table = await db.DataStoreTables
            .AsNoTracking()
            .Where(t => t.DataStoreId == sourceId
                && EF.Functions.ILike(t.TableName, sourceTableName))
            .SingleOrDefaultAsync(ct);
        if (table is null)
            return $"Table '{sourceTableName}' was not found in datastore '{dataStore.Name}'. Call lookup-datastores.list_data_store_tables with dataStoreId={sourceId} to see the real table names.";

        HashSet<string> actualColumnNames;
        try
        {
            var schema = JsonSerializer.Deserialize<List<CsvColumn>>(
                table.ColumnSchemaJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? new List<CsvColumn>();
            actualColumnNames = schema
                .Select(c => c.Name ?? string.Empty)
                .Where(n => n.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            // Malformed table schema row — let creation proceed and surface at
            // refresh/query time rather than block on a corrupt registry row.
            return null;
        }

        var missing = declaredColumns
            .Where(c => !string.IsNullOrWhiteSpace(c.Name)
                && !actualColumnNames.Contains(c.Name))
            .Select(c => c.Name)
            .ToList();
        if (missing.Count > 0)
            return $"Declared columns [{string.Join(", ", missing)}] are not in table '{sourceTableName}'. Actual columns: [{string.Join(", ", actualColumnNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}].";

        return null;
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
