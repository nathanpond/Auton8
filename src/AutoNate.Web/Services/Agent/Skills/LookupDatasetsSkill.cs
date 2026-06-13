using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.DataStores;
using AutoNate.Web.Services.Datasets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Read-only dataset inspection. Mirrors GET /api/datasets so the chatbot can
// list datasets, fetch metadata for one, and resolve a dataset's source down
// to "this lives in datastore X, table Y" — the latter is what's needed to
// help the user draft AQL queries against a dataset.
public sealed class LookupDatasetsSkill : IAgentSkill
{
    public string Name => "lookup-datasets";

    public string Description =>
        "List datasets, fetch one by id, and resolve a dataset's source to its underlying datastore / table.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public LookupDatasetsSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "list_datasets",
                Description: "List datasets. Optional `mode` filters Virtual or Cached; `sourceKind` filters to 'datastore' or 'dataconnector'; `search` is a case-insensitive name substring.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "mode": { "type": ["string", "null"], "enum": ["Virtual", "Cached", null] },
                        "sourceKind": { "type": ["string", "null"], "enum": ["datastore", "dataconnector", null] },
                        "search": { "type": ["string", "null"] }
                      },
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListAsync),

            new AgentTool(
                Name: "get_dataset",
                Description: "Fetch one dataset by id, including the locked column schema, refresh cron, and last-refreshed timestamp.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": { "id": { "type": "string" } },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeGetAsync),

            new AgentTool(
                Name: "describe_dataset_source",
                Description: "Resolve a dataset's source: the underlying datastore name, the source table (for datastore sources), and the dataset's column schema. Use this when drafting AQL `FROM Dataset(\"name\")` queries.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": { "id": { "type": "string" } },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeDescribeSourceAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Datasets front AQL queries via `FROM Dataset(\"name\")`. Mode Virtual = executes against source on each query; Mode Cached = materialized into autonate_datastores by the refresh scheduler. Cached datasets honor `refreshCron` (5-field cron) and can be manually refreshed via manage-datasets.refresh_dataset.";

    private static async Task<JsonElement> InvokeListAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        var modeFilter = ReadString(args, "mode");
        var sourceKindFilter = ReadString(args, "sourceKind");
        var search = ReadString(args, "search");

        DatasetMode? mode = null;
        if (!string.IsNullOrWhiteSpace(modeFilter))
        {
            if (!Enum.TryParse<DatasetMode>(modeFilter, ignoreCase: true, out var parsed))
                return Error("list_datasets", $"Unknown dataset mode '{modeFilter}'.");
            mode = parsed;
        }

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var listDecision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.List, new EntityRef(EntityKinds.Dataset, string.Empty), ct);
        if (!listDecision.IsAllowed)
            return Error("list_datasets", "Dataset:List permission required.");

        var store = ctx.Services.GetRequiredService<IDatasetStore>();
        var rows = await store.ListAsync(ct);
        IEnumerable<Persistence.Scaffolded.Dataset> filtered = rows;
        if (mode is { } m) filtered = filtered.Where(d => d.Mode == (short)m);
        if (!string.IsNullOrWhiteSpace(sourceKindFilter))
            filtered = filtered.Where(d => string.Equals(d.SourceKind, sourceKindFilter, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(search))
            filtered = filtered.Where(d => d.Name.Contains(search, StringComparison.OrdinalIgnoreCase));

        var data = filtered.Select(d => new
        {
            id = d.Id,
            name = d.Name,
            description = d.Description,
            mode = ((DatasetMode)d.Mode).ToString(),
            sourceKind = d.SourceKind,
            sourceId = d.SourceId,
            sourceTableName = d.SourceTableName,
            refreshCron = d.RefreshCron,
            lastRefreshedAtUtc = d.LastRefreshedAtUtc,
            updatedAtUtc = d.UpdatedAtUtc
        }).ToList();

        return JsonSerializer.SerializeToElement(new
        {
            kind = "datasets",
            source = "IDatasetStore",
            data
        });
    }

    private static async Task<JsonElement> InvokeGetAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        if (!TryReadGuid(args, "id", out var id))
            return Error("get_dataset", "id is required and must be a GUID.");
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.Dataset, id.ToString()), ct);
        if (!decision.IsAllowed)
            return Error("get_dataset", $"Dataset:View permission denied on {id}.");

        var store = ctx.Services.GetRequiredService<IDatasetStore>();
        var row = await store.GetAsync(id, ct);
        if (row is null) return Error("get_dataset", $"Dataset {id} not found.");

        var columns = DatasetSchemaCodec.Decode(row.ColumnSchemaJson);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "dataset",
            source = "IDatasetStore",
            data = new
            {
                id = row.Id,
                name = row.Name,
                description = row.Description,
                mode = ((DatasetMode)row.Mode).ToString(),
                sourceKind = row.SourceKind,
                sourceId = row.SourceId,
                sourceTableName = row.SourceTableName,
                refreshCron = row.RefreshCron,
                lastRefreshedAtUtc = row.LastRefreshedAtUtc,
                ownerUserId = row.OwnerUserId,
                createdAtUtc = row.CreatedAtUtc,
                updatedAtUtc = row.UpdatedAtUtc,
                columns
            }
        });
    }

    private static async Task<JsonElement> InvokeDescribeSourceAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        if (!TryReadGuid(args, "id", out var id))
            return Error("describe_dataset_source", "id is required.");
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.Dataset, id.ToString()), ct);
        if (!decision.IsAllowed)
            return Error("describe_dataset_source", $"Dataset:View permission denied on {id}.");

        var store = ctx.Services.GetRequiredService<IDatasetStore>();
        var row = await store.GetAsync(id, ct);
        if (row is null) return Error("describe_dataset_source", $"Dataset {id} not found.");

        object source;
        if (string.Equals(row.SourceKind, "datastore", StringComparison.OrdinalIgnoreCase))
        {
            var dataStoreStore = ctx.Services.GetRequiredService<IDataStoreStore>();
            var ds = await dataStoreStore.GetAsync(row.SourceId, ct);
            source = new
            {
                kind = "datastore",
                dataStoreId = row.SourceId,
                dataStoreName = ds?.Name,
                dataStoreKind = ds is null ? null : ((DataStoreKind)ds.Kind).ToString(),
                tableName = row.SourceTableName
            };
        }
        else
        {
            source = new
            {
                kind = row.SourceKind,
                connectorId = row.SourceId,
                tableName = row.SourceTableName
            };
        }

        return JsonSerializer.SerializeToElement(new
        {
            kind = "dataset_source",
            source = "IDatasetStore + IDataStoreStore",
            data = new
            {
                datasetId = row.Id,
                datasetName = row.Name,
                mode = ((DatasetMode)row.Mode).ToString(),
                columns = DatasetSchemaCodec.Decode(row.ColumnSchemaJson),
                source
            }
        });
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

    private static JsonElement Error(string source, string message) =>
        JsonSerializer.SerializeToElement(new { kind = "error", source, data = new { message } });

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
