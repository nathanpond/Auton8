using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Agent.Skills.Internal;
using AutoNate.Web.Services.DataStores;
using AutoNate.Web.Services.DataStores.File;
using AutoNate.Web.Services.DataStores.Sql;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Confirm-gated mutations for the data-store domain — create / update / delete
// stores plus folder + file metadata operations on FileType stores. CSV ingest
// is deliberately excluded: it needs multipart file upload that doesn't
// translate to a JSON tool call. Users still ingest CSVs via the SPA's
// detail page; the chatbot can guide them there.
public sealed class ManageDataStoresSkill : IAgentSkill
{
    public string Name => "manage-data-stores";

    public string Description =>
        "Create / update / delete data stores and manage folders and files inside FileType stores.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public ManageDataStoresSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "create_data_store",
                Description: "Create a new data store. `kind` is 'FileType' (folders + files) or 'SqlType' (CSV-ingested tables). Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "name": { "type": "string" },
                        "description": { "type": ["string", "null"] },
                        "kind": { "type": "string", "enum": ["FileType", "SqlType"] },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["name", "kind"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeCreateAsync),

            new AgentTool(
                Name: "update_data_store",
                Description: "Rename or re-describe an existing data store. Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "id": { "type": "string" },
                        "name": { "type": ["string", "null"] },
                        "description": { "type": ["string", "null"] },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeUpdateAsync),

            new AgentTool(
                Name: "delete_data_store",
                Description: "Delete a data store. For SqlType stores this drops the per-store schema and role; for FileType stores the file metadata rows go too (the on-disk bytes are swept by background cleanup). Irreversible; confirm-gated.",
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
                Name: "create_data_store_folder",
                Description: "Create an empty folder in a FileType data store. Folder paths are POSIX-style with a leading '/'. Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "folderPath": { "type": "string" },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["dataStoreId", "folderPath"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeCreateFolderAsync),

            new AgentTool(
                Name: "delete_data_store_folder",
                Description: "Delete a folder and every file under it in a FileType data store. Irreversible; confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "folderPath": { "type": "string" },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["dataStoreId", "folderPath"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeDeleteFolderAsync),

            new AgentTool(
                Name: "delete_data_store_file",
                Description: "Delete a single file from a FileType data store by its file id. Irreversible; confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "fileId": { "type": "string" },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["dataStoreId", "fileId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeDeleteFileAsync),

            new AgentTool(
                Name: "rename_or_move_data_store_file",
                Description: "Rename or move a single file. At least one of newFolderPath or newFilename must be set. Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "fileId": { "type": "string" },
                        "newFolderPath": { "type": ["string", "null"] },
                        "newFilename": { "type": ["string", "null"] },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["dataStoreId", "fileId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeRenameFileAsync),

            new AgentTool(
                Name: "create_data_store_text_file",
                Description:
                    "Create a new UTF-8 text file in a FileType data store with the supplied `content`. " +
                    "Use for small derived artifacts the agent produces (analysis notes, README files, small derived CSV summaries). " +
                    "For unpivoting a wide table into a long-format CSV use unpivot_data_store_file_to_csv instead — it streams " +
                    "without holding the result in memory. `content` is capped at 5MB; pass larger payloads via the SPA upload UI. " +
                    "Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "folderPath": { "type": ["string", "null"], "description": "Destination folder, POSIX-style. Default '/'." },
                        "filename": { "type": "string" },
                        "content": { "type": "string" },
                        "contentType": { "type": ["string", "null"], "description": "MIME type. Auto-derived from filename extension when null." },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["dataStoreId", "filename", "content"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeCreateTextFileAsync),

            new AgentTool(
                Name: "unpivot_data_store_file_to_csv",
                Description:
                    "Read a wide-pivot source file (CSV or XLSX) from a FileType data store, transform it into a long-format CSV " +
                    "(one row per (key, entity, value) triple), and save the result as a new CSV in the same data store. " +
                    "Use this after profile_data_store_*_file identifies a wide-pivot layout and the user agrees to convert. " +
                    "Streams source-to-destination through a temp file — safe on multi-GB wide tables with millions of output rows. " +
                    "Confirm-gated: the proposal includes the destination path and a row-count estimate so the user can confirm scope. " +
                    "`keyColumns` lists the source column names that identify each row (typically a date or id column); these become the leftmost columns in the output. " +
                    "All OTHER source columns are unpivoted: their values become rows, with the column name written into the `entityColumnName` column (default 'entity') " +
                    "and the cell value written into the `valueColumnName` column (default 'value'). " +
                    "For XLSX, set `sheetName` or `sheetIndex`, `headerRow` (default 1) for the column names, and `descriptionRow` (optional) for a banner row above the headers " +
                    "carrying longer-form labels that should be included as a per-output-row `description` column when `includeDescription: true`. " +
                    "`skipMissingValues: true` (the default) drops output rows where the source cell is empty / null / 'NA' / 'NaN' / '?' / '-' so the output isn't padded with empty rows. " +
                    "Source format auto-detects from filename extension when `sourceFormat` is omitted.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "sourceFileId": { "type": "string" },
                        "sourceFormat": { "type": ["string", "null"], "enum": ["csv", "xlsx", null] },
                        "delimiter": { "type": ["string", "null"], "minLength": 1, "maxLength": 4 },
                        "sheetName": { "type": ["string", "null"] },
                        "sheetIndex": { "type": ["integer", "null"], "minimum": 1 },
                        "headerRow": { "type": ["integer", "null"], "minimum": 1 },
                        "descriptionRow": { "type": ["integer", "null"], "minimum": 1 },
                        "dataStartRow": { "type": ["integer", "null"], "minimum": 1, "description": "XLSX: first data row (default headerRow + 1)." },
                        "keyColumns": {
                          "type": "array",
                          "items": { "type": "string" },
                          "minItems": 1,
                          "description": "Source column names that identify each row (becomes leftmost columns in output). Case-insensitive."
                        },
                        "entityColumnName": { "type": ["string", "null"] },
                        "valueColumnName": { "type": ["string", "null"] },
                        "includeDescription": { "type": ["boolean", "null"] },
                        "skipMissingValues": { "type": ["boolean", "null"] },
                        "destinationFolder": { "type": ["string", "null"], "description": "Default: same folder as the source file." },
                        "destinationFilename": { "type": "string" },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["dataStoreId", "sourceFileId", "keyColumns", "destinationFilename"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeUnpivotToCsvAsync),

            new AgentTool(
                Name: "ingest_data_store_csv_to_sql_table",
                Description:
                    "Read a CSV file that already lives in a FileType data store and ingest it as a new SQL table in a SqlType data store. " +
                    "Streams server-side — does NOT require the user to download and re-upload the CSV. " +
                    "Use this to chain after unpivot_data_store_file_to_csv: " +
                    "(1) unpivot the wide source into a long-format CSV inside the FileType store, " +
                    "(2) create a SqlType data store via create_data_store with kind: 'SqlType', " +
                    "(3) call this tool to ingest the CSV into a table in that store, " +
                    "(4) call manage-datasets.create_dataset to expose the table as a queryable Dataset. " +
                    "If `columns` is omitted the tool runs CsvIngestor.PreviewAsync on the source to infer column types from a ~200-row sample, " +
                    "and returns the inferred schema in the proposal so the user can review before committing. " +
                    "`mode`: 'insert' (default, errors if the table already exists), 'replace' (drops + recreates), 'append' (adds rows when the schema matches). " +
                    "Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "sourceDataStoreId": { "type": "string", "description": "FileType data store containing the source CSV." },
                        "sourceFileId": { "type": "string" },
                        "destinationDataStoreId": { "type": "string", "description": "SqlType data store where the table will land." },
                        "tableName": { "type": "string" },
                        "columns": {
                          "type": ["array", "null"],
                          "description": "Optional explicit column schema. Each item: { name, postgresType }. Omit to auto-infer from a CSV preview.",
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
                        "mode": { "type": ["string", "null"], "enum": ["insert", "append", "replace", null] },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["sourceDataStoreId", "sourceFileId", "destinationDataStoreId", "tableName"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeIngestCsvToSqlTableAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Data store mutations are confirm-gated. CSV ingest (POST /tables) needs a binary upload — direct the user to the SPA detail page rather than attempting it from chat. " +
        "FileType folder paths are POSIX-style with a leading '/' (e.g. '/raw/2026/'). Use lookup-data-stores tools first to find ids. " +
        "FILE CREATION: agents can write small text files via create_data_store_text_file (capped at 5MB) or unpivot a wide source file into a long-format CSV via unpivot_data_store_file_to_csv. " +
        "After profile_data_store_*_file identifies a wide-pivot layout and the user agrees to the unpivot recommendation, call unpivot_data_store_file_to_csv with `keyColumns` set to the key axis columns the profile flagged (usually the date column). The tool streams the conversion and saves the result alongside the source. " +
        "INGEST FROM ONE DATASTORE TO ANOTHER: when a CSV already lives in a FileType data store and the user wants it queryable via AQL, do NOT ask them to download/re-upload — call ingest_data_store_csv_to_sql_table, which streams the source file directly into a SqlType store's table. Typical chain after unpivot: create_data_store(kind:'SqlType') → ingest_data_store_csv_to_sql_table → manage-datasets.create_dataset.";

    private static async Task<JsonElement> InvokeCreateAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "create_data_store";
        var name = ReadString(args, "name");
        var description = ReadString(args, "description");
        var kindRaw = ReadString(args, "kind");
        if (string.IsNullOrWhiteSpace(name)) return ConfirmGate.Rejected(action, "name is required.");
        if (string.IsNullOrWhiteSpace(kindRaw)) return ConfirmGate.Rejected(action, "kind is required.");
        if (!Enum.TryParse<DataStoreKind>(kindRaw, ignoreCase: true, out var kind))
            return ConfirmGate.Rejected(action, $"Unknown data store kind '{kindRaw}'. Use 'FileType' or 'SqlType'.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Create, new EntityRef(EntityKinds.DataStore, string.Empty), ct);
        if (!decision.IsAllowed)
            return ConfirmGate.Rejected(action, "DataStore:Create permission required.");

        var preview = new { name = name.Trim(), description, kind = kind.ToString() };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("data_store_create_proposal", action, preview);

        if (kind == DataStoreKind.SqlType)
        {
            var provisioner = ctx.Services.GetRequiredService<SqlDataStoreProvisioner>();
            if (!provisioner.IsEnabled)
                return ConfirmGate.Failed("data_store_create_failed", action, "SqlType stores require the datastores Postgres to be configured.");
        }

        var store = ctx.Services.GetRequiredService<IDataStoreStore>();
        try
        {
            var row = await store.CreateAsync(
                new CreateDataStoreInput(name.Trim(), description?.Trim(), kind),
                ctx.Session.UserId, ct);
            if (kind == DataStoreKind.SqlType)
            {
                var provisioner = ctx.Services.GetRequiredService<SqlDataStoreProvisioner>();
                await provisioner.ProvisionAsync(row.Id, ct);
            }
            return ConfirmGate.Committed("data_store_create_committed", action, new
            {
                id = row.Id,
                name = row.Name,
                kind = ((DataStoreKind)row.Kind).ToString()
            });
        }
        catch (DataStoreNameConflictException ex)
        {
            return ConfirmGate.Failed("data_store_create_failed", action, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return ConfirmGate.Failed("data_store_create_failed", action, ex.Message);
        }
    }

    private static async Task<JsonElement> InvokeUpdateAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "update_data_store";
        if (!TryReadGuid(args, "id", out var id))
            return ConfirmGate.Rejected(action, "id is required and must be a GUID.");
        var name = ReadString(args, "name");
        var description = ReadString(args, "description");
        if (name is null && description is null)
            return ConfirmGate.Rejected(action, "At least one of name or description must be set.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Edit, new EntityRef(EntityKinds.DataStore, id.ToString()), ct);
        if (!decision.IsAllowed)
            return ConfirmGate.Rejected(action, $"DataStore:Edit permission denied on {id}.");

        var store = ctx.Services.GetRequiredService<IDataStoreStore>();
        var existing = await store.GetAsync(id, ct);
        if (existing is null) return ConfirmGate.Rejected(action, $"Data store {id} not found.");

        var preview = new { id, before = new { existing.Name, existing.Description }, patch = new { name, description } };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("data_store_update_proposal", action, preview);

        try
        {
            var row = await store.UpdateAsync(
                id, new UpdateDataStoreInput(name?.Trim(), description?.Trim()), ctx.Session.UserId, ct);
            return ConfirmGate.Committed("data_store_update_committed", action, new
            {
                id = row.Id,
                name = row.Name,
                description = row.Description
            });
        }
        catch (DataStoreNotFoundException)
        {
            return ConfirmGate.Failed("data_store_update_failed", action, $"Data store {id} not found.");
        }
        catch (DataStoreNameConflictException ex)
        {
            return ConfirmGate.Failed("data_store_update_failed", action, ex.Message);
        }
    }

    private static async Task<JsonElement> InvokeDeleteAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "delete_data_store";
        if (!TryReadGuid(args, "id", out var id))
            return ConfirmGate.Rejected(action, "id is required and must be a GUID.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Delete, new EntityRef(EntityKinds.DataStore, id.ToString()), ct);
        if (!decision.IsAllowed)
            return ConfirmGate.Rejected(action, $"DataStore:Delete permission denied on {id}.");

        var store = ctx.Services.GetRequiredService<IDataStoreStore>();
        var existing = await store.GetAsync(id, ct);
        if (existing is null) return ConfirmGate.Rejected(action, $"Data store {id} not found.");

        var preview = new
        {
            id,
            existing.Name,
            kind = ((DataStoreKind)existing.Kind).ToString(),
            warning = "Irreversible. SqlType deletes drop the per-store schema and every ingested table; FileType deletes drop every file metadata row."
        };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("data_store_delete_proposal", action, preview);

        var provisioner = ctx.Services.GetRequiredService<SqlDataStoreProvisioner>();
        await provisioner.DeprovisionAsync(id, ct);
        var deleted = await store.DeleteAsync(id, ct);
        if (!deleted)
            return ConfirmGate.Failed("data_store_delete_failed", action, $"Data store {id} not found.");
        return ConfirmGate.Committed("data_store_delete_committed", action, new { id, name = existing.Name });
    }

    private static async Task<JsonElement> InvokeCreateFolderAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "create_data_store_folder";
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return ConfirmGate.Rejected(action, "dataStoreId is required.");
        var folderPath = ReadString(args, "folderPath");
        if (string.IsNullOrWhiteSpace(folderPath))
            return ConfirmGate.Rejected(action, "folderPath is required.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Edit, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return ConfirmGate.Rejected(action, $"DataStore:Edit permission denied on {dataStoreId}.");

        var preview = new { dataStoreId, folderPath };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("data_store_folder_create_proposal", action, preview);

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        try
        {
            await files.CreateFolderAsync(dataStoreId, folderPath, ct);
            return ConfirmGate.Committed("data_store_folder_create_committed", action, preview);
        }
        catch (FileDataStoreNotFoundException)
        {
            return ConfirmGate.Failed("data_store_folder_create_failed", action, $"Data store {dataStoreId} is not a FileType store or does not exist.");
        }
        catch (ArgumentException ex)
        {
            return ConfirmGate.Failed("data_store_folder_create_failed", action, ex.Message);
        }
    }

    private static async Task<JsonElement> InvokeDeleteFolderAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "delete_data_store_folder";
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return ConfirmGate.Rejected(action, "dataStoreId is required.");
        var folderPath = ReadString(args, "folderPath");
        if (string.IsNullOrWhiteSpace(folderPath))
            return ConfirmGate.Rejected(action, "folderPath is required.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Edit, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return ConfirmGate.Rejected(action, $"DataStore:Edit permission denied on {dataStoreId}.");

        var preview = new { dataStoreId, folderPath, warning = "Deletes every file under this prefix. Irreversible." };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("data_store_folder_delete_proposal", action, preview);

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        try
        {
            var deletedCount = await files.DeleteFolderAsync(dataStoreId, folderPath, ct);
            return ConfirmGate.Committed("data_store_folder_delete_committed", action, new { dataStoreId, folderPath, filesDeleted = deletedCount });
        }
        catch (FileDataStoreNotFoundException)
        {
            return ConfirmGate.Failed("data_store_folder_delete_failed", action, $"Data store {dataStoreId} is not a FileType store or does not exist.");
        }
        catch (InvalidOperationException ex)
        {
            return ConfirmGate.Failed("data_store_folder_delete_failed", action, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return ConfirmGate.Failed("data_store_folder_delete_failed", action, ex.Message);
        }
    }

    private static async Task<JsonElement> InvokeDeleteFileAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "delete_data_store_file";
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return ConfirmGate.Rejected(action, "dataStoreId is required.");
        if (!TryReadGuid(args, "fileId", out var fileId))
            return ConfirmGate.Rejected(action, "fileId is required.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Edit, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return ConfirmGate.Rejected(action, $"DataStore:Edit permission denied on {dataStoreId}.");

        var preview = new { dataStoreId, fileId, warning = "Irreversible." };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("data_store_file_delete_proposal", action, preview);

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        try
        {
            var deleted = await files.DeleteFileAsync(dataStoreId, fileId, ct);
            return ConfirmGate.Committed("data_store_file_delete_committed", action, new
            {
                dataStoreId,
                fileId = deleted.Id,
                folderPath = deleted.FolderPath,
                filename = deleted.Filename
            });
        }
        catch (FileDataStoreFileNotFoundException)
        {
            return ConfirmGate.Failed("data_store_file_delete_failed", action, $"File {fileId} not found in data store {dataStoreId}.");
        }
    }

    private static async Task<JsonElement> InvokeRenameFileAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "rename_or_move_data_store_file";
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return ConfirmGate.Rejected(action, "dataStoreId is required.");
        if (!TryReadGuid(args, "fileId", out var fileId))
            return ConfirmGate.Rejected(action, "fileId is required.");
        var newFolderPath = ReadString(args, "newFolderPath");
        var newFilename = ReadString(args, "newFilename");
        if (newFolderPath is null && newFilename is null)
            return ConfirmGate.Rejected(action, "At least one of newFolderPath or newFilename must be set.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Edit, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return ConfirmGate.Rejected(action, $"DataStore:Edit permission denied on {dataStoreId}.");

        var preview = new { dataStoreId, fileId, newFolderPath, newFilename };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("data_store_file_rename_proposal", action, preview);

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        try
        {
            var (prevFolder, prevFilename, entity) = await files.RenameOrMoveFileAsync(
                dataStoreId, fileId, newFolderPath, newFilename, ctx.Session.UserId, ct);
            return ConfirmGate.Committed("data_store_file_rename_committed", action, new
            {
                dataStoreId,
                fileId = entity.Id,
                previousFolderPath = prevFolder,
                previousFilename = prevFilename,
                folderPath = entity.FolderPath,
                filename = entity.Filename
            });
        }
        catch (FileDataStoreNotFoundException)
        {
            return ConfirmGate.Failed("data_store_file_rename_failed", action, $"Data store {dataStoreId} is not a FileType store or does not exist.");
        }
        catch (FileDataStoreFileNotFoundException)
        {
            return ConfirmGate.Failed("data_store_file_rename_failed", action, $"File {fileId} not found in data store {dataStoreId}.");
        }
        catch (FileDataStoreFilenameConflictException ex)
        {
            return ConfirmGate.Failed("data_store_file_rename_failed", action, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return ConfirmGate.Failed("data_store_file_rename_failed", action, ex.Message);
        }
    }

    // Hard cap on text-file content the agent can land via the inline create
    // tool. Larger payloads should go through the SPA upload UI or, for
    // wide→long conversions, through unpivot_data_store_file_to_csv (which
    // streams via temp file and never holds the payload in memory).
    private const int MaxInlineTextBytes = 5 * 1024 * 1024;

    // Same vocabulary as the profile / inspect tools — keeps the unpivot
    // tool's "skip missing values" behavior consistent with what the agent
    // already told the user about during profiling.
    private static readonly HashSet<string> MissingValueSentinels = new(StringComparer.OrdinalIgnoreCase)
    {
        "NA", "N/A", "n/a", "NaN", "null", "NULL", "(null)", "None", "missing", "?", "-", "--", "."
    };

    private static async Task<JsonElement> InvokeCreateTextFileAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "create_data_store_text_file";
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return ConfirmGate.Rejected(action, "dataStoreId is required.");
        var folderPath = ReadString(args, "folderPath");
        if (string.IsNullOrWhiteSpace(folderPath)) folderPath = "/";
        var filename = ReadString(args, "filename");
        if (string.IsNullOrWhiteSpace(filename))
            return ConfirmGate.Rejected(action, "filename is required.");
        var content = ReadString(args, "content");
        if (content is null)
            return ConfirmGate.Rejected(action, "content is required.");
        var contentType = ReadString(args, "contentType");
        if (string.IsNullOrWhiteSpace(contentType)) contentType = GuessContentTypeFromExtension(filename);

        // Encode the byte length up front so the confirm proposal can quote
        // the exact size — and so we can reject oversize content before we
        // ever touch storage.
        var bytes = Encoding.UTF8.GetBytes(content);
        if (bytes.Length > MaxInlineTextBytes)
            return ConfirmGate.Rejected(action,
                $"content is {bytes.Length:N0} bytes; create_data_store_text_file caps at {MaxInlineTextBytes:N0} bytes. " +
                "Use the SPA upload UI for larger files, or unpivot_data_store_file_to_csv for derived long-format conversions.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Edit, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return ConfirmGate.Rejected(action, $"DataStore:Edit permission denied on {dataStoreId}.");

        var preview = new
        {
            dataStoreId,
            folderPath,
            filename,
            contentType,
            sizeBytes = bytes.Length,
            contentPreview = content.Length > 200 ? content[..200] + "…" : content
        };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("data_store_text_file_create_proposal", action, preview);

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            var uploaded = await files.UploadAsync(
                dataStoreId, folderPath, filename, contentType, ms, ctx.Session.UserId, ct);
            return ConfirmGate.Committed("data_store_text_file_create_committed", action, new
            {
                dataStoreId,
                fileId = uploaded.Id,
                folderPath = uploaded.FolderPath,
                filename = uploaded.Filename,
                sizeBytes = uploaded.SizeBytes,
                contentType = uploaded.ContentType
            });
        }
        catch (FileDataStoreNotFoundException)
        {
            return ConfirmGate.Failed("data_store_text_file_create_failed", action,
                $"Data store {dataStoreId} is not a FileType store or does not exist.");
        }
        catch (FileDataStoreFilenameConflictException ex)
        {
            return ConfirmGate.Failed("data_store_text_file_create_failed", action, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return ConfirmGate.Failed("data_store_text_file_create_failed", action, ex.Message);
        }
    }

    private static async Task<JsonElement> InvokeUnpivotToCsvAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "unpivot_data_store_file_to_csv";
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return ConfirmGate.Rejected(action, "dataStoreId is required.");
        if (!TryReadGuid(args, "sourceFileId", out var sourceFileId))
            return ConfirmGate.Rejected(action, "sourceFileId is required.");
        var destinationFilename = ReadString(args, "destinationFilename");
        if (string.IsNullOrWhiteSpace(destinationFilename))
            return ConfirmGate.Rejected(action, "destinationFilename is required.");

        var keyColumns = ReadStringArray(args, "keyColumns");
        if (keyColumns is null || keyColumns.Count == 0)
            return ConfirmGate.Rejected(action, "keyColumns is required and must list at least one column name (e.g. [\"date\"]).");

        var entityColumnName = ReadString(args, "entityColumnName");
        if (string.IsNullOrWhiteSpace(entityColumnName)) entityColumnName = "entity";
        var valueColumnName = ReadString(args, "valueColumnName");
        if (string.IsNullOrWhiteSpace(valueColumnName)) valueColumnName = "value";
        var includeDescription = args.TryGetProperty("includeDescription", out var idv) && idv.ValueKind == JsonValueKind.True;
        var skipMissingValues = !args.TryGetProperty("skipMissingValues", out var smv) || smv.ValueKind != JsonValueKind.False;
        var delimiter = ReadString(args, "delimiter");
        if (string.IsNullOrEmpty(delimiter)) delimiter = ",";
        var destinationFolder = ReadString(args, "destinationFolder");
        var sourceFormat = ReadString(args, "sourceFormat")?.ToLowerInvariant();
        // XLSX-specific args
        var sheetName = ReadString(args, "sheetName");
        var sheetIndex = ReadInt(args, "sheetIndex");
        var headerRow = ReadInt(args, "headerRow") ?? 1;
        var descriptionRow = ReadInt(args, "descriptionRow");
        var dataStartRow = ReadInt(args, "dataStartRow") ?? (headerRow + 1);
        if (headerRow <= 0) headerRow = 1;
        if (descriptionRow is <= 0) descriptionRow = null;
        if (dataStartRow <= headerRow) dataStartRow = headerRow + 1;

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Edit, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return ConfirmGate.Rejected(action, $"DataStore:Edit permission denied on {dataStoreId}.");

        // Look up the source file once to get filename / folder for the
        // proposal AND to choose the source format when the caller didn't.
        var dbFactory = ctx.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sourceMeta = await db.DataStoreFiles.AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == sourceFileId && f.DataStoreId == dataStoreId, ct);
        if (sourceMeta is null)
            return ConfirmGate.Rejected(action, $"Source file {sourceFileId} not found in data store {dataStoreId}.");

        sourceFormat ??= DetectSourceFormat(sourceMeta.Filename);
        if (sourceFormat is not ("csv" or "xlsx"))
            return ConfirmGate.Rejected(action,
                $"Could not detect source format from filename '{sourceMeta.Filename}'. Pass `sourceFormat` explicitly.");

        var actualDestinationFolder = string.IsNullOrWhiteSpace(destinationFolder)
            ? sourceMeta.FolderPath
            : destinationFolder;

        var preview = new
        {
            dataStoreId,
            sourceFileId,
            sourceFilename = sourceMeta.Filename,
            sourceFolder = sourceMeta.FolderPath,
            sourceFormat,
            destinationFolder = actualDestinationFolder,
            destinationFilename,
            keyColumns,
            entityColumnName,
            valueColumnName,
            includeDescription,
            skipMissingValues,
            estimateNote = "Output rows ≈ source data rows × (totalColumns − keyColumns.length). Run profile_data_store_*_file first if the user wants a precise estimate."
        };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("unpivot_to_csv_proposal", action, preview);

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        Stream sourceStream;
        DataStoreFile sourceFileMeta;
        try
        {
            (sourceFileMeta, sourceStream) = await files.DownloadAsync(dataStoreId, sourceFileId, ct);
        }
        catch (FileDataStoreFileNotFoundException)
        {
            return ConfirmGate.Failed("unpivot_to_csv_failed", action, $"Source file {sourceFileId} not found.");
        }

        // Write the unpivoted CSV to a system temp file. We need to read it
        // back to feed UploadAsync (which captures Length while copying), so
        // we can't go memory-only without buffering the entire long table.
        var tempPath = Path.Combine(Path.GetTempPath(), $"autonate-unpivot-{Guid.NewGuid():N}.csv");
        var sw = Stopwatch.StartNew();
        long outputRows = 0;
        long skippedRows = 0;
        try
        {
            await using (sourceStream)
            await using (var outFile = File.Create(tempPath))
            await using (var outWriter = new StreamWriter(outFile, new UTF8Encoding(false)))
            {
                // CSV header — write the whole line as one async call so the
                // VSTHRD103 analyzer is happy. The big hot loop happens inside
                // the unpivot helpers, where sync StreamWriter.Write is the
                // intentional choice.
                var headerLine = new StringBuilder();
                headerLine.Append(string.Join(",", keyColumns.Select(CsvEscape)));
                headerLine.Append(',');
                headerLine.Append(CsvEscape(entityColumnName));
                if (includeDescription)
                {
                    headerLine.Append(',');
                    headerLine.Append(CsvEscape("description"));
                }
                headerLine.Append(',');
                headerLine.Append(CsvEscape(valueColumnName));
                headerLine.Append('\n');
                await outWriter.WriteAsync(headerLine.ToString().AsMemory(), ct);

                if (sourceFormat == "xlsx")
                {
                    (outputRows, skippedRows) = await UnpivotXlsxAsync(
                        sourceStream, outWriter, sheetName, sheetIndex,
                        headerRow, descriptionRow, dataStartRow,
                        keyColumns, includeDescription, skipMissingValues, ct);
                }
                else
                {
                    (outputRows, skippedRows) = await UnpivotCsvAsync(
                        sourceStream, outWriter, delimiter,
                        keyColumns, skipMissingValues, ct);
                }
            }

            await using var uploadStream = File.OpenRead(tempPath);
            var uploaded = await files.UploadAsync(
                dataStoreId, actualDestinationFolder, destinationFilename, "text/csv",
                uploadStream, ctx.Session.UserId, ct);
            sw.Stop();
            return ConfirmGate.Committed("unpivot_to_csv_committed", action, new
            {
                dataStoreId,
                sourceFileId,
                sourceFilename = sourceFileMeta.Filename,
                destinationFileId = uploaded.Id,
                destinationFolder = uploaded.FolderPath,
                destinationFilename = uploaded.Filename,
                destinationSizeBytes = uploaded.SizeBytes,
                sourceFormat,
                outputRowCount = outputRows,
                skippedRowCount = skippedRows,
                elapsedMs = sw.ElapsedMilliseconds
            });
        }
        catch (FileDataStoreFilenameConflictException ex)
        {
            return ConfirmGate.Failed("unpivot_to_csv_failed", action,
                $"A file named '{destinationFilename}' already exists in '{actualDestinationFolder}'. Pick a different name or delete the existing file first. ({ex.Message})");
        }
        catch (ArgumentException ex)
        {
            return ConfirmGate.Failed("unpivot_to_csv_failed", action, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ConfirmGate.Failed("unpivot_to_csv_failed", action, "Unpivot failed: " + ex.Message);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch { /* best-effort cleanup; the system tmp sweep will catch any stragglers */ }
            }
        }
    }

    // Sync StreamWriter.Write inside the unpivot loops is intentional —
    // StreamWriter buffers internally and flushes to the underlying FileStream
    // in chunks, so per-cell awaits would add measurable overhead with no
    // throughput gain for a write-only sink. The VSTHRD103 analyzer doesn't
    // distinguish "blocking I/O" from "buffered append," so it's quieted for
    // these two methods.
#pragma warning disable VSTHRD103

    // Stream a CSV source through a row-by-row unpivot. Each source data row
    // emits one output row per non-key column whose value isn't a missing-
    // value sentinel (when skipMissing is true).
    private static async Task<(long output, long skipped)> UnpivotCsvAsync(
        Stream source,
        StreamWriter outWriter,
        string delimiter,
        IReadOnlyList<string> keyColumns,
        bool skipMissing,
        CancellationToken ct)
    {
        using var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null,
            Delimiter = delimiter
        };
        using var csv = new CsvReader(reader, csvConfig);
        if (!await csv.ReadAsync()) return (0, 0);
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? Array.Empty<string>();
        var keyLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++) keyLookup.TryAdd(headers[i], i);
        var keyIndices = new int[keyColumns.Count];
        for (int k = 0; k < keyColumns.Count; k++)
        {
            if (!keyLookup.TryGetValue(keyColumns[k], out var idx))
                throw new ArgumentException($"keyColumn '{keyColumns[k]}' not found in source headers.");
            keyIndices[k] = idx;
        }
        var keyIndexSet = new HashSet<int>(keyIndices);

        // Pre-escape non-key entity names once — they're written N times each.
        var entityEscaped = new string?[headers.Length];
        for (int c = 0; c < headers.Length; c++)
            if (!keyIndexSet.Contains(c)) entityEscaped[c] = CsvEscape(headers[c]);

        long outputRows = 0, skipped = 0;
        var keyBuf = new string?[keyColumns.Count];
        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            for (int k = 0; k < keyIndices.Length; k++)
            {
                try { keyBuf[k] = csv.GetField(keyIndices[k]); }
                catch (CsvHelperException) { keyBuf[k] = null; }
            }
            var keyCsv = string.Join(",", keyBuf.Select(CsvEscape));
            for (int c = 0; c < headers.Length; c++)
            {
                if (keyIndexSet.Contains(c)) continue;
                string? v = null;
                try { v = csv.GetField(c); }
                catch (CsvHelperException) { v = null; }
                if (IsMissing(v, skipMissing)) { skipped++; continue; }
                outWriter.Write(keyCsv);
                outWriter.Write(",");
                outWriter.Write(entityEscaped[c]);
                outWriter.Write(",");
                outWriter.Write(CsvEscape(v));
                outWriter.Write("\n");
                outputRows++;
            }
        }
        return (outputRows, skipped);
    }

    // Stream an XLSX source through a row-by-row unpivot. Loads the workbook
    // via ClosedXML (size cap enforced at the inspector layer; the unpivot
    // tool doesn't impose its own cap because the user is intentionally
    // converting a known wide file). Per-cell access via sheet.Cell is O(log n)
    // but adequate for the one-shot conversion use case.
    private static async Task<(long output, long skipped)> UnpivotXlsxAsync(
        Stream source,
        StreamWriter outWriter,
        string? sheetName,
        int? sheetIndex,
        int headerRow,
        int? descriptionRow,
        int dataStartRow,
        IReadOnlyList<string> keyColumns,
        bool includeDescription,
        bool skipMissing,
        CancellationToken ct)
    {
        using var wb = new XLWorkbook(source);
        IXLWorksheet sheet;
        if (!string.IsNullOrWhiteSpace(sheetName))
        {
            sheet = wb.Worksheets.FirstOrDefault(w => string.Equals(w.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Sheet '{sheetName}' not found.");
        }
        else if (sheetIndex.HasValue)
        {
            sheet = wb.Worksheets.Worksheet(sheetIndex.Value);
        }
        else
        {
            sheet = wb.Worksheets.FirstOrDefault()
                ?? throw new ArgumentException("Workbook has no sheets.");
        }

        var range = sheet.RangeUsed()
            ?? throw new ArgumentException("Sheet has no used range — nothing to unpivot.");
        var lastRow = range.LastRow().RowNumber();
        var lastCol = range.LastColumn().ColumnNumber();

        // Headers from headerRow; map keyColumns to indices.
        var headers = new string[lastCol];
        for (int c = 1; c <= lastCol; c++)
        {
            var raw = sheet.Cell(headerRow, c).GetString();
            headers[c - 1] = string.IsNullOrWhiteSpace(raw) ? $"col_{c}" : raw.Trim();
        }
        var keyLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++) keyLookup.TryAdd(headers[i], i);
        var keyIndices = new int[keyColumns.Count];
        for (int k = 0; k < keyColumns.Count; k++)
        {
            if (!keyLookup.TryGetValue(keyColumns[k], out var idx))
                throw new ArgumentException($"keyColumn '{keyColumns[k]}' not found in headerRow {headerRow}.");
            keyIndices[k] = idx;
        }
        var keyIndexSet = new HashSet<int>(keyIndices);

        // Pre-escape entity names + optional descriptions once.
        var entityEscaped = new string?[lastCol];
        var descEscaped = includeDescription ? new string?[lastCol] : null;
        for (int c = 0; c < lastCol; c++)
        {
            if (keyIndexSet.Contains(c)) continue;
            entityEscaped[c] = CsvEscape(headers[c]);
            if (descEscaped is not null)
            {
                var desc = descriptionRow.HasValue
                    ? sheet.Cell(descriptionRow.Value, c + 1).GetString()
                    : string.Empty;
                descEscaped[c] = CsvEscape(desc ?? string.Empty);
            }
        }

        long outputRows = 0, skipped = 0;
        var keyBuf = new string?[keyColumns.Count];
        for (int r = dataStartRow; r <= lastRow; r++)
        {
            ct.ThrowIfCancellationRequested();
            for (int k = 0; k < keyIndices.Length; k++)
                keyBuf[k] = XlsxCellToCanonicalString(sheet.Cell(r, keyIndices[k] + 1));
            var keyCsv = string.Join(",", keyBuf.Select(CsvEscape));
            for (int c = 0; c < lastCol; c++)
            {
                if (keyIndexSet.Contains(c)) continue;
                var val = XlsxCellToCanonicalString(sheet.Cell(r, c + 1));
                if (IsMissing(val, skipMissing)) { skipped++; continue; }
                outWriter.Write(keyCsv);
                outWriter.Write(",");
                outWriter.Write(entityEscaped[c]);
                if (descEscaped is not null)
                {
                    outWriter.Write(",");
                    outWriter.Write(descEscaped[c]);
                }
                outWriter.Write(",");
                outWriter.Write(CsvEscape(val));
                outWriter.Write("\n");
                outputRows++;
            }
        }
        return await Task.FromResult((outputRows, skipped));
    }
#pragma warning restore VSTHRD103

    private static async Task<JsonElement> InvokeIngestCsvToSqlTableAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "ingest_data_store_csv_to_sql_table";
        if (!TryReadGuid(args, "sourceDataStoreId", out var sourceDataStoreId))
            return ConfirmGate.Rejected(action, "sourceDataStoreId is required.");
        if (!TryReadGuid(args, "sourceFileId", out var sourceFileId))
            return ConfirmGate.Rejected(action, "sourceFileId is required.");
        if (!TryReadGuid(args, "destinationDataStoreId", out var destDataStoreId))
            return ConfirmGate.Rejected(action, "destinationDataStoreId is required.");
        var tableName = ReadString(args, "tableName");
        if (string.IsNullOrWhiteSpace(tableName))
            return ConfirmGate.Rejected(action, "tableName is required.");
        var modeStr = (ReadString(args, "mode") ?? "insert").ToLowerInvariant();
        CsvIngestMode mode = modeStr switch
        {
            "insert" => CsvIngestMode.Insert,
            "append" => CsvIngestMode.Append,
            "replace" => CsvIngestMode.Replace,
            _ => CsvIngestMode.Insert
        };
        if (modeStr is not ("insert" or "append" or "replace"))
            return ConfirmGate.Rejected(action, "mode must be 'insert', 'append', or 'replace'.");

        // Two-sided permission: View on the source (we're reading bytes from it)
        // and Edit on the destination (we're writing a table into it).
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var srcDecision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.DataStore, sourceDataStoreId.ToString()), ct);
        if (!srcDecision.IsAllowed)
            return ConfirmGate.Rejected(action, $"DataStore:View permission denied on source {sourceDataStoreId}.");
        var destDecision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Edit, new EntityRef(EntityKinds.DataStore, destDataStoreId.ToString()), ct);
        if (!destDecision.IsAllowed)
            return ConfirmGate.Rejected(action, $"DataStore:Edit permission denied on destination {destDataStoreId}.");

        var dbFactory = ctx.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var sourceMeta = await db.DataStoreFiles.AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == sourceFileId && f.DataStoreId == sourceDataStoreId, ct);
        if (sourceMeta is null)
            return ConfirmGate.Rejected(action, $"Source file {sourceFileId} not found in data store {sourceDataStoreId}.");

        var destStoreRow = await db.DataStores.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == destDataStoreId, ct);
        if (destStoreRow is null)
            return ConfirmGate.Rejected(action, $"Destination data store {destDataStoreId} not found.");
        if ((DataStoreKind)destStoreRow.Kind != DataStoreKind.SqlType)
            return ConfirmGate.Rejected(action,
                $"Destination '{destStoreRow.Name}' is {(DataStoreKind)destStoreRow.Kind}; ingest target must be SqlType. Create one with create_data_store(kind: 'SqlType') first.");

        var explicitColumns = ReadCsvColumnsFromArgs(args);
        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        var ingestor = ctx.Services.GetRequiredService<CsvIngestor>();

        // Column schema resolution: if the caller passed `columns`, trust them;
        // otherwise stream the first ~200 rows through CsvIngestor.PreviewAsync
        // to infer types. We re-preview on the committed call too (cheap; only
        // touches a small prefix) so the agent doesn't have to ferry inferred
        // columns through args.
        List<CsvColumn> columns;
        if (explicitColumns is not null)
        {
            columns = explicitColumns;
        }
        else
        {
            (_, var previewStream) = await files.DownloadAsync(sourceDataStoreId, sourceFileId, ct);
            await using (previewStream)
            {
                try
                {
                    var preview = await ingestor.PreviewAsync(previewStream, sourceMeta.Filename, ct);
                    columns = preview.Columns.ToList();
                }
                catch (InvalidOperationException ex)
                {
                    return ConfirmGate.Rejected(action, $"CSV preview failed: {ex.Message}");
                }
            }
        }

        var proposalPreview = new
        {
            sourceFile = sourceMeta.Filename,
            sourceFolder = sourceMeta.FolderPath,
            sourceSizeBytes = sourceMeta.SizeBytes,
            destinationStore = destStoreRow.Name,
            destinationStoreId = destDataStoreId,
            tableName,
            mode = mode.ToString().ToLowerInvariant(),
            columnCount = columns.Count,
            inferred = explicitColumns is null,
            // The proposal exposes a sample of the columns so the user can
            // sanity-check the inferred types without choking on a 1800-
            // column header list.
            sampleColumns = columns.Take(12).Select(c => new { c.Name, c.PostgresType }).ToList(),
            sampleColumnsTruncated = columns.Count > 12
        };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("ingest_csv_to_sql_proposal", action, proposalPreview);

        // Commit: re-download the source (fresh seekable stream) and feed it
        // to CsvIngestor — which uses Postgres COPY under the hood for the
        // bulk insert, so 7-million-row weather files are still seconds.
        try
        {
            (_, var dataStream) = await files.DownloadAsync(sourceDataStoreId, sourceFileId, ct);
            await using (dataStream)
            {
                var result = await ingestor.IngestAsync(
                    destDataStoreId, tableName!, columns, dataStream, ctx.Session.UserId, mode, ct);
                return ConfirmGate.Committed("ingest_csv_to_sql_committed", action, new
                {
                    destinationStoreId = destDataStoreId,
                    destinationStore = destStoreRow.Name,
                    tableId = result.TableId,
                    schemaName = result.SchemaName,
                    tableName = result.TableName,
                    rowsInserted = result.RowsInserted,
                    appended = result.Appended,
                    replaced = result.Replaced,
                    previousRowCount = result.PreviousRowCount,
                    schemaChanged = result.SchemaChanged,
                    columnCount = columns.Count
                });
            }
        }
        catch (DataStoreTableExistsException ex)
        {
            return ConfirmGate.Failed("ingest_csv_to_sql_failed", action,
                $"Table '{ex.SanitizedTableName}' already exists ({ex.ExistingRowCount:N0} rows). " +
                "Re-call with mode 'replace' to overwrite or 'append' to add rows (append requires a matching schema).");
        }
        catch (DataStoreTableSchemaMismatchException ex)
        {
            return ConfirmGate.Failed("ingest_csv_to_sql_failed", action,
                $"Schema mismatch: the existing '{ex.SanitizedTableName}' has different columns. " +
                "Re-call with mode 'replace' to drop and recreate, or pick a different tableName.");
        }
        catch (InvalidOperationException ex)
        {
            return ConfirmGate.Failed("ingest_csv_to_sql_failed", action, ex.Message);
        }
    }

    private static List<CsvColumn>? ReadCsvColumnsFromArgs(JsonElement args)
    {
        if (!args.TryGetProperty("columns", out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
            return null;
        var list = new List<CsvColumn>(arr.GetArrayLength());
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            var type = item.TryGetProperty("postgresType", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type)) continue;
            list.Add(new CsvColumn(name, type));
        }
        return list.Count > 0 ? list : null;
    }

    private static string XlsxCellToCanonicalString(IXLCell cell)
    {
        var v = cell.Value;
        if (v.IsBlank) return string.Empty;
        if (v.IsText) return v.GetText();
        if (v.IsNumber) return v.GetNumber().ToString("R", CultureInfo.InvariantCulture);
        if (v.IsBoolean) return v.GetBoolean() ? "true" : "false";
        if (v.IsDateTime) return v.GetDateTime().ToString("O", CultureInfo.InvariantCulture);
        if (v.IsTimeSpan) return v.GetTimeSpan().ToString("c", CultureInfo.InvariantCulture);
        if (v.IsError) return string.Empty;
        return cell.GetString();
    }

    private static bool IsMissing(string? value, bool skipMissing)
    {
        if (!skipMissing) return false;
        if (string.IsNullOrEmpty(value)) return true;
        var t = value.Trim();
        if (t.Length == 0) return true;
        return MissingValueSentinels.Contains(t);
    }

    // Standard RFC 4180-ish CSV escape: quote when the value contains a comma,
    // quote, CR, or LF; double-up embedded quotes. Treats null as empty.
    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.IndexOfAny(s_csvSpecialChars) < 0) return value;
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var ch in value)
        {
            sb.Append(ch);
            if (ch == '"') sb.Append('"');
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static readonly char[] s_csvSpecialChars = { ',', '"', '\n', '\r' };

    private static string DetectSourceFormat(string filename)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        return ext switch
        {
            ".csv" or ".tsv" or ".txt" => "csv",
            ".xlsx" or ".xlsm" => "xlsx",
            _ => string.Empty
        };
    }

    private static string GuessContentTypeFromExtension(string filename)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        return ext switch
        {
            ".csv" => "text/csv",
            ".tsv" => "text/tab-separated-values",
            ".md" => "text/markdown",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".yaml" or ".yml" => "application/x-yaml",
            ".html" or ".htm" => "text/html",
            _ => "text/plain"
        };
    }

    private static List<string>? ReadStringArray(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return null;
        var list = new List<string>();
        foreach (var el in v.EnumerateArray())
            if (el.ValueKind == JsonValueKind.String && el.GetString() is { } s) list.Add(s);
        return list.Count > 0 ? list : null;
    }

    private static int? ReadInt(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : null;

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
