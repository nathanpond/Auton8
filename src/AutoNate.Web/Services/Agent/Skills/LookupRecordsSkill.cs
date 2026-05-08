using System.Text.Json;
using AutoNate.Web.Services.Records;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Read-only record diagnostics. list_record_types returns the typed
// catalog so the model can pick a short code; search_records and get_record
// fetch concrete rows. All three flow through IRecordStore / IRecordTypeStore,
// which already gate by IAuthorizer when authorization is enabled.
public sealed class LookupRecordsSkill : IAgentSkill
{
    public string Name => "lookup-records";

    public string Description => "List record types, search records by type, and fetch a single record by key.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public LookupRecordsSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "list_record_types",
                Description: "List the record types defined in the system. Each carries a short code that other tools accept.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "includeArchived": { "type": "boolean", "description": "Include archived types. Defaults to false." }
                      },
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListRecordTypesAsync),

            new AgentTool(
                Name: "search_records",
                Description: "Search records of a given type by free text. Returns up to 25 matches.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "typeShortCode": { "type": "string", "description": "Short code of the record type to search." },
                        "query": { "type": "string", "description": "Optional free text. Empty = first 25 by recent." },
                        "take": { "type": "integer", "minimum": 1, "maximum": 50, "description": "Max rows to return." }
                      },
                      "required": ["typeShortCode"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeSearchRecordsAsync),

            new AgentTool(
                Name: "get_record",
                Description: "Fetch one record by its stable key (e.g. ACC-101).",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "key": { "type": "string", "description": "Record key, e.g. INC-12 or ACC-101." }
                      },
                      "required": ["key"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeGetRecordAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "When the user asks about records, call list_record_types first if you don't know the short code. Then use search_records to narrow, and get_record by key for details.";

    private static async Task<JsonElement> InvokeListRecordTypesAsync(
        JsonElement args,
        AgentToolContext context,
        CancellationToken ct)
    {
        var includeArchived = args.TryGetProperty("includeArchived", out var ia) && ia.ValueKind == JsonValueKind.True;
        var store = context.Services.GetRequiredService<IRecordTypeStore>();
        var types = await store.ListAsync(includeArchived, ct);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "record_types",
            source = "IRecordTypeStore",
            data = types.Select(t => new
            {
                id = t.Id,
                shortCode = t.ShortCode,
                name = t.Name,
                description = t.Description,
                isArchived = t.IsArchived
            }).ToArray()
        });
    }

    private static async Task<JsonElement> InvokeSearchRecordsAsync(
        JsonElement args,
        AgentToolContext context,
        CancellationToken ct)
    {
        if (!args.TryGetProperty("typeShortCode", out var sc) || sc.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(sc.GetString()))
        {
            return ErrorElement("search_records", "typeShortCode is required.");
        }

        var typeStore = context.Services.GetRequiredService<IRecordTypeStore>();
        var type = await typeStore.GetByShortCodeAsync(sc.GetString()!, ct);
        if (type is null)
        {
            return ErrorElement("search_records", $"No record type with short code '{sc.GetString()}'.");
        }

        var take = args.TryGetProperty("take", out var t) && t.ValueKind == JsonValueKind.Number
            ? Math.Clamp(t.GetInt32(), 1, 50)
            : 25;

        var recordStore = context.Services.GetRequiredService<IRecordStore>();
        var input = new RecordSearchInput(
            RecordTypeId: type.Id,
            Filters: null,
            AssigneeId: null,
            IncludeArchived: false,
            Page: 0,
            PageSize: take,
            Sort: null);
        var page = await recordStore.SearchAsync(input, ct);

        return JsonSerializer.SerializeToElement(new
        {
            kind = "record_search_results",
            source = "IRecordStore",
            data = new
            {
                typeShortCode = type.ShortCode,
                totalCount = page.TotalCount,
                items = page.Records.Select(r => new
                {
                    id = r.Id,
                    key = r.Key,
                    name = r.Name,
                    status = r.Status,
                    updatedAtUtc = r.UpdatedAtUtc
                }).ToArray()
            }
        });
    }

    private static async Task<JsonElement> InvokeGetRecordAsync(
        JsonElement args,
        AgentToolContext context,
        CancellationToken ct)
    {
        if (!args.TryGetProperty("key", out var k) || k.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(k.GetString()))
        {
            return ErrorElement("get_record", "key is required.");
        }

        var store = context.Services.GetRequiredService<IRecordStore>();
        var record = await store.GetByKeyAsync(k.GetString()!, ct);
        if (record is null)
        {
            return ErrorElement("get_record", $"No record with key '{k.GetString()}' visible.");
        }

        return JsonSerializer.SerializeToElement(new
        {
            kind = "record",
            source = "IRecordStore",
            data = new
            {
                id = record.Id,
                key = record.Key,
                name = record.Name,
                status = record.Status,
                values = record.Values,
                createdAtUtc = record.CreatedAtUtc,
                updatedAtUtc = record.UpdatedAtUtc
            }
        });
    }

    private static JsonElement ErrorElement(string source, string message) =>
        JsonSerializer.SerializeToElement(new { kind = "error", source, data = new { message } });

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
