using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
                    "Use `ignoreColumns` to drop source columns entirely (neither key nor entity) — e.g. when the XLSX has a redundant datetime column at column B with an empty header (synthesized as 'col_2'), " +
                    "pass `ignoreColumns: [\"col_2\"]` so it isn't unpivoted into junk rows. Case-insensitive; columns listed in both `keyColumns` and `ignoreColumns` are rejected. " +
                    "Use `entityColumnSplit` when the entity headers share a common template — e.g. 'Average temperature in {city} (degree Celsius) - {country} - FSR - Daily' across 1800 columns. " +
                    "Pass `{ template: \"...{city}...{country}...\", outputColumns: [\"city\", \"country\"] }` and the output replaces the single `entityColumnName` column with one column per placeholder, " +
                    "writing the captured values from each source header. The regex is compiled once and run once per source header (not per row), so the per-row cost is essentially nothing even at multi-million-row scale, " +
                    "and the output file shrinks dramatically (each captured city/country is written instead of the full ~70-char string on every row). " +
                    "The template MUST match every non-key, non-ignored source header — if any header fails to match the tool refuses to commit and lists the failures so you can adjust the template or add the offender to `ignoreColumns`. " +
                    "When `entityColumnSplit` is set, `entityColumnName` is ignored and `entityValueRenames` is rejected (the split defines the entity schema). " +
                    "If the source key columns have unhelpful names (e.g. an XLSX with an empty header cell shows up as 'col_1'), use the inline object form in `keyColumns` to pair source and output names: " +
                    "`keyColumns: [{\"source\": \"col_1\", \"output\": \"date\"}]` makes the leftmost output column header 'date' while still matching the unnamed source column. " +
                    "Prefer this over the older `keyColumnRenames` map — the map is fragile because the confirm-gate re-call can drop just the rename and produce a CSV that looks correct in the proposal but ships with the original names. " +
                    "`entityValueRenames` is a {sourceColumnName: outputEntityValue} map that rewrites the values written into the entity column (use it when the unpivoted source columns are poorly named). " +
                    "All renames are case-insensitive on the lookup side; unmatched keys are rejected so a typo doesn't silently no-op. " +
                    "For XLSX, set `sheetName` or `sheetIndex`, `headerRow` (default 1) for the column names, and `descriptionRow` (optional) for an extra metadata row (above OR below the headers) " +
                    "carrying longer-form labels that should be included as a per-output-row `description` column when `includeDescription: true`. " +
                    "When the XLSX has MORE than one header / metadata row (e.g. row 1 = long descriptions, row 2 = short codes, row 3+ = data — common in FAO / World Bank / weather exports), " +
                    "you MUST explicitly set `dataStartRow` to the first row of real data (e.g. `dataStartRow: 3`). The tool refuses to run if `dataStartRow` looks type-shifted from the rows below it, " +
                    "since silently treating a secondary header row as data produces tens of MB of garbage CSV that has to be cleaned up. " +
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
                          "items": {
                            "oneOf": [
                              { "type": "string" },
                              {
                                "type": "object",
                                "properties": {
                                  "source": { "type": "string", "description": "Source column name (matches a header in the file)." },
                                  "output": { "type": ["string", "null"], "description": "Optional output column name. When set, renames the column in the output CSV header. Bundling source + output in one item is more robust than `keyColumnRenames` because the confirm-gate re-call can't drop the rename without dropping the column itself." }
                                },
                                "required": ["source"],
                                "additionalProperties": false
                              }
                            ]
                          },
                          "minItems": 1,
                          "description": "Source columns that identify each row (becomes leftmost columns in output). Each item is either a string (source column name, no rename) or an object { source, output? } that pairs the source column with its output header name. Source matching is case-insensitive."
                        },
                        "ignoreColumns": {
                          "type": ["array", "null"],
                          "items": { "type": "string" },
                          "description": "Optional source column names to drop entirely — neither emitted as keys nor unpivoted into entity/value rows. Useful for redundant datetime columns with empty headers (synthesized as 'col_N'). Case-insensitive; must not overlap keyColumns."
                        },
                        "keyColumnRenames": {
                          "type": ["object", "null"],
                          "description": "DEPRECATED — prefer the inline { source, output } object form inside `keyColumns`. The map form survives in case an agent is more comfortable with it, but it is fragile: if the confirm-gate re-call accidentally drops this field, the rename silently no-ops while the rest of the call succeeds. Keys are case-insensitive against `keyColumns`; unmatched keys and conflicts with an inline `output` for the same source column are rejected.",
                          "additionalProperties": { "type": "string" }
                        },
                        "entityValueRenames": {
                          "type": ["object", "null"],
                          "description": "Optional {sourceColumnName: outputEntityValue} map. Rewrites the values that get written into the entity column when the named source column is unpivoted. Useful when source headers are generic ('col_2') but you want meaningful entity names. Keys are case-insensitive against the source headers; unmatched keys are rejected. Mutually exclusive with entityColumnSplit.",
                          "additionalProperties": { "type": "string" }
                        },
                        "entityColumnSplit": {
                          "type": ["object", "null"],
                          "description": "Replaces the single entity column with one column per {placeholder} parsed out of each source header. Use when entity headers share a common template — e.g. 'Average temperature in {city} (degree Celsius) - {country} - FSR - Daily'. The template MUST match every non-key, non-ignored source header.",
                          "properties": {
                            "template": {
                              "type": "string",
                              "description": "The source-header template. {name} placeholders capture variable parts; everything else is literal text matched verbatim. Each placeholder name must be a valid identifier and must appear in outputColumns."
                            },
                            "outputColumns": {
                              "type": "array",
                              "items": { "type": "string" },
                              "minItems": 1,
                              "description": "Output column names, in the order they should appear in the CSV. Must match the set of {name} placeholders in the template."
                            }
                          },
                          "required": ["template", "outputColumns"],
                          "additionalProperties": false
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
        "BEFORE running the unpivot, look at the sample column names the profile returned. If they share a common templated shape — a fixed prefix and/or suffix wrapped around 1-3 variable parts (e.g. 'Average temperature in {city} (degree Celsius) - {country} - FSR - Daily', 'GDP per capita ({year}) - {country}', '{metric}_{region}_{year}') — proactively SUGGEST `entityColumnSplit` to the user with a concrete template before calling the tool. Splitting a 70-char repeated entity string into two short fields shrinks the output CSV 3-5x and makes the result queryable per-attribute without LIKE patterns. Surface the template and the resulting output schema in your proposal so the user can correct the template before commit. " +
        "When renaming a key column (e.g. an XLSX with an empty header for the date column renders as 'col_1'), USE THE INLINE OBJECT FORM in keyColumns: `keyColumns: [{\"source\": \"col_1\", \"output\": \"date\"}]`. The older `keyColumnRenames` map is structurally fragile under the confirm-gate two-call pattern — if the rename map is accidentally omitted from the commit call, the output CSV silently ships with the original source name. The inline form bundles source and output together so dropping the rename means dropping the column. " +
        "The unpivot proposal contains an `outputHeader` field showing the EXACT CSV header line that will be written. Read this back to the user so they can confirm column names before commit. The committed result echoes the same field — if it doesn't match what was in the proposal, a parameter was dropped on the confirm re-call and the run needs to be redone. " +
        "When you confirm a confirm-gated tool, re-pass EVERY parameter from the original proposal call verbatim alongside `confirmed: true`. The tool is stateless between phases; any field you omit on the confirm call effectively reverts to its default. " +
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

        // Parse keyColumns — each item can be a string OR { source, output? } object.
        // Inline output is the preferred form (the confirm-gate re-call can't drop just the
        // rename without dropping the column itself).
        var keyColumnSpecs = ReadKeyColumnSpecs(args, out var keySpecError);
        if (keySpecError is not null)
            return ConfirmGate.Rejected(action, keySpecError);
        if (keyColumnSpecs is null || keyColumnSpecs.Count == 0)
            return ConfirmGate.Rejected(action, "keyColumns is required and must list at least one column (e.g. [\"date\"] or [{\"source\": \"col_1\", \"output\": \"date\"}]).");

        // Source-name view for downstream code that thinks in source names (validation,
        // ignoreColumns overlap detection, unpivot helpers).
        var keyColumns = keyColumnSpecs.Select(s => s.Source).ToList();

        var ignoreColumns = ReadStringArray(args, "ignoreColumns") ?? new List<string>();
        if (ignoreColumns.Count > 0)
        {
            var keyLookup = new HashSet<string>(keyColumns, StringComparer.OrdinalIgnoreCase);
            var overlap = ignoreColumns.FirstOrDefault(c => keyLookup.Contains(c));
            if (overlap is not null)
                return ConfirmGate.Rejected(action,
                    $"ignoreColumns and keyColumns overlap on '{overlap}'. A column can be a key OR ignored, not both.");
        }

        // keyColumnRenames is the deprecated map form. Merge it into the spec list, rejecting
        // conflicts where both an inline `output` AND a map entry target the same source column.
        var keyColumnRenames = ReadStringMap(args, "keyColumnRenames");
        if (keyColumnRenames is not null)
        {
            var keySet = new HashSet<string>(keyColumns, StringComparer.OrdinalIgnoreCase);
            foreach (var rename in keyColumnRenames)
            {
                if (!keySet.Contains(rename.Key))
                    return ConfirmGate.Rejected(action,
                        $"keyColumnRenames key '{rename.Key}' does not match any source name in keyColumns ({string.Join(", ", keyColumns)}). " +
                        "The map key must be the source column name; the value is the desired output header.");
                if (string.IsNullOrWhiteSpace(rename.Value))
                    return ConfirmGate.Rejected(action,
                        $"keyColumnRenames['{rename.Key}'] is empty. Provide a non-empty output column name.");
            }
            // Merge map renames into the spec list (without clobbering inline outputs).
            for (int i = 0; i < keyColumnSpecs.Count; i++)
            {
                if (!keyColumnRenames.TryGetValue(keyColumnSpecs[i].Source, out var mapOutput)) continue;
                if (keyColumnSpecs[i].Output is { Length: > 0 } inlineOutput && !string.Equals(inlineOutput, mapOutput.Trim(), StringComparison.Ordinal))
                    return ConfirmGate.Rejected(action,
                        $"keyColumnRenames['{keyColumnSpecs[i].Source}'] = '{mapOutput}' conflicts with the inline output '{inlineOutput}' for the same source column. Use one form or make them agree.");
                keyColumnSpecs[i] = keyColumnSpecs[i] with { Output = mapOutput.Trim() };
            }
        }

        // Reject duplicate output headers (post-resolution).
        var resolvedKeyHeaders = keyColumnSpecs.Select(s => s.Output is { Length: > 0 } o ? o : s.Source).ToList();
        var dupKey = resolvedKeyHeaders
            .GroupBy(h => h, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1)?.Key;
        if (dupKey is not null)
            return ConfirmGate.Rejected(action,
                $"keyColumns resolves to duplicate output column '{dupKey}'. Each key column must produce a distinct output header.");

        // entityValueRenames is validated against actual source headers inside the unpivot helper
        // (we don't know them yet), but reject obviously-malformed entries (empty value) up front.
        var entityValueRenames = ReadStringMap(args, "entityValueRenames");
        if (entityValueRenames is not null)
        {
            foreach (var rename in entityValueRenames)
                if (string.IsNullOrWhiteSpace(rename.Value))
                    return ConfirmGate.Rejected(action,
                        $"entityValueRenames['{rename.Key}'] is empty. Provide a non-empty entity value.");
        }

        // entityColumnSplit: parsed + regex-compiled here so syntax errors surface at proposal time.
        // Header-coverage validation happens later — once we've read the source headers.
        var entityColumnSplit = BuildEntityColumnSplit(args, out var entitySplitError);
        if (entitySplitError is not null)
            return ConfirmGate.Rejected(action, entitySplitError);
        if (entityColumnSplit is not null && entityValueRenames is not null)
            return ConfirmGate.Rejected(action,
                "entityColumnSplit and entityValueRenames are mutually exclusive — the split defines the entity output schema, so per-source renames would be ambiguous. Pick one.");

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

        // The fully-resolved CSV header line that WILL be written on commit. Echoed in
        // the proposal preview so the agent can read it back to the user verbatim — and
        // echoed in the committed result so any divergence (e.g. agent dropped a rename
        // on the commit re-call) is immediately visible side-by-side with the proposal.
        var resolvedOutputHeader = BuildResolvedOutputHeader(
            keyColumnSpecs, entityColumnSplit, entityColumnName!, valueColumnName!, includeDescription);

        var preview = new
        {
            dataStoreId,
            sourceFileId,
            sourceFilename = sourceMeta.Filename,
            sourceFolder = sourceMeta.FolderPath,
            sourceFormat,
            destinationFolder = actualDestinationFolder,
            destinationFilename,
            keyColumns = keyColumnSpecs.Select(s => s.Output is null
                ? (object)s.Source
                : new { source = s.Source, output = s.Output }).ToList(),
            ignoreColumns = ignoreColumns.Count > 0 ? ignoreColumns : null,
            keyColumnRenames,
            entityValueRenames,
            entityColumnSplit = entityColumnSplit is null
                ? null
                : new { template = entityColumnSplit.Template, outputColumns = entityColumnSplit.OutputColumns },
            entityColumnName = entityColumnSplit is null ? entityColumnName : null,
            valueColumnName,
            includeDescription,
            skipMissingValues,
            outputHeader = resolvedOutputHeader,
            estimateNote = "Output rows ≈ source data rows × (totalColumns − keyColumns.length − ignoreColumns.length). Run profile_data_store_*_file first if the user wants a precise estimate.",
            confirmReminder = "When confirming, RE-PASS every parameter from this proposal verbatim (including keyColumns object items, keyColumnRenames, entityColumnSplit, ignoreColumns, dataStartRow, etc.) together with confirmed:true — the tool is stateless between proposal and commit. Compare the `outputHeader` in the committed result to this one to spot any dropped fields."
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
                await outWriter.WriteAsync((resolvedOutputHeader + "\n").AsMemory(), ct);

                if (sourceFormat == "xlsx")
                {
                    (outputRows, skippedRows) = await UnpivotXlsxAsync(
                        sourceStream, outWriter, sheetName, sheetIndex,
                        headerRow, descriptionRow, dataStartRow,
                        keyColumns, ignoreColumns, entityValueRenames, entityColumnSplit,
                        includeDescription, skipMissingValues, ct);
                }
                else
                {
                    (outputRows, skippedRows) = await UnpivotCsvAsync(
                        sourceStream, outWriter, delimiter,
                        keyColumns, ignoreColumns, entityValueRenames, entityColumnSplit,
                        skipMissingValues, ct);
                }
            }

            await using var uploadStream = File.OpenRead(tempPath);
            var uploaded = await files.UploadAsync(
                dataStoreId, actualDestinationFolder, destinationFilename, "text/csv",
                uploadStream, ctx.Session.UserId, ct);
            sw.Stop();
            return ConfirmGate.Committed("unpivot_to_csv_committed", action, new
            {
                outputHeader = resolvedOutputHeader,
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
        IReadOnlyList<string> ignoreColumns,
        IReadOnlyDictionary<string, string>? entityValueRenames,
        EntityColumnSplit? entityColumnSplit,
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
        var skipIndexSet = ResolveIgnoreIndices(ignoreColumns, headers, keyLookup, keyIndexSet);

        ValidateEntityValueRenames(entityValueRenames, headers, keyIndexSet, skipIndexSet);

        // Pre-build the entity fragment that gets written per source column. Without a split this
        // is one CSV-escaped value; with a split it's the joined CSV-escaped captured values.
        // Either way the write loop just emits it verbatim, so the hot path stays branchless.
        var entityEscaped = entityColumnSplit is not null
            ? PrecomputeSplitEntityFragments(headers, keyIndexSet, skipIndexSet, entityColumnSplit)
            : BuildSimpleEntityFragments(headers, keyIndexSet, skipIndexSet, entityValueRenames);

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
                if (keyIndexSet.Contains(c) || skipIndexSet.Contains(c)) continue;
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
        IReadOnlyList<string> ignoreColumns,
        IReadOnlyDictionary<string, string>? entityValueRenames,
        EntityColumnSplit? entityColumnSplit,
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
        var skipIndexSet = ResolveIgnoreIndices(ignoreColumns, headers, keyLookup, keyIndexSet);

        ValidateEntityValueRenames(entityValueRenames, headers, keyIndexSet, skipIndexSet);

        // Sanity check: detect when dataStartRow is actually a second header row.
        // FAO / World Bank / weather exports commonly have row 1 = long description,
        // row 2 = short code, row 3+ = data. If the agent picks headerRow:1 and accepts
        // the default dataStartRow (= headerRow + 1 = 2), every "data" row is the
        // sub-header — producing 38MB of garbage CSV with literals like "Date string"
        // in the date column and city codes in the value column.
        // Rather than silently emit bad output, refuse and tell the agent what to fix.
        DetectSecondaryHeaderRow(sheet, headers, keyIndexSet, skipIndexSet, dataStartRow, lastRow, lastCol);

        // Pre-build entity fragments + optional descriptions once. With a split, each fragment
        // already encodes multiple CSV cells joined by commas — the write loop emits it verbatim.
        var entityEscaped = entityColumnSplit is not null
            ? PrecomputeSplitEntityFragments(headers, keyIndexSet, skipIndexSet, entityColumnSplit)
            : BuildSimpleEntityFragments(headers, keyIndexSet, skipIndexSet, entityValueRenames);
        var descEscaped = includeDescription ? new string?[lastCol] : null;
        if (descEscaped is not null)
        {
            for (int c = 0; c < lastCol; c++)
            {
                if (keyIndexSet.Contains(c) || skipIndexSet.Contains(c)) continue;
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
                if (keyIndexSet.Contains(c) || skipIndexSet.Contains(c)) continue;
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

    private static string ResolveEntityValue(string sourceHeader, IReadOnlyDictionary<string, string>? renames)
        => renames is not null && renames.TryGetValue(sourceHeader, out var r) ? r.Trim() : sourceHeader;

    // A key-column entry. Source is the column name in the input file; Output (when set) is
    // the column name we'll write into the output CSV header. The two-field form is what
    // makes the rename robust to the confirm-gate re-call dropping individual parameters.
    private sealed record KeyColumnSpec(string Source, string? Output);

    // Reproduce the exact CSV header line that the unpivot will write, so the proposal can
    // show it AND the committed result can echo it. The hot-loop writer uses this same
    // string verbatim — keeping there only one source of truth for header composition.
    private static string BuildResolvedOutputHeader(
        IReadOnlyList<KeyColumnSpec> keyColumnSpecs,
        EntityColumnSplit? entityColumnSplit,
        string entityColumnName,
        string valueColumnName,
        bool includeDescription)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", keyColumnSpecs.Select(s =>
            CsvEscape(s.Output is { Length: > 0 } o ? o : s.Source))));
        sb.Append(',');
        if (entityColumnSplit is not null)
            sb.Append(string.Join(",", entityColumnSplit.OutputColumns.Select(CsvEscape)));
        else
            sb.Append(CsvEscape(entityColumnName));
        if (includeDescription)
        {
            sb.Append(',');
            sb.Append(CsvEscape("description"));
        }
        sb.Append(',');
        sb.Append(CsvEscape(valueColumnName));
        return sb.ToString();
    }

    // Walk the keyColumns JSON array, accepting either string items (source-only, no rename)
    // or object items { source, output? }. Returns null + an error message if any item is
    // malformed; returns an empty list if keyColumns is missing.
    private static List<KeyColumnSpec>? ReadKeyColumnSpecs(JsonElement args, out string? error)
    {
        error = null;
        if (!args.TryGetProperty("keyColumns", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return new List<KeyColumnSpec>();
        var list = new List<KeyColumnSpec>();
        int idx = 0;
        foreach (var item in arr.EnumerateArray())
        {
            switch (item.ValueKind)
            {
                case JsonValueKind.String:
                {
                    var s = item.GetString();
                    if (string.IsNullOrWhiteSpace(s))
                    {
                        error = $"keyColumns[{idx}] is an empty string. Provide a non-empty source column name.";
                        return null;
                    }
                    list.Add(new KeyColumnSpec(s.Trim(), null));
                    break;
                }
                case JsonValueKind.Object:
                {
                    string? source = null;
                    string? output = null;
                    if (item.TryGetProperty("source", out var sv) && sv.ValueKind == JsonValueKind.String)
                        source = sv.GetString();
                    if (item.TryGetProperty("output", out var ov) && ov.ValueKind == JsonValueKind.String)
                        output = ov.GetString();
                    if (string.IsNullOrWhiteSpace(source))
                    {
                        error = $"keyColumns[{idx}] is an object but `source` is missing or empty. Provide {{ \"source\": \"<name>\", \"output\": \"<header>\" }}.";
                        return null;
                    }
                    if (output is not null && string.IsNullOrWhiteSpace(output))
                    {
                        error = $"keyColumns[{idx}].output is empty. Either omit `output` or set it to a non-empty header name.";
                        return null;
                    }
                    list.Add(new KeyColumnSpec(source.Trim(), output?.Trim()));
                    break;
                }
                default:
                    error = $"keyColumns[{idx}] must be a string or an object {{ source, output? }} — got {item.ValueKind}.";
                    return null;
            }
            idx++;
        }
        return list;
    }

    // A parsed entityColumnSplit instruction. The compiled regex carries one named group
    // per outputColumn — we run it once per source header (precomputed) to derive the
    // CSV-escaped per-column values that get repeated on every output row.
    private sealed record EntityColumnSplit(
        string Template,
        IReadOnlyList<string> OutputColumns,
        Regex CompiledRegex);

    private static readonly Regex s_entitySplitPlaceholderRegex = new(@"\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    // Parse the agent-supplied { template, outputColumns } into a compiled regex.
    // The template uses {name} placeholders; everything else is literal text. Each
    // placeholder must appear in outputColumns and vice versa. Names must be valid
    // identifier-ish (the regex engine uses them as group names). Failures bubble up
    // as ConfirmGate.Rejected so the agent sees what to fix at proposal time, BEFORE
    // we touch the source file.
    private static EntityColumnSplit? BuildEntityColumnSplit(JsonElement args, out string? error)
    {
        error = null;
        if (!args.TryGetProperty("entityColumnSplit", out var el) || el.ValueKind != JsonValueKind.Object) return null;

        string? template = null;
        if (el.TryGetProperty("template", out var tv) && tv.ValueKind == JsonValueKind.String)
            template = tv.GetString();
        if (string.IsNullOrWhiteSpace(template))
        {
            error = "entityColumnSplit.template is required and must be a non-empty string.";
            return null;
        }

        var outputColumns = new List<string>();
        if (el.TryGetProperty("outputColumns", out var ov) && ov.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in ov.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s && !string.IsNullOrWhiteSpace(s))
                    outputColumns.Add(s.Trim());
        }
        if (outputColumns.Count == 0)
        {
            error = "entityColumnSplit.outputColumns is required and must contain at least one non-empty name.";
            return null;
        }

        // Validate names — they're used as regex group names, so they must be identifier-ish
        // and unique. Reject up front rather than letting the regex engine throw something cryptic.
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in outputColumns)
        {
            if (!Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            {
                error = $"entityColumnSplit.outputColumns entry '{name}' is not a valid identifier (use letters, digits, and underscores; must not start with a digit).";
                return null;
            }
            if (!seenNames.Add(name))
            {
                error = $"entityColumnSplit.outputColumns contains '{name}' more than once. Each output column name must be unique.";
                return null;
            }
        }

        // Walk the template, replacing {name} placeholders with named regex groups and
        // regex-escaping everything else. Collect the placeholder names we see so we can
        // cross-check against outputColumns.
        var sb = new StringBuilder();
        sb.Append('^');
        var seenPlaceholders = new HashSet<string>(StringComparer.Ordinal);
        int last = 0;
        foreach (Match m in s_entitySplitPlaceholderRegex.Matches(template))
        {
            if (m.Index > last) sb.Append(Regex.Escape(template.Substring(last, m.Index - last)));
            var name = m.Groups["name"].Value;
            if (!seenNames.Contains(name))
            {
                error = $"entityColumnSplit.template references '{{{name}}}' but '{name}' is not in outputColumns ({string.Join(", ", outputColumns)}).";
                return null;
            }
            if (!seenPlaceholders.Add(name))
            {
                error = $"entityColumnSplit.template references '{{{name}}}' more than once. Each placeholder must appear exactly once.";
                return null;
            }
            sb.Append("(?<").Append(name).Append(">.+?)");
            last = m.Index + m.Length;
        }
        if (last < template.Length) sb.Append(Regex.Escape(template.Substring(last)));
        sb.Append('$');

        var missingFromTemplate = outputColumns.Where(n => !seenPlaceholders.Contains(n)).ToList();
        if (missingFromTemplate.Count > 0)
        {
            error = $"entityColumnSplit.outputColumns includes [{string.Join(", ", missingFromTemplate)}] but the template doesn't reference {{{missingFromTemplate[0]}}}. Every outputColumn must appear as a placeholder.";
            return null;
        }

        Regex compiled;
        try
        {
            compiled = new Regex(sb.ToString(), RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }
        catch (ArgumentException ex)
        {
            error = $"entityColumnSplit produced an invalid regex from the template: {ex.Message}";
            return null;
        }

        return new EntityColumnSplit(template, outputColumns, compiled);
    }

    // No-split path: one CSV cell per source column, optionally renamed.
    private static string?[] BuildSimpleEntityFragments(
        IReadOnlyList<string> headers,
        HashSet<int> keyIndexSet,
        HashSet<int> skipIndexSet,
        IReadOnlyDictionary<string, string>? entityValueRenames)
    {
        var fragments = new string?[headers.Count];
        for (int c = 0; c < headers.Count; c++)
            if (!keyIndexSet.Contains(c) && !skipIndexSet.Contains(c))
                fragments[c] = CsvEscape(ResolveEntityValue(headers[c], entityValueRenames));
        return fragments;
    }

    // For each non-key, non-ignored source header, run the split regex and pre-build the
    // CSV-escaped, comma-joined fragment that will be written into every output row for
    // that source column. Throws ArgumentException listing the first few headers that
    // don't match — the agent sees this on commit and can adjust the template.
    private static string?[] PrecomputeSplitEntityFragments(
        IReadOnlyList<string> headers,
        HashSet<int> keyIndexSet,
        HashSet<int> skipIndexSet,
        EntityColumnSplit split)
    {
        var fragments = new string?[headers.Count];
        var unmatched = new List<string>();
        for (int c = 0; c < headers.Count; c++)
        {
            if (keyIndexSet.Contains(c) || skipIndexSet.Contains(c)) continue;
            var m = split.CompiledRegex.Match(headers[c]);
            if (!m.Success)
            {
                if (unmatched.Count < 5) unmatched.Add(headers[c]);
                continue;
            }
            var parts = new string[split.OutputColumns.Count];
            for (int i = 0; i < split.OutputColumns.Count; i++)
                parts[i] = CsvEscape(m.Groups[split.OutputColumns[i]].Value);
            fragments[c] = string.Join(",", parts);
        }
        if (unmatched.Count > 0)
            throw new ArgumentException(
                $"entityColumnSplit.template did not match {unmatched.Count} source header(s) — first failures: " +
                string.Join("; ", unmatched.Select(h => $"'{h}'")) +
                $". Adjust the template so it matches every non-key, non-ignored source column, or add the failing columns to `ignoreColumns`.");
        return fragments;
    }

    // Resolve the user-supplied ignoreColumns (header names, case-insensitive) into
    // a set of zero-based column indices. Reject names that don't match any source
    // header — silent no-op would mask a typo and leave junk columns in the output.
    private static HashSet<int> ResolveIgnoreIndices(
        IReadOnlyList<string> ignoreColumns,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, int> headerLookup,
        HashSet<int> keyIndexSet)
    {
        var skip = new HashSet<int>();
        if (ignoreColumns.Count == 0) return skip;
        foreach (var name in ignoreColumns)
        {
            if (!headerLookup.TryGetValue(name, out var idx))
                throw new ArgumentException(
                    $"ignoreColumn '{name}' not found in source headers. " +
                    $"Available non-key headers: {string.Join(", ", headers.Where((_, i) => !keyIndexSet.Contains(i)).Select(h => $"'{h}'"))}.");
            skip.Add(idx);
        }
        return skip;
    }

    // Sample up to 12 non-key, non-ignored columns. For each, compare the cell type at
    // dataStartRow to the cell types at dataStartRow + 1..3. If most sampled columns show
    // "text at dataStartRow, number/datetime just below," dataStartRow almost certainly
    // points at a secondary header row (think 'Afghanistan-Farah' / 'Date string') rather
    // than data. The threshold is intentionally conservative (>= 70% of useful samples)
    // so a sheet that's legitimately all-text doesn't trip the check — those sheets won't
    // have a type shift between dataStartRow and the rows below it.
    private static void DetectSecondaryHeaderRow(
        IXLWorksheet sheet,
        IReadOnlyList<string> headers,
        HashSet<int> keyIndexSet,
        HashSet<int> skipIndexSet,
        int dataStartRow,
        int lastRow,
        int lastCol)
    {
        if (dataStartRow >= lastRow) return; // Nothing below — can't detect.

        var sampleCols = new List<int>(12);
        for (int c = 0; c < lastCol && sampleCols.Count < 12; c++)
        {
            if (keyIndexSet.Contains(c) || skipIndexSet.Contains(c)) continue;
            if (string.IsNullOrWhiteSpace(headers[c])) continue;
            sampleCols.Add(c);
        }
        if (sampleCols.Count < 4) return; // Too narrow to draw a confident conclusion.

        int useful = 0;
        int flagged = 0;
        int probeRows = Math.Min(3, lastRow - dataStartRow);
        foreach (var c in sampleCols)
        {
            var startVal = sheet.Cell(dataStartRow, c + 1).Value;
            if (!startVal.IsText) continue; // Only flag when the supposed first data row is text.
            var startText = startVal.GetText();
            if (string.IsNullOrWhiteSpace(startText)) continue;
            if (double.TryParse(startText, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) continue; // Numeric-as-text — leave alone.

            bool laterIsTyped = false;
            for (int delta = 1; delta <= probeRows; delta++)
            {
                var laterVal = sheet.Cell(dataStartRow + delta, c + 1).Value;
                if (laterVal.IsNumber || laterVal.IsDateTime || laterVal.IsBoolean)
                {
                    laterIsTyped = true;
                    break;
                }
            }
            useful++;
            if (laterIsTyped) flagged++;
        }

        if (useful < 4) return;
        // 70%+ of useful samples flip from text → number/date one row down. Almost
        // certainly a secondary header that should be skipped.
        if (flagged * 10 >= useful * 7)
        {
            throw new ArgumentException(
                $"Row {dataStartRow} looks like a secondary header row — {flagged} of {useful} sampled non-key columns hold text values there but switch to numbers or dates within the {probeRows} rows below. " +
                $"This usually means the XLSX has more than one header / metadata row (e.g. row 1 = long descriptions like 'Average temperature in Farah ... - Daily', row 2 = short codes like 'Afghanistan-Farah', row 3+ = data). " +
                $"Re-run with `dataStartRow: {dataStartRow + 1}` (or wherever the real data actually begins), and consider setting `descriptionRow` to carry the extra metadata row into the output if useful.");
        }
    }

    // Reject any entityValueRenames key that doesn't match a real non-key, non-ignored
    // source header, so a typo doesn't silently no-op the way it would with a permissive map.
    private static void ValidateEntityValueRenames(
        IReadOnlyDictionary<string, string>? renames,
        IReadOnlyList<string> headers,
        HashSet<int> keyIndexSet,
        HashSet<int> skipIndexSet)
    {
        if (renames is null || renames.Count == 0) return;
        var entityHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Count; i++)
            if (!keyIndexSet.Contains(i) && !skipIndexSet.Contains(i)) entityHeaders.Add(headers[i]);
        foreach (var rename in renames)
        {
            if (!entityHeaders.Contains(rename.Key))
                throw new ArgumentException(
                    $"entityValueRenames key '{rename.Key}' does not match any source column that will be unpivoted. " +
                    "It must be the name of a source column that is NOT in keyColumns and NOT in ignoreColumns.");
        }
    }

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

    private static Dictionary<string, string>? ReadStringMap(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Object) return null;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in v.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { } s) map[p.Name] = s;
        return map.Count > 0 ? map : null;
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
