using System.Globalization;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.DataStores;
using AutoNate.Web.Services.DataStores.File;
using AutoNate.Web.Services.DataStores.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AutoNate.Web.Services.Agent.Skills;

// Read-only data-store inspection — mirrors the GET endpoints under
// /api/datastores so the chatbot can answer "what stores do I have", "what
// tables are in store X", "what files live under folder Y". Per-store View
// grants are enforced via the same FilterQueryAsync path the endpoints use,
// so an actor with no datastore grants sees an empty list.
public sealed class LookupDataStoresSkill : IAgentSkill
{
    public string Name => "lookup-data-stores";

    public string Description =>
        "List data stores, inspect ingested SQL tables, and browse stored files.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public LookupDataStoresSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "list_data_stores",
                Description: "List data stores visible to the current user. Optional `kind` filters to FileType or SqlType; `search` filters by name (case-insensitive substring); `take` caps the result.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "kind": { "type": ["string", "null"], "enum": ["FileType", "SqlType", null] },
                        "search": { "type": ["string", "null"] },
                        "take": { "type": ["integer", "null"], "minimum": 1, "maximum": 200 }
                      },
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListAsync),

            new AgentTool(
                Name: "get_data_store",
                Description: "Fetch one data store by id.",
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
                Name: "list_data_store_tables",
                Description: "List ingested SQL tables in a SqlType data store. Returns id, schema/table name, row count, and the inferred column schema. FileType stores return an empty list.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": { "dataStoreId": { "type": "string" } },
                      "required": ["dataStoreId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListTablesAsync),

            new AgentTool(
                Name: "preview_data_store_table",
                Description: "Top-N row sample of an ingested SQL table. `take` defaults to 30, capped at 200.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "tableId": { "type": "string" },
                        "take": { "type": ["integer", "null"], "minimum": 1, "maximum": 200 }
                      },
                      "required": ["dataStoreId", "tableId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokePreviewTableAsync),

            new AgentTool(
                Name: "list_data_store_files",
                Description: "List folders and files in a FileType data store at the given `folder` path (POSIX-style, defaults to '/').",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "folder": { "type": ["string", "null"] }
                      },
                      "required": ["dataStoreId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListFilesAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Data stores have two kinds: FileType (folders + files) and SqlType (ingested CSV tables). Per-store View grants filter what shows up — empty list means the user has no grants. Use list_data_stores to find ids, then list_data_store_tables / preview_data_store_table for SqlType inspection or list_data_store_files for FileType browsing.";

    private static async Task<JsonElement> InvokeListAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        var kindFilter = ReadString(args, "kind");
        var search = ReadString(args, "search");
        var take = ReadInt(args, "take") ?? 50;
        if (take <= 0) take = 50;
        if (take > 200) take = 200;

        DataStoreKind? kind = null;
        if (!string.IsNullOrWhiteSpace(kindFilter))
        {
            if (!Enum.TryParse<DataStoreKind>(kindFilter, ignoreCase: true, out var parsed))
                return Error("list_data_stores", $"Unknown data store kind '{kindFilter}'. Use 'FileType' or 'SqlType'.");
            kind = parsed;
        }

        var dbFactory = ctx.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        IQueryable<DataStore> query = db.DataStores.AsNoTracking().OrderBy(d => d.Name);
        if (kind is { } k) query = query.Where(d => d.Kind == (short)k);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(d => EF.Functions.ILike(d.Name, "%" + s + "%"));
        }
        var visible = await authorizer.FilterQueryAsync(
            db, ctx.Session.User, EntityKinds.DataStore, Actions.View, query, ct);
        var rows = await visible.Take(take).ToListAsync(ct);

        var data = rows.Select(d => new
        {
            id = d.Id,
            name = d.Name,
            description = d.Description,
            kind = ((DataStoreKind)d.Kind).ToString(),
            ownerUserId = d.OwnerUserId,
            createdAtUtc = d.CreatedAtUtc,
            updatedAtUtc = d.UpdatedAtUtc
        }).ToList();

        return JsonSerializer.SerializeToElement(new
        {
            kind = "data_stores",
            source = "IDataStoreStore",
            data
        });
    }

    private static async Task<JsonElement> InvokeGetAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        if (!TryReadGuid(args, "id", out var id))
            return Error("get_data_store", "id is required and must be a GUID.");
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.DataStore, id.ToString()), ct);
        if (!decision.IsAllowed)
            return Error("get_data_store", $"View permission denied on data store {id}.");
        var store = ctx.Services.GetRequiredService<IDataStoreStore>();
        var row = await store.GetAsync(id, ct);
        if (row is null) return Error("get_data_store", $"Data store {id} not found.");
        return JsonSerializer.SerializeToElement(new
        {
            kind = "data_store",
            source = "IDataStoreStore",
            data = new
            {
                id = row.Id,
                name = row.Name,
                description = row.Description,
                kind = ((DataStoreKind)row.Kind).ToString(),
                ownerUserId = row.OwnerUserId,
                createdAtUtc = row.CreatedAtUtc,
                updatedAtUtc = row.UpdatedAtUtc
            }
        });
    }

    private static async Task<JsonElement> InvokeListTablesAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return Error("list_data_store_tables", "dataStoreId is required and must be a GUID.");
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return Error("list_data_store_tables", $"View permission denied on data store {dataStoreId}.");

        var dbFactory = ctx.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.DataStoreTables
            .AsNoTracking()
            .Where(t => t.DataStoreId == dataStoreId)
            .OrderBy(t => t.TableName)
            .ToListAsync(ct);

        var data = rows.Select(r =>
        {
            List<CsvColumn> columns;
            try
            {
                columns = JsonSerializer.Deserialize<List<CsvColumn>>(
                    r.ColumnSchemaJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? new List<CsvColumn>();
            }
            catch (JsonException)
            {
                columns = new List<CsvColumn>();
            }
            return new
            {
                id = r.Id,
                schemaName = r.SchemaName,
                tableName = r.TableName,
                rowCount = r.RowCount,
                columns
            };
        }).ToList();

        return JsonSerializer.SerializeToElement(new
        {
            kind = "data_store_tables",
            source = "DataStoreTables",
            data
        });
    }

    private static async Task<JsonElement> InvokePreviewTableAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return Error("preview_data_store_table", "dataStoreId is required.");
        if (!TryReadGuid(args, "tableId", out var tableId))
            return Error("preview_data_store_table", "tableId is required.");
        var take = ReadInt(args, "take") ?? 30;
        if (take <= 0) take = 30;
        if (take > 200) take = 200;

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return Error("preview_data_store_table", $"View permission denied on data store {dataStoreId}.");

        var connectionFactory = ctx.Services.GetRequiredService<IDatastoresConnectionFactory>();
        if (!connectionFactory.IsEnabled)
            return Error("preview_data_store_table", "Datastores database is not configured.");

        var dbFactory = ctx.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.DataStoreTables
            .AsNoTracking()
            .Where(t => t.Id == tableId && t.DataStoreId == dataStoreId)
            .SingleOrDefaultAsync(ct);
        if (row is null) return Error("preview_data_store_table", $"Table {tableId} not found in data store {dataStoreId}.");

        var quotedSchema = "\"" + row.SchemaName.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        var quotedTable = "\"" + row.TableName.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        var sql = $"SELECT * FROM {quotedSchema}.{quotedTable} LIMIT {take}";

        try
        {
            await using var conn = await connectionFactory.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            var columns = new List<object>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(new
                {
                    name = reader.GetName(i),
                    postgresType = reader.GetDataTypeName(i)
                });
            }

            var rows = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync(ct))
            {
                var dict = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    dict[reader.GetName(i)] = await reader.IsDBNullAsync(i, ct)
                        ? null
                        : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                }
                rows.Add(dict);
            }

            return JsonSerializer.SerializeToElement(new
            {
                kind = "data_store_table_preview",
                source = "autonate_datastores",
                data = new
                {
                    schemaName = row.SchemaName,
                    tableName = row.TableName,
                    totalRowCount = row.RowCount,
                    columns,
                    rows
                }
            });
        }
        catch (PostgresException ex)
        {
            return Error("preview_data_store_table", $"Postgres {ex.SqlState}: {ex.MessageText}");
        }
    }

    private static async Task<JsonElement> InvokeListFilesAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return Error("list_data_store_files", "dataStoreId is required.");
        var folder = ReadString(args, "folder") ?? "/";
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return Error("list_data_store_files", $"View permission denied on data store {dataStoreId}.");

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        try
        {
            var listing = await files.ListAsync(dataStoreId, folder, ct);
            return JsonSerializer.SerializeToElement(new
            {
                kind = "data_store_listing",
                source = "IFileDataStoreService",
                data = new
                {
                    folder,
                    folders = listing.Folders.Select(f => new { path = f.FolderPath }),
                    files = listing.Files.Select(f => new
                    {
                        id = f.Id,
                        folderPath = f.FolderPath,
                        filename = f.Filename,
                        sizeBytes = f.SizeBytes,
                        contentType = f.ContentType,
                        uploadedAtUtc = f.UploadedAtUtc
                    })
                }
            });
        }
        catch (FileDataStoreNotFoundException)
        {
            return Error("list_data_store_files", $"Data store {dataStoreId} is not a FileType store or does not exist.");
        }
        catch (ArgumentException ex)
        {
            return Error("list_data_store_files", ex.Message);
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

    private static int? ReadInt(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : null;

    private static JsonElement Error(string source, string message) =>
        JsonSerializer.SerializeToElement(new { kind = "error", source, data = new { message } });

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
