using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.DataStores;
using AutoNate.Web.Services.DataStores.File;
using AutoNate.Web.Services.DataStores.Sql;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
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
                Invoke: InvokeListFilesAsync),

            new AgentTool(
                Name: "peek_data_store_file",
                Description:
                    "Read the first (or last) chunk of a file in a FileType data store and return it as text. " +
                    "Best for a quick 'what does this look like' on any text-ish file (csv, json, log, txt). " +
                    "Streams — does NOT load the whole file. `maxBytes` defaults to 8192, capped at 65536. " +
                    "`fromEnd: true` reads the tail instead of the head (useful for tailing a log).",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "fileId": { "type": "string" },
                        "maxBytes": { "type": ["integer", "null"], "minimum": 1, "maximum": 65536 },
                        "fromEnd": { "type": ["boolean", "null"] }
                      },
                      "required": ["dataStoreId", "fileId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokePeekFileAsync),

            new AgentTool(
                Name: "inspect_data_store_text_file",
                Description:
                    "Summarize a text file: total size, sample of the first N lines, and (optionally) the total line count. " +
                    "Streams line-by-line — safe on multi-GB files. `lineCount` defaults to 50, capped at 500. " +
                    "Set `countTotalLines: true` to also report the total — bounded to ~5M lines for responsiveness; " +
                    "the result flag `totalLineCountComplete` indicates whether the cap was hit.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "fileId": { "type": "string" },
                        "lineCount": { "type": ["integer", "null"], "minimum": 1, "maximum": 500 },
                        "countTotalLines": { "type": ["boolean", "null"] }
                      },
                      "required": ["dataStoreId", "fileId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeInspectTextFileAsync),

            new AgentTool(
                Name: "inspect_data_store_csv_file",
                Description:
                    "Parse a CSV in a FileType data store. Returns ALL column headers with their position, " +
                    "inferred Postgres-style types per column (bigint / double precision / boolean / timestamptz / text — " +
                    "same vocab as datasets and CSV ingest), the first `sampleRows` rows as objects, and the total row count when requested. " +
                    "Streams row-by-row — safe on very large CSVs. " +
                    "WIDE FILES (hundreds of columns): start with `schemaOnly: true` to fetch the column list cheaply, then re-call with " +
                    "`columns: [\"col1\", \"col2\", ...]` to limit sample rows + type inference to the subset you care about — this keeps the " +
                    "response small enough for the LLM. If `columns` is omitted, the sample only includes the first `maxColumnsInSample` columns " +
                    "(all column names still appear in the schema). " +
                    "`sampleRows` defaults to 20, capped at 200. `computeRowCount: true` walks the whole file to count rows — bounded to ~5M for responsiveness; " +
                    "`totalRowCountComplete` indicates whether the cap was hit. `delimiter` defaults to ','. " +
                    "`maxCellLength` clamps individual cell values (default 200 chars, max 2000) — a clipped value ends with '…'.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "fileId": { "type": "string" },
                        "sampleRows": { "type": ["integer", "null"], "minimum": 1, "maximum": 200 },
                        "computeRowCount": { "type": ["boolean", "null"] },
                        "delimiter": { "type": ["string", "null"], "minLength": 1, "maxLength": 4 },
                        "schemaOnly": { "type": ["boolean", "null"] },
                        "columns": {
                          "type": ["array", "null"],
                          "items": { "type": "string" },
                          "description": "Specific column names to include in sample rows + type inference. Case-insensitive match."
                        },
                        "maxColumnsInSample": { "type": ["integer", "null"], "minimum": 1, "maximum": 500 },
                        "maxCellLength": { "type": ["integer", "null"], "minimum": 0, "maximum": 2000 }
                      },
                      "required": ["dataStoreId", "fileId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeInspectCsvFileAsync),

            new AgentTool(
                Name: "profile_data_store_csv_file",
                Description:
                    "Profile a CSV to DESCRIBE the shape of the data — what's in it, how it's laid out, and how it might be reshaped. " +
                    "Designed for 'tell me about this file' questions on wide files where listing 1000+ columns would be useless. " +
                    "Returns: total row + column count; header pattern analysis (how many headers match date / alpha-name / integer / float / id patterns, " +
                    "plus the dominant pattern and the 'odd column out' that's likely a key); per-column statistics for a representative subset of columns " +
                    "(min / max / null count / distinct count up to 200 / a few sample values / inferred type); a layout hint that classifies the file as " +
                    "wide-pivot vs long-table with a description; and conversion recommendations (e.g. 'this is a wide pivot; unpivot to long format for per-entity analysis'). " +
                    "The agent uses this to compose a plain-language summary of the file ('1891 cities tracked, daily readings from 2015-01-01 to 2026-06-09, ...'). " +
                    "Streams row-by-row, bounded to ~5M rows. `profileColumns` overrides the default column picker; otherwise the tool auto-picks ~10 representative columns. " +
                    "`maxScannedRows` caps the streaming scan (default 5M).",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "fileId": { "type": "string" },
                        "delimiter": { "type": ["string", "null"], "minLength": 1, "maxLength": 4 },
                        "profileColumns": {
                          "type": ["array", "null"],
                          "items": { "type": "string" },
                          "description": "Specific column names to deep-profile. Case-insensitive."
                        },
                        "maxProfiledColumns": { "type": ["integer", "null"], "minimum": 1, "maximum": 50 },
                        "maxScannedRows": { "type": ["integer", "null"], "minimum": 1000, "maximum": 5000000 }
                      },
                      "required": ["dataStoreId", "fileId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeProfileCsvFileAsync),

            new AgentTool(
                Name: "lookup_data_store_csv_rows",
                Description:
                    "Stream a CSV and return rows where the value in `filterColumn` matches `filterValue` (case-insensitive), projecting the chosen columns. " +
                    "Use to answer point lookups like 'what was the value of X on date Y' without first ingesting the file as a SQL table. " +
                    "Single-column equality filter only — for richer queries, ingest the CSV into a SqlType data store and use AQL. " +
                    "`projectColumns` selects which columns appear in each returned row (case-insensitive); omit to project the filter column plus the first 20 other columns. " +
                    "`limit` defaults to 20, capped at 100. Streams; gives up after `maxScannedRows` (default 5M) to stay responsive.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "fileId": { "type": "string" },
                        "delimiter": { "type": ["string", "null"], "minLength": 1, "maxLength": 4 },
                        "filterColumn": { "type": "string" },
                        "filterValue": { "type": "string" },
                        "projectColumns": {
                          "type": ["array", "null"],
                          "items": { "type": "string" }
                        },
                        "limit": { "type": ["integer", "null"], "minimum": 1, "maximum": 100 },
                        "maxScannedRows": { "type": ["integer", "null"], "minimum": 1000, "maximum": 5000000 },
                        "maxCellLength": { "type": ["integer", "null"], "minimum": 0, "maximum": 2000 }
                      },
                      "required": ["dataStoreId", "fileId", "filterColumn", "filterValue"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeLookupCsvRowsAsync),

            new AgentTool(
                Name: "inspect_data_store_xlsx_workbook",
                Description:
                    "List the sheets in an XLSX workbook with each sheet's name, row count, column count, and hidden flag. " +
                    "Cheap overview — does not read row data. Use this first to see what's in the file, then call " +
                    "inspect_data_store_xlsx_sheet to dig into one specific sheet.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "fileId": { "type": "string" }
                      },
                      "required": ["dataStoreId", "fileId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeInspectXlsxWorkbookAsync),

            new AgentTool(
                Name: "inspect_data_store_xlsx_sheet",
                Description:
                    "Inspect one sheet of an XLSX workbook. Returns ALL column headers (from `headerRow`, default 1), " +
                    "an inferred type per column (bigint / double precision / boolean / timestamptz / text — same vocab as the CSV inspector), " +
                    "the first `sampleRows` data rows formatted the way Excel would display them, and the sheet's total row and column counts. " +
                    "Pick the sheet by `sheetName` (case-insensitive) or `sheetIndex` (1-based); omit both to use the first sheet. " +
                    "WIDE SHEETS: start with `schemaOnly: true` to fetch the column list cheaply, then re-call with `columns: [\"col1\", \"col2\"]` " +
                    "to limit sample rows + type inference to the subset you care about. If `columns` is omitted, the sample only includes the first " +
                    "`maxColumnsInSample` columns (all column names still appear in the schema). " +
                    "`sampleRows` defaults to 20, capped at 200. `maxCellLength` clamps cell values (default 200 chars, max 2000). " +
                    "XLSX size cap: 150MB — beyond that, save the sheet as CSV and use the CSV inspector.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "fileId": { "type": "string" },
                        "sheetName": { "type": ["string", "null"] },
                        "sheetIndex": { "type": ["integer", "null"], "minimum": 1 },
                        "headerRow": { "type": ["integer", "null"], "minimum": 1 },
                        "sampleRows": { "type": ["integer", "null"], "minimum": 1, "maximum": 200 },
                        "schemaOnly": { "type": ["boolean", "null"] },
                        "columns": {
                          "type": ["array", "null"],
                          "items": { "type": "string" },
                          "description": "Specific column names to include in sample rows + type inference. Case-insensitive match."
                        },
                        "maxColumnsInSample": { "type": ["integer", "null"], "minimum": 1, "maximum": 500 },
                        "maxCellLength": { "type": ["integer", "null"], "minimum": 0, "maximum": 2000 }
                      },
                      "required": ["dataStoreId", "fileId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeInspectXlsxSheetAsync),

            new AgentTool(
                Name: "profile_data_store_xlsx_sheet",
                Description:
                    "Profile one sheet of an XLSX workbook to DESCRIBE the shape of the data — same purpose as profile_data_store_csv_file but for Excel. " +
                    "Returns: total row + column count; header pattern analysis with the dominant pattern and the 'odd column out' key candidates; " +
                    "per-column statistics for ~10 representative columns (min / max / null count / distinct count / sample values / inferred Postgres-style type); " +
                    "a layout hint (wide-pivot vs long-table) with description and conversion recommendations. " +
                    "Designed for 'tell me about this sheet' on wide files (e.g. 1900-column city-by-date temperature pivot). " +
                    "`headerRow` (default 1) picks the row whose cells become column names. " +
                    "`descriptionRow` (optional) names a separate row that carries longer descriptive labels above the headers — when set, those values are surfaced as `descriptionAbove` on each profiled column. " +
                    "Common case: a workbook with row 1 = banner like 'Average temperature in Farah (degree Celsius)' and row 2 = compact code like 'Afghanistan-Farah'. Call with `headerRow: 2, descriptionRow: 1` to expose both. " +
                    "Pick the sheet with `sheetName` or `sheetIndex` (1-based); omit both for the first sheet. XLSX size cap: 150MB.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "fileId": { "type": "string" },
                        "sheetName": { "type": ["string", "null"] },
                        "sheetIndex": { "type": ["integer", "null"], "minimum": 1 },
                        "headerRow": { "type": ["integer", "null"], "minimum": 1 },
                        "descriptionRow": { "type": ["integer", "null"], "minimum": 1 },
                        "profileColumns": {
                          "type": ["array", "null"],
                          "items": { "type": "string" },
                          "description": "Specific column names to deep-profile. Case-insensitive."
                        },
                        "maxProfiledColumns": { "type": ["integer", "null"], "minimum": 1, "maximum": 50 },
                        "maxScannedRows": { "type": ["integer", "null"], "minimum": 1000, "maximum": 5000000 }
                      },
                      "required": ["dataStoreId", "fileId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeProfileXlsxSheetAsync),

            new AgentTool(
                Name: "inspect_data_store_json_file",
                Description:
                    "Inspect a JSON file. Auto-detects shape: NDJSON / JSON Lines (one record per line, streams), " +
                    "JSON array of records, or a single non-tabular object. " +
                    "For NDJSON and arrays returns the union of keys seen with inferred types " +
                    "(bigint / double precision / boolean / timestamptz / text), the first `sampleRecords` records, and (optionally) the total record count. " +
                    "For a single object returns the top-level field list with types and short previews plus max nesting depth. " +
                    "NDJSON streams line-by-line — safe on multi-GB files. Array / single-object modes load the whole document with a 200MB cap. " +
                    "WIDE RECORDS: use `columns: [\"a\", \"b\"]` to restrict sample records + type inference to the keys you care about. " +
                    "When `columns` is omitted, sample records only retain the first `maxColumnsInSample` keys (in the order they're first seen) but " +
                    "all observed keys still appear in the result's columns list. " +
                    "`sampleRecords` defaults to 20, capped at 200. `countTotalRecords: true` walks the whole file (NDJSON / array) bounded to ~5M records. " +
                    "`mode` overrides auto-detection. `maxCellLength` clamps individual scalar/string values (default 200 chars, max 2000).",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "fileId": { "type": "string" },
                        "sampleRecords": { "type": ["integer", "null"], "minimum": 1, "maximum": 200 },
                        "countTotalRecords": { "type": ["boolean", "null"] },
                        "mode": { "type": ["string", "null"], "enum": ["auto", "ndjson", "array", "single", null] },
                        "columns": {
                          "type": ["array", "null"],
                          "items": { "type": "string" },
                          "description": "Specific keys to keep in sample records + type inference. Case-insensitive match."
                        },
                        "maxColumnsInSample": { "type": ["integer", "null"], "minimum": 1, "maximum": 500 },
                        "maxCellLength": { "type": ["integer", "null"], "minimum": 0, "maximum": 2000 }
                      },
                      "required": ["dataStoreId", "fileId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeInspectJsonFileAsync),

            new AgentTool(
                Name: "profile_data_store_json_file",
                Description:
                    "Profile a JSON file to DESCRIBE the shape of the data — same purpose as profile_data_store_csv_file but for JSON. " +
                    "Auto-detects NDJSON / array / single-object. " +
                    "For NDJSON and arrays returns: total record count; key pattern analysis (which keys look like dates, alpha names, ids, etc., " +
                    "with the dominant pattern and the 'odd key out' candidates); per-key statistics for a representative subset of keys " +
                    "(presence count, null count, min / max / mean / distinct / inferred Postgres-style type / sample values); layout hint with conversion recommendations. " +
                    "For single-object mode returns the same structural summary as the inspector (top-level fields, types, previews, nesting depth). " +
                    "NDJSON streams; array / single-object use a 200MB cap. `mode` overrides auto-detection. " +
                    "`profileKeys` deep-profiles specific keys; otherwise the tool auto-picks ~10 representative keys (key-looking + first/last/middle samples).",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "fileId": { "type": "string" },
                        "mode": { "type": ["string", "null"], "enum": ["auto", "ndjson", "array", "single", null] },
                        "profileKeys": {
                          "type": ["array", "null"],
                          "items": { "type": "string" },
                          "description": "Specific JSON keys to deep-profile. Case-insensitive."
                        },
                        "maxProfiledColumns": { "type": ["integer", "null"], "minimum": 1, "maximum": 50 },
                        "maxScannedRows": { "type": ["integer", "null"], "minimum": 1000, "maximum": 5000000 }
                      },
                      "required": ["dataStoreId", "fileId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeProfileJsonFileAsync),

            new AgentTool(
                Name: "inspect_data_store_parquet_file",
                Description:
                    "Inspect a Parquet file. Returns the schema (column name, Parquet CLR type, nullable flag, mapped Postgres-style type), " +
                    "the exact total row count from the file footer (free — no row scan needed), the row group count, " +
                    "and a sample of `sampleRows` rows from the first row group. " +
                    "WIDE FILES: start with `schemaOnly: true` to fetch the schema + counts cheaply (no column reads), then re-call with " +
                    "`columns: [\"col1\", \"col2\"]` to decompress only the columns you need from the first row group. " +
                    "If `columns` is omitted, only the first `maxColumnsInSample` columns are decompressed; the rest still appear in the schema. " +
                    "`sampleRows` defaults to 20, capped at 200; set to 0 to skip sampling. " +
                    "`maxCellLength` clamps cell values (default 200 chars, max 2000). Size cap: 500MB.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "fileId": { "type": "string" },
                        "sampleRows": { "type": ["integer", "null"], "minimum": 0, "maximum": 200 },
                        "schemaOnly": { "type": ["boolean", "null"] },
                        "columns": {
                          "type": ["array", "null"],
                          "items": { "type": "string" },
                          "description": "Specific column names to decompress and include in sample rows. Case-insensitive match."
                        },
                        "maxColumnsInSample": { "type": ["integer", "null"], "minimum": 1, "maximum": 500 },
                        "maxCellLength": { "type": ["integer", "null"], "minimum": 0, "maximum": 2000 }
                      },
                      "required": ["dataStoreId", "fileId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeInspectParquetFileAsync),

            new AgentTool(
                Name: "profile_data_store_parquet_file",
                Description:
                    "Profile a Parquet file to DESCRIBE the shape of the data — same purpose as profile_data_store_csv_file but for Parquet. " +
                    "Cheap by design: Parquet's footer carries exact row count, per-column min / max / null / distinct stats, and schema — no data scan needed. " +
                    "Returns: total row + column count, row group count; column name pattern analysis (dominant pattern + 'odd column out' keys); " +
                    "per-column profile for ~10 representative columns (footer-derived min/max/null/distinct + a handful of sample values decompressed from row group 0); " +
                    "a layout hint with conversion recommendations. " +
                    "Use this for 'tell me about this file' before reaching for inspect_data_store_parquet_file. Size cap: 500MB.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "fileId": { "type": "string" },
                        "profileColumns": {
                          "type": ["array", "null"],
                          "items": { "type": "string" },
                          "description": "Specific column names to deep-profile. Case-insensitive."
                        },
                        "maxProfiledColumns": { "type": ["integer", "null"], "minimum": 1, "maximum": 50 }
                      },
                      "required": ["dataStoreId", "fileId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeProfileParquetFileAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Data stores have two kinds: FileType (folders + files) and SqlType (ingested CSV tables). Per-store View grants filter what shows up — empty list means the user has no grants. Use list_data_stores to find ids, then list_data_store_tables / preview_data_store_table for SqlType inspection or list_data_store_files for FileType browsing. " +
        "File-content tools stream the file rather than loading it whole: peek_data_store_file / inspect_data_store_text_file / inspect_data_store_csv_file are safe on multi-GB inputs and only walk the whole file when the caller opts in via countTotalLines / computeRowCount. " +
        "For XLSX, call inspect_data_store_xlsx_workbook first to see the sheets, then inspect_data_store_xlsx_sheet to inspect one — XLSX parsing loads the workbook into memory and is capped at 150MB. " +
        "For JSON use inspect_data_store_json_file: NDJSON streams safely, array / single-object modes have a 200MB cap. " +
        "For Parquet use inspect_data_store_parquet_file: row count is free (from the footer) and sampling decompresses only the first row group; 500MB cap. " +
        "WIDE FILES (>~100 columns): the tabular inspectors (csv / xlsx_sheet / json / parquet) all accept `schemaOnly: true` for a cheap column list, `columns: [\"a\", \"b\"]` to restrict sample rows + type inference to specific columns (case-insensitive), `maxColumnsInSample` to cap how many columns the sample includes when no column list is given (default 50), and `maxCellLength` to clamp individual cell values (default 200 chars). The full column-name list always comes back so you can see what's available and decide which subset to pull samples for — this is the right workflow for a 1000+-column sensor / feature file that would otherwise blow the LLM payload. " +
        "DESCRIBING A FILE ('tell me about this file', 'what's in here'): prefer the format-specific profile tool — profile_data_store_csv_file for CSV, profile_data_store_xlsx_sheet for XLSX (pass `headerRow` + `descriptionRow` when the sheet has a multi-row banner like a Weather export), profile_data_store_json_file for NDJSON / array / single-object JSON, profile_data_store_parquet_file for Parquet (footer stats are free — almost no data read). All four return: header / key pattern stats, per-column statistics for ~10 representative columns, a layout hint (wide-pivot vs long-table) with the dominant pattern and the 'odd column out' key, and conversion recommendations. Use the result to compose a plain-English summary — don't try to list every column for the user. For point lookups ('what was X for Y on date Z') use lookup_data_store_csv_rows on CSV, or guide the user toward ingesting the file as a SqlType table for AQL. " +
        "All inferred-type strings use the same vocabulary (bigint / double precision / boolean / timestamptz / text) as datasets and CSV ingest, so types can be passed straight through.";

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

    // Bound on how many rows / lines we'll walk for an opt-in total count.
    // Big enough that real-world CSVs (Excel exports, point-in-time exports
    // from operational systems) are usually under the cap; small enough that
    // even a worst-case full-walk completes well inside the chat round-trip
    // budget. Result flags expose whether the cap was hit so the model can
    // tell the user "5,000,000+ rows (cap)" instead of asserting an exact
    // number it didn't actually verify.
    private const long MaxCountedRows = 5_000_000;

    private static async Task<JsonElement> InvokePeekFileAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return Error("peek_data_store_file", "dataStoreId is required.");
        if (!TryReadGuid(args, "fileId", out var fileId))
            return Error("peek_data_store_file", "fileId is required.");
        var maxBytes = ReadInt(args, "maxBytes") ?? 8192;
        if (maxBytes <= 0) maxBytes = 8192;
        if (maxBytes > 65536) maxBytes = 65536;
        var fromEnd = args.TryGetProperty("fromEnd", out var fe) && fe.ValueKind == JsonValueKind.True;

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return Error("peek_data_store_file", $"View permission denied on data store {dataStoreId}.");

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        DataStoreFile metadata;
        Stream content;
        try
        {
            (metadata, content) = await files.DownloadAsync(dataStoreId, fileId, ct);
        }
        catch (FileDataStoreFileNotFoundException)
        {
            return Error("peek_data_store_file", $"File {fileId} not found in data store {dataStoreId}.");
        }

        await using var stream = content;
        // Seek from-end if requested. The current FileDataStoreService backs
        // DownloadAsync with FileStream (seekable); the guard below makes the
        // tool resilient if that ever changes (we just fall back to head).
        if (fromEnd && stream.CanSeek && stream.Length > maxBytes)
            stream.Seek(-maxBytes, SeekOrigin.End);

        var buffer = new byte[maxBytes];
        int total = 0;
        int read;
        while (total < maxBytes
            && (read = await stream.ReadAsync(buffer.AsMemory(total, maxBytes - total), ct)) > 0)
        {
            total += read;
        }

        var (text, isText, encodingNote) = DecodeBytesAsText(buffer, total);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "data_store_file_peek",
            source = "IFileDataStoreService",
            data = new
            {
                fileId,
                filename = metadata.Filename,
                folderPath = metadata.FolderPath,
                contentType = metadata.ContentType,
                sizeBytes = metadata.SizeBytes,
                bytesRead = total,
                fromEnd,
                isText,
                encodingNote,
                content = text,
                truncated = metadata.SizeBytes > total
            }
        });
    }

    private static async Task<JsonElement> InvokeInspectTextFileAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return Error("inspect_data_store_text_file", "dataStoreId is required.");
        if (!TryReadGuid(args, "fileId", out var fileId))
            return Error("inspect_data_store_text_file", "fileId is required.");
        var lineCount = ReadInt(args, "lineCount") ?? 50;
        if (lineCount <= 0) lineCount = 50;
        if (lineCount > 500) lineCount = 500;
        var countTotal = args.TryGetProperty("countTotalLines", out var c) && c.ValueKind == JsonValueKind.True;

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return Error("inspect_data_store_text_file", $"View permission denied on data store {dataStoreId}.");

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        DataStoreFile metadata;
        Stream content;
        try
        {
            (metadata, content) = await files.DownloadAsync(dataStoreId, fileId, ct);
        }
        catch (FileDataStoreFileNotFoundException)
        {
            return Error("inspect_data_store_text_file", $"File {fileId} not found in data store {dataStoreId}.");
        }

        await using var stream = content;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var sample = new List<string>(lineCount);
        long totalLines = 0;
        long longestLine = 0;
        bool totalComplete = true;
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (sample.Count < lineCount) sample.Add(line);
            totalLines++;
            if (line.Length > longestLine) longestLine = line.Length;
            // Sample-only path: bail as soon as we have enough lines.
            if (!countTotal && sample.Count >= lineCount) break;
            if (countTotal && totalLines >= MaxCountedRows)
            {
                totalComplete = false;
                break;
            }
        }

        return JsonSerializer.SerializeToElement(new
        {
            kind = "data_store_text_inspection",
            source = "IFileDataStoreService",
            data = new
            {
                fileId,
                filename = metadata.Filename,
                folderPath = metadata.FolderPath,
                contentType = metadata.ContentType,
                sizeBytes = metadata.SizeBytes,
                encoding = reader.CurrentEncoding.WebName,
                sampleLineCount = sample.Count,
                sampleLines = sample,
                longestSampledLine = longestLine,
                totalLineCount = countTotal ? totalLines : (long?)null,
                totalLineCountComplete = countTotal ? totalComplete : (bool?)null
            }
        });
    }

    private static async Task<JsonElement> InvokeInspectCsvFileAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return Error("inspect_data_store_csv_file", "dataStoreId is required.");
        if (!TryReadGuid(args, "fileId", out var fileId))
            return Error("inspect_data_store_csv_file", "fileId is required.");
        var sampleRows = ReadInt(args, "sampleRows") ?? 20;
        if (sampleRows <= 0) sampleRows = 20;
        if (sampleRows > 200) sampleRows = 200;
        var computeRowCount = args.TryGetProperty("computeRowCount", out var c) && c.ValueKind == JsonValueKind.True;
        var delimiter = ReadString(args, "delimiter") ?? ",";
        if (delimiter.Length == 0 || delimiter.Length > 4)
            return Error("inspect_data_store_csv_file", "delimiter must be 1–4 characters.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return Error("inspect_data_store_csv_file", $"View permission denied on data store {dataStoreId}.");

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        DataStoreFile metadata;
        Stream content;
        try
        {
            (metadata, content) = await files.DownloadAsync(dataStoreId, fileId, ct);
        }
        catch (FileDataStoreFileNotFoundException)
        {
            return Error("inspect_data_store_csv_file", $"File {fileId} not found in data store {dataStoreId}.");
        }

        await using var stream = content;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null,
            Delimiter = delimiter
        };
        using var csv = new CsvReader(reader, csvConfig);

        // Header. An empty file or one without a header row gets a clean error
        // — different from a row-parse error, so the model can tell the user
        // "this file has no header" instead of "csv broken."
        try
        {
            if (!await csv.ReadAsync()) return Error("inspect_data_store_csv_file", "File is empty.");
            csv.ReadHeader();
        }
        catch (CsvHelperException ex)
        {
            return Error("inspect_data_store_csv_file", "Header parse failed: " + ex.Message);
        }
        var headers = csv.HeaderRecord ?? Array.Empty<string>();
        if (headers.Length == 0)
            return Error("inspect_data_store_csv_file", "No columns parsed from header row.");

        var sel = ReadColumnSelection(args);
        var (includedSet, orderedIncluded, unknownColumns, columnsTruncated) =
            ResolveIncludedColumns(headers, sel);
        if (sel.RequestedColumns is not null && includedSet.Count == 0)
            return Error("inspect_data_store_csv_file",
                $"None of the requested columns matched the file's headers. Unknown: {string.Join(", ", unknownColumns)}.");

        // schemaOnly skips data-row reads entirely — cheapest path for getting
        // a column list from a wide file before deciding which columns matter.
        if (sel.SchemaOnly)
        {
            return JsonSerializer.SerializeToElement(new
            {
                kind = "data_store_csv_inspection",
                source = "IFileDataStoreService + CsvHelper",
                data = new
                {
                    fileId,
                    filename = metadata.Filename,
                    folderPath = metadata.FolderPath,
                    contentType = metadata.ContentType,
                    sizeBytes = metadata.SizeBytes,
                    delimiter,
                    mode = "schemaOnly",
                    columnCount = headers.Length,
                    columns = headers.Select((h, i) => new
                    {
                        name = h,
                        index = i,
                        inferredType = (string?)null
                    }).ToList(),
                    sampleRowCount = 0,
                    sampleRows = Array.Empty<object>(),
                    sampleColumnsTruncated = false,
                    unknownRequestedColumns = unknownColumns
                }
            });
        }

        // Per-column sample lists for type inference. Only allocate for the
        // included columns so a 1800-column file with `columns: [...]` set
        // doesn't pay the memory cost of 1800 inference buckets.
        var sampleByCol = new Dictionary<int, List<string?>>(includedSet.Count);
        foreach (var idx in includedSet) sampleByCol[idx] = new List<string?>(sampleRows);
        var sampleRowsOut = new List<Dictionary<string, string?>>(sampleRows);

        long totalRows = 0;
        bool totalComplete = true;
        try
        {
            while (await csv.ReadAsync())
            {
                if (sampleRowsOut.Count < sampleRows)
                {
                    var row = new Dictionary<string, string?>(includedSet.Count, StringComparer.Ordinal);
                    foreach (var i in orderedIncluded)
                    {
                        string? v = null;
                        try { v = csv.GetField(i); }
                        catch (CsvHelperException) { v = null; }
                        var truncated = TruncateCell(v, sel.MaxCellLength);
                        row[headers[i]] = truncated;
                        sampleByCol[i].Add(v);
                    }
                    sampleRowsOut.Add(row);
                }
                totalRows++;
                if (!computeRowCount && sampleRowsOut.Count >= sampleRows) break;
                if (computeRowCount && totalRows >= MaxCountedRows)
                {
                    totalComplete = false;
                    break;
                }
            }
        }
        catch (CsvHelperException ex)
        {
            return JsonSerializer.SerializeToElement(new
            {
                kind = "data_store_csv_inspection",
                source = "IFileDataStoreService + CsvHelper",
                data = new
                {
                    fileId,
                    filename = metadata.Filename,
                    folderPath = metadata.FolderPath,
                    contentType = metadata.ContentType,
                    sizeBytes = metadata.SizeBytes,
                    delimiter,
                    columnCount = headers.Length,
                    columns = BuildCsvColumns(headers, includedSet, sampleByCol),
                    sampleRowCount = sampleRowsOut.Count,
                    sampleRows = sampleRowsOut,
                    sampleColumnsTruncated = columnsTruncated,
                    unknownRequestedColumns = unknownColumns,
                    totalRowCount = computeRowCount ? totalRows : (long?)null,
                    totalRowCountComplete = computeRowCount ? false : (bool?)null,
                    parseError = ex.Message
                }
            });
        }

        return JsonSerializer.SerializeToElement(new
        {
            kind = "data_store_csv_inspection",
            source = "IFileDataStoreService + CsvHelper",
            data = new
            {
                fileId,
                filename = metadata.Filename,
                folderPath = metadata.FolderPath,
                contentType = metadata.ContentType,
                sizeBytes = metadata.SizeBytes,
                delimiter,
                columnCount = headers.Length,
                columns = BuildCsvColumns(headers, includedSet, sampleByCol),
                sampleRowCount = sampleRowsOut.Count,
                sampleRows = sampleRowsOut,
                sampleColumnsTruncated = columnsTruncated,
                sampleColumnsIncluded = orderedIncluded.Select(i => headers[i]).ToList(),
                unknownRequestedColumns = unknownColumns,
                totalRowCount = computeRowCount ? totalRows : (long?)null,
                totalRowCountComplete = computeRowCount ? totalComplete : (bool?)null
            }
        });
    }

    // Build the columns[] block for the CSV result. Every header appears (so
    // the model always sees the full column list); inferredType is populated
    // only for columns we actually sampled.
    private static List<object> BuildCsvColumns(
        IReadOnlyList<string> headers,
        HashSet<int> includedSet,
        Dictionary<int, List<string?>> sampleByCol)
    {
        var cols = new List<object>(headers.Count);
        for (int i = 0; i < headers.Count; i++)
        {
            string? inferredType = null;
            if (includedSet.Contains(i) && sampleByCol.TryGetValue(i, out var samples))
                inferredType = InferColumnType(samples);
            cols.Add(new
            {
                name = headers[i],
                index = i,
                inferredType
            });
        }
        return cols;
    }

    // ClosedXML loads the workbook into memory. Anything beyond this cap is
    // refused with a hint to convert to CSV — we'd rather give a precise error
    // than OOM the process on a 1GB xlsx.
    private const long MaxXlsxBytes = 150L * 1024 * 1024;

    private static async Task<JsonElement> InvokeInspectXlsxWorkbookAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "inspect_data_store_xlsx_workbook";
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return Error(action, "dataStoreId is required.");
        if (!TryReadGuid(args, "fileId", out var fileId))
            return Error(action, "fileId is required.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return Error(action, $"View permission denied on data store {dataStoreId}.");

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        DataStoreFile metadata;
        Stream content;
        try
        {
            (metadata, content) = await files.DownloadAsync(dataStoreId, fileId, ct);
        }
        catch (FileDataStoreFileNotFoundException)
        {
            return Error(action, $"File {fileId} not found in data store {dataStoreId}.");
        }

        if (metadata.SizeBytes > MaxXlsxBytes)
        {
            await content.DisposeAsync();
            return Error(action,
                $"File is {metadata.SizeBytes:N0} bytes; XLSX inspection caps at {MaxXlsxBytes:N0} bytes. Save each sheet as CSV (Excel: File → Save As → CSV UTF-8) and re-upload, then use inspect_data_store_csv_file.");
        }

        await using var stream = content;
        XLWorkbook wb;
        try
        {
            wb = new XLWorkbook(stream);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error(action, "Workbook open failed: " + ex.Message);
        }
        using (wb)
        {
            try
            {
                var sheets = new List<object>(wb.Worksheets.Count);
                foreach (var ws in wb.Worksheets)
                {
                    // RangeUsed walks cell metadata to find the bounding box.
                    // For an empty sheet it returns null; we report zero
                    // dimensions in that case so the model can still tell the
                    // user "the sheet exists but is empty."
                    var range = ws.RangeUsed();
                    var rowCount = range?.LastRow().RowNumber() ?? 0;
                    var colCount = range?.LastColumn().ColumnNumber() ?? 0;
                    sheets.Add(new
                    {
                        position = ws.Position,
                        name = ws.Name,
                        rowCount,
                        columnCount = colCount,
                        isHidden = ws.Visibility != XLWorksheetVisibility.Visible
                    });
                }
                return JsonSerializer.SerializeToElement(new
                {
                    kind = "data_store_xlsx_workbook",
                    source = "IFileDataStoreService + ClosedXML",
                    data = new
                    {
                        fileId,
                        filename = metadata.Filename,
                        folderPath = metadata.FolderPath,
                        contentType = metadata.ContentType,
                        sizeBytes = metadata.SizeBytes,
                        sheetCount = sheets.Count,
                        sheets
                    }
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Error(action, "Workbook parse failed: " + ex.Message);
            }
        }
    }

    private static async Task<JsonElement> InvokeInspectXlsxSheetAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "inspect_data_store_xlsx_sheet";
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return Error(action, "dataStoreId is required.");
        if (!TryReadGuid(args, "fileId", out var fileId))
            return Error(action, "fileId is required.");
        var sheetName = ReadString(args, "sheetName");
        var sheetIndex = ReadInt(args, "sheetIndex");
        var headerRow = ReadInt(args, "headerRow") ?? 1;
        if (headerRow <= 0) headerRow = 1;
        var sampleRows = ReadInt(args, "sampleRows") ?? 20;
        if (sampleRows <= 0) sampleRows = 20;
        if (sampleRows > 200) sampleRows = 200;
        if (!string.IsNullOrWhiteSpace(sheetName) && sheetIndex.HasValue)
            return Error(action, "Specify sheetName or sheetIndex, not both.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return Error(action, $"View permission denied on data store {dataStoreId}.");

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        DataStoreFile metadata;
        Stream content;
        try
        {
            (metadata, content) = await files.DownloadAsync(dataStoreId, fileId, ct);
        }
        catch (FileDataStoreFileNotFoundException)
        {
            return Error(action, $"File {fileId} not found in data store {dataStoreId}.");
        }

        if (metadata.SizeBytes > MaxXlsxBytes)
        {
            await content.DisposeAsync();
            return Error(action,
                $"File is {metadata.SizeBytes:N0} bytes; XLSX inspection caps at {MaxXlsxBytes:N0} bytes. Save the sheet as CSV and use inspect_data_store_csv_file.");
        }

        await using var stream = content;
        XLWorkbook wb;
        try
        {
            wb = new XLWorkbook(stream);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error(action, "Workbook open failed: " + ex.Message);
        }
        using (wb)
        {
            IXLWorksheet? sheet;
            if (!string.IsNullOrWhiteSpace(sheetName))
            {
                sheet = wb.Worksheets.FirstOrDefault(w =>
                    string.Equals(w.Name, sheetName, StringComparison.OrdinalIgnoreCase));
                if (sheet is null)
                {
                    var available = string.Join(", ", wb.Worksheets.Select(w => $"'{w.Name}'"));
                    return Error(action, $"Sheet '{sheetName}' not found. Available: {available}.");
                }
            }
            else if (sheetIndex.HasValue)
            {
                var idx = sheetIndex.Value;
                if (idx < 1 || idx > wb.Worksheets.Count)
                    return Error(action, $"sheetIndex {idx} out of range; workbook has {wb.Worksheets.Count} sheet(s).");
                sheet = wb.Worksheets.Worksheet(idx);
            }
            else
            {
                sheet = wb.Worksheets.FirstOrDefault();
                if (sheet is null)
                    return Error(action, "Workbook has no sheets.");
            }

            try
            {
                var range = sheet.RangeUsed();
                if (range is null)
                {
                    return JsonSerializer.SerializeToElement(new
                    {
                        kind = "data_store_xlsx_sheet",
                        source = "IFileDataStoreService + ClosedXML",
                        data = new
                        {
                            fileId,
                            filename = metadata.Filename,
                            folderPath = metadata.FolderPath,
                            contentType = metadata.ContentType,
                            sizeBytes = metadata.SizeBytes,
                            sheetName = sheet.Name,
                            sheetPosition = sheet.Position,
                            isHidden = sheet.Visibility != XLWorksheetVisibility.Visible,
                            totalRowCount = 0,
                            totalColumnCount = 0,
                            headerRow,
                            columnCount = 0,
                            columns = Array.Empty<object>(),
                            sampleRowCount = 0,
                            sampleRows = Array.Empty<object>(),
                            note = "Sheet has no used range — it's empty."
                        }
                    });
                }
                var firstRow = range.FirstRow().RowNumber();
                var lastRow = range.LastRow().RowNumber();
                var lastCol = range.LastColumn().ColumnNumber();

                if (headerRow < firstRow || headerRow > lastRow)
                    return Error(action,
                        $"headerRow {headerRow} is outside the used range (rows {firstRow}–{lastRow}).");

                // Build header list. Empty header cells get an "col_N" name so
                // the row-object keys stay non-empty — same fallback the CSV
                // ingestor uses for unnamed columns.
                var headers = new List<string>(lastCol);
                var headerOriginals = new List<string>(lastCol);
                for (int c = 1; c <= lastCol; c++)
                {
                    var raw = sheet.Cell(headerRow, c).GetString();
                    headerOriginals.Add(raw);
                    headers.Add(string.IsNullOrWhiteSpace(raw) ? $"col_{c}" : raw.Trim());
                }

                var sel = ReadColumnSelection(args);
                var (includedSet, orderedIncluded, unknownColumns, columnsTruncated) =
                    ResolveIncludedColumns(headers, sel);
                if (sel.RequestedColumns is not null && includedSet.Count == 0)
                    return Error(action,
                        $"None of the requested columns matched the sheet's headers. Unknown: {string.Join(", ", unknownColumns)}.");

                // schemaOnly skips data reads entirely — cheapest path on a
                // very wide sheet.
                if (sel.SchemaOnly)
                {
                    var totalDataRowsSchema = Math.Max(0, lastRow - (headerRow + 1) + 1);
                    return JsonSerializer.SerializeToElement(new
                    {
                        kind = "data_store_xlsx_sheet",
                        source = "IFileDataStoreService + ClosedXML",
                        data = new
                        {
                            fileId,
                            filename = metadata.Filename,
                            folderPath = metadata.FolderPath,
                            contentType = metadata.ContentType,
                            sizeBytes = metadata.SizeBytes,
                            sheetName = sheet.Name,
                            sheetPosition = sheet.Position,
                            isHidden = sheet.Visibility != XLWorksheetVisibility.Visible,
                            firstUsedRow = firstRow,
                            lastUsedRow = lastRow,
                            totalRowCount = totalDataRowsSchema,
                            totalColumnCount = lastCol,
                            headerRow,
                            mode = "schemaOnly",
                            columnCount = headers.Count,
                            columns = headers.Select((h, i) => new
                            {
                                name = h,
                                originalHeader = headerOriginals[i],
                                columnLetter = ColumnLetter(i + 1),
                                inferredType = (string?)null
                            }).ToList(),
                            sampleRowCount = 0,
                            sampleRows = Array.Empty<object>(),
                            unknownRequestedColumns = unknownColumns
                        }
                    });
                }

                // Read sample rows after the header. We track display strings
                // (Excel-formatted, friendly to the user) and inference
                // strings (canonical, friendly to the type detector) in
                // parallel. Only the selected columns get sampled — wide
                // sheets with `columns: [...]` pay only for the chosen ones.
                var dataStart = headerRow + 1;
                var sampleByCol = new Dictionary<int, List<string?>>(includedSet.Count);
                foreach (var idx in includedSet) sampleByCol[idx] = new List<string?>(sampleRows);
                var sampleRowsOut = new List<Dictionary<string, string?>>(sampleRows);

                for (int r = dataStart; r <= lastRow && sampleRowsOut.Count < sampleRows; r++)
                {
                    ct.ThrowIfCancellationRequested();
                    var row = new Dictionary<string, string?>(includedSet.Count, StringComparer.Ordinal);
                    // Blank-row detection still walks every cell — checking only
                    // the included subset would let an otherwise-empty row past
                    // when the user filters down to a sparse column.
                    bool allEmpty = true;
                    for (int c = 1; c <= lastCol; c++)
                    {
                        var cell = sheet.Cell(r, c);
                        var inferenceVal = XlsxCellToInferenceString(cell);
                        if (!string.IsNullOrEmpty(inferenceVal)) allEmpty = false;
                        var idx = c - 1;
                        if (!includedSet.Contains(idx)) continue;
                        var displayVal = cell.GetFormattedString();
                        row[headers[idx]] = TruncateCell(displayVal, sel.MaxCellLength);
                        sampleByCol[idx].Add(inferenceVal);
                    }
                    if (allEmpty) continue;
                    sampleRowsOut.Add(row);
                }

                var totalDataRows = Math.Max(0, lastRow - dataStart + 1);

                var columns = headers.Select((h, i) =>
                {
                    string? inferredType = null;
                    if (includedSet.Contains(i) && sampleByCol.TryGetValue(i, out var s))
                        inferredType = InferColumnType(s);
                    return (object)new
                    {
                        name = h,
                        originalHeader = headerOriginals[i],
                        columnLetter = ColumnLetter(i + 1),
                        inferredType
                    };
                }).ToList();

                return JsonSerializer.SerializeToElement(new
                {
                    kind = "data_store_xlsx_sheet",
                    source = "IFileDataStoreService + ClosedXML",
                    data = new
                    {
                        fileId,
                        filename = metadata.Filename,
                        folderPath = metadata.FolderPath,
                        contentType = metadata.ContentType,
                        sizeBytes = metadata.SizeBytes,
                        sheetName = sheet.Name,
                        sheetPosition = sheet.Position,
                        isHidden = sheet.Visibility != XLWorksheetVisibility.Visible,
                        firstUsedRow = firstRow,
                        lastUsedRow = lastRow,
                        totalRowCount = totalDataRows,
                        totalColumnCount = lastCol,
                        headerRow,
                        columnCount = headers.Count,
                        columns,
                        sampleRowCount = sampleRowsOut.Count,
                        sampleRows = sampleRowsOut,
                        sampleColumnsTruncated = columnsTruncated,
                        sampleColumnsIncluded = orderedIncluded.Select(i => headers[i]).ToList(),
                        unknownRequestedColumns = unknownColumns
                    }
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Error(action, "Sheet parse failed: " + ex.Message);
            }
        }
    }

    // ClosedXML's XLCellValue holds the typed cell value (number / date /
    // bool / text / blank / error). Stringify it in canonical, invariant form
    // so InferColumnType sees a consistent shape — ISO dates, R-format
    // numbers, lowercase true/false — regardless of the sheet's display
    // formatting.
    private static string XlsxCellToInferenceString(IXLCell cell)
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

    // 1 → A, 27 → AA, 53 → BA. Saves callers a round-trip to look up the
    // Excel column letter when they want to reference a cell from chat.
    private static string ColumnLetter(int col)
    {
        var sb = new StringBuilder();
        while (col > 0)
        {
            col--;
            sb.Insert(0, (char)('A' + col % 26));
            col /= 26;
        }
        return sb.ToString();
    }

    // NDJSON streams; array / single-object loads the whole doc through
    // JsonDocument and so needs a memory cap. 200MB matches the xlsx scale —
    // anything that wants to hold more bytes in RAM than that should be NDJSON
    // (which the model can convert the user toward in chat).
    private const long MaxJsonNonStreamingBytes = 200L * 1024 * 1024;

    // Parquet inspection decompresses one row group's worth into memory plus
    // schema/footer. Higher cap than xlsx/json because Parquet is heavily
    // compressed on disk and the footer-only metadata path is cheap.
    private const long MaxParquetBytes = 500L * 1024 * 1024;

    private static async Task<JsonElement> InvokeInspectJsonFileAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "inspect_data_store_json_file";
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return Error(action, "dataStoreId is required.");
        if (!TryReadGuid(args, "fileId", out var fileId))
            return Error(action, "fileId is required.");
        var sampleRecords = ReadInt(args, "sampleRecords") ?? 20;
        if (sampleRecords <= 0) sampleRecords = 20;
        if (sampleRecords > 200) sampleRecords = 200;
        var countTotal = args.TryGetProperty("countTotalRecords", out var c) && c.ValueKind == JsonValueKind.True;
        var modeArg = (ReadString(args, "mode") ?? "auto").ToLowerInvariant();
        if (modeArg is not ("auto" or "ndjson" or "array" or "single"))
            return Error(action, "mode must be 'auto', 'ndjson', 'array', or 'single'.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return Error(action, $"View permission denied on data store {dataStoreId}.");

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        DataStoreFile metadata;
        Stream content;
        try
        {
            (metadata, content) = await files.DownloadAsync(dataStoreId, fileId, ct);
        }
        catch (FileDataStoreFileNotFoundException)
        {
            return Error(action, $"File {fileId} not found in data store {dataStoreId}.");
        }

        await using var stream = content;

        string mode = modeArg;
        string? detectionNote = null;
        if (mode == "auto")
        {
            if (!stream.CanSeek)
                return Error(action, "Cannot auto-detect JSON mode on non-seekable stream; specify `mode` explicitly.");
            mode = await DetectJsonModeAsync(stream, ct);
            stream.Seek(0, SeekOrigin.Begin);
            detectionNote = $"auto-detected as {mode}";
            if (mode == "unknown")
                return Error(action, "Could not classify the JSON shape from the first 64KB. Pass `mode` explicitly.");
        }

        var sel = ReadColumnSelection(args);
        return mode switch
        {
            "ndjson" => await InspectJsonNdjsonAsync(stream, metadata, fileId, sampleRecords, countTotal, detectionNote, sel, ct),
            "array" => await InspectJsonArrayAsync(stream, metadata, fileId, sampleRecords, countTotal, detectionNote, sel, ct),
            // schemaOnly / columns / cell truncation don't apply to a single
            // non-tabular object — the result is already a tiny structural
            // summary, not row data.
            "single" => await InspectJsonSingleAsync(stream, metadata, fileId, detectionNote, ct),
            _ => Error(action, $"Unknown JSON mode '{mode}'.")
        };
    }

    // Peek-based mode detection. JSON starting with '[' is an array. JSON
    // starting with '{' is single-object UNLESS we see another top-level
    // value following the first one within the peek window — that's NDJSON.
    // Bracket-depth scan respects strings + escapes so we don't get confused
    // by braces inside string values.
    private static async Task<string> DetectJsonModeAsync(Stream stream, CancellationToken ct)
    {
        var peekSize = (int)Math.Min(stream.Length, 65536);
        if (peekSize == 0) return "unknown";
        var buffer = new byte[peekSize];
        int total = 0;
        int read;
        while (total < peekSize
            && (read = await stream.ReadAsync(buffer.AsMemory(total, peekSize - total), ct)) > 0)
            total += read;

        int i = 0;
        while (i < total && IsJsonWhitespace(buffer[i])) i++;
        if (i >= total) return "unknown";
        if (buffer[i] == '[') return "array";
        if (buffer[i] != '{') return "unknown";

        int depth = 0;
        bool inString = false;
        bool escape = false;
        for (int j = i; j < total; j++)
        {
            byte b = buffer[j];
            if (escape) { escape = false; continue; }
            if (inString)
            {
                if (b == '\\') escape = true;
                else if (b == '"') inString = false;
                continue;
            }
            if (b == '"') { inString = true; continue; }
            if (b == '{' || b == '[') depth++;
            else if (b == '}' || b == ']')
            {
                depth--;
                if (depth == 0)
                {
                    // Closed the first top-level value. Look ahead for the
                    // start of another JSON value; if we find one, this is
                    // NDJSON (or a concatenated JSON stream, which is shape-
                    // equivalent for sampling).
                    int k = j + 1;
                    while (k < total && IsJsonWhitespace(buffer[k])) k++;
                    if (k >= total) return "single";
                    return buffer[k] == '{' ? "ndjson" : "single";
                }
            }
        }
        // First object didn't close within the peek window — definitely a
        // single (very large) object. NDJSON's per-line records are much
        // smaller than 64KB in practice.
        return "single";
    }

    private static bool IsJsonWhitespace(byte b) =>
        b == ' ' || b == '\t' || b == '\r' || b == '\n';

    // Decide whether a JSON record key should be included in sample-record
    // output + type inference. When the caller supplied `columns`, only keys
    // matching the case-insensitive allow-list pass. Otherwise we accept up
    // to maxColumnsInSample keys in first-seen order — keys already accepted
    // always continue to pass.
    private static bool ShouldIncludeJsonKey(
        string propName,
        HashSet<string>? allowedLower,
        HashSet<string> alreadyIncluded,
        int maxColumnsInSample)
    {
        if (allowedLower is not null)
            return allowedLower.Contains(propName.ToLowerInvariant());
        if (alreadyIncluded.Contains(propName)) return true;
        return alreadyIncluded.Count < maxColumnsInSample;
    }

    private static async Task<JsonElement> InspectJsonNdjsonAsync(
        Stream stream, DataStoreFile metadata, Guid fileId, int sampleRecords, bool countTotal,
        string? detectionNote, ColumnSelectionParams sel, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var orderedKeys = new List<string>();
        var keysSeen = new HashSet<string>(StringComparer.Ordinal);
        var includedKeys = new HashSet<string>(StringComparer.Ordinal);
        var sampleByKey = new Dictionary<string, List<string?>>(StringComparer.Ordinal);
        // Sample records are now string-keyed truncated values. We give up the
        // original JsonElement nesting (which let nested objects render as JSON
        // in the output) in exchange for predictable size — a 1.8k-key record
        // with structured nesting blew the LLM payload, so capping per-cell
        // length is necessary even for record-shaped JSON.
        var samples = new List<Dictionary<string, string?>>(sampleRecords);

        var allowedLower = sel.RequestedColumns is not null
            ? new HashSet<string>(sel.RequestedColumns.Select(k => k.ToLowerInvariant()), StringComparer.Ordinal)
            : null;

        long totalRecords = 0;
        bool totalComplete = true;
        long parseErrors = 0;
        string? firstParseError = null;
        bool sawNonObject = false;

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonDocument? doc = null;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException ex)
            {
                parseErrors++;
                firstParseError ??= ex.Message;
                continue;
            }

            using (doc)
            {
                if (samples.Count < sampleRecords && !sel.SchemaOnly)
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        var rec = new Dictionary<string, string?>(StringComparer.Ordinal);
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            bool firstSeen = keysSeen.Add(prop.Name);
                            if (firstSeen) orderedKeys.Add(prop.Name);

                            bool include = ShouldIncludeJsonKey(
                                prop.Name, allowedLower, includedKeys, sel.MaxColumnsInSample);
                            if (!include) continue;

                            if (includedKeys.Add(prop.Name))
                            {
                                sampleByKey[prop.Name] = new List<string?>(sampleRecords);
                                // Back-pad earlier sample records with null
                                // for the new key so per-column samples stay
                                // row-aligned.
                                for (int p = 0; p < samples.Count; p++) sampleByKey[prop.Name].Add(null);
                            }
                            var inf = JsonElementToInferenceString(prop.Value);
                            sampleByKey[prop.Name].Add(inf);
                            rec[prop.Name] = TruncateCell(inf, sel.MaxCellLength);
                        }
                        foreach (var k in includedKeys)
                            if (!rec.ContainsKey(k)) sampleByKey[k].Add(null);
                        samples.Add(rec);
                    }
                    else
                    {
                        sawNonObject = true;
                        samples.Add(new Dictionary<string, string?>(StringComparer.Ordinal)
                        {
                            ["__value"] = TruncateCell(JsonElementToInferenceString(doc.RootElement), sel.MaxCellLength)
                        });
                    }
                }
                else if (sel.SchemaOnly)
                {
                    // schemaOnly still scans keys so the full key union is
                    // reported. We only skip sample collection + inference.
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in doc.RootElement.EnumerateObject())
                            if (keysSeen.Add(prop.Name)) orderedKeys.Add(prop.Name);
                    }
                    else sawNonObject = true;
                }
                totalRecords++;
            }

            if (!countTotal && !sel.SchemaOnly && samples.Count >= sampleRecords) break;
            if (countTotal && totalRecords >= MaxCountedRows) { totalComplete = false; break; }
        }

        var columns = orderedKeys.Select(k => new
        {
            name = k,
            included = includedKeys.Contains(k),
            inferredType = sampleByKey.TryGetValue(k, out var s) ? InferColumnType(s) : null
        }).ToList();

        var unknownColumns = sel.RequestedColumns is null
            ? new List<string>()
            : sel.RequestedColumns.Where(r => !keysSeen.Any(k => string.Equals(k, r, StringComparison.OrdinalIgnoreCase))).ToList();

        return JsonSerializer.SerializeToElement(new
        {
            kind = "data_store_json_inspection",
            source = "IFileDataStoreService",
            data = new
            {
                fileId,
                filename = metadata.Filename,
                folderPath = metadata.FolderPath,
                contentType = metadata.ContentType,
                sizeBytes = metadata.SizeBytes,
                shape = "ndjson",
                mode = sel.SchemaOnly ? "schemaOnly" : "full",
                detectionNote,
                columnCount = columns.Count,
                columns,
                sawNonObjectRecord = sawNonObject,
                sampleRecordCount = samples.Count,
                sampleRecords = samples,
                sampleColumnsIncluded = includedKeys.ToList(),
                sampleColumnsTruncated = sel.RequestedColumns is null && keysSeen.Count > sel.MaxColumnsInSample,
                unknownRequestedColumns = unknownColumns,
                totalRecordCount = countTotal ? totalRecords : (long?)null,
                totalRecordCountComplete = countTotal ? totalComplete : (bool?)null,
                parseErrorCount = parseErrors,
                firstParseError
            }
        });
    }

    private static async Task<JsonElement> InspectJsonArrayAsync(
        Stream stream, DataStoreFile metadata, Guid fileId, int sampleRecords, bool countTotal,
        string? detectionNote, ColumnSelectionParams sel, CancellationToken ct)
    {
        const string action = "inspect_data_store_json_file";
        if (metadata.SizeBytes > MaxJsonNonStreamingBytes)
            return Error(action,
                $"File is {metadata.SizeBytes:N0} bytes; JSON array mode caps at {MaxJsonNonStreamingBytes:N0} bytes. Convert to NDJSON (one JSON value per line, no surrounding [ ]) to stream the whole file.");

        JsonDocument doc;
        try
        {
            doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (JsonException ex)
        {
            return Error(action, "JSON parse failed: " + ex.Message);
        }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return Error(action, $"Expected JSON array, got {doc.RootElement.ValueKind}. Pass `mode` explicitly.");

            var orderedKeys = new List<string>();
            var keysSeen = new HashSet<string>(StringComparer.Ordinal);
            var includedKeys = new HashSet<string>(StringComparer.Ordinal);
            var sampleByKey = new Dictionary<string, List<string?>>(StringComparer.Ordinal);
            var samples = new List<Dictionary<string, string?>>(sampleRecords);
            bool sawNonObject = false;
            long totalRecords = doc.RootElement.GetArrayLength();

            var allowedLower = sel.RequestedColumns is not null
                ? new HashSet<string>(sel.RequestedColumns.Select(k => k.ToLowerInvariant()), StringComparer.Ordinal)
                : null;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    // Always register keys so the column list is exhaustive
                    // even when sample collection has hit its cap.
                    foreach (var prop in item.EnumerateObject())
                        if (keysSeen.Add(prop.Name)) orderedKeys.Add(prop.Name);
                }
                else
                {
                    sawNonObject = true;
                }

                if (sel.SchemaOnly || samples.Count >= sampleRecords) continue;

                if (item.ValueKind == JsonValueKind.Object)
                {
                    var rec = new Dictionary<string, string?>(StringComparer.Ordinal);
                    foreach (var prop in item.EnumerateObject())
                    {
                        if (!ShouldIncludeJsonKey(prop.Name, allowedLower, includedKeys, sel.MaxColumnsInSample))
                            continue;
                        if (includedKeys.Add(prop.Name))
                        {
                            sampleByKey[prop.Name] = new List<string?>(sampleRecords);
                            for (int p = 0; p < samples.Count; p++) sampleByKey[prop.Name].Add(null);
                        }
                        var inf = JsonElementToInferenceString(prop.Value);
                        sampleByKey[prop.Name].Add(inf);
                        rec[prop.Name] = TruncateCell(inf, sel.MaxCellLength);
                    }
                    foreach (var k in includedKeys)
                        if (!rec.ContainsKey(k)) sampleByKey[k].Add(null);
                    samples.Add(rec);
                }
                else
                {
                    samples.Add(new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["__value"] = TruncateCell(JsonElementToInferenceString(item), sel.MaxCellLength)
                    });
                }
            }

            var columns = orderedKeys.Select(k => new
            {
                name = k,
                included = includedKeys.Contains(k),
                inferredType = sampleByKey.TryGetValue(k, out var s) ? InferColumnType(s) : null
            }).ToList();

            var unknownColumns = sel.RequestedColumns is null
                ? new List<string>()
                : sel.RequestedColumns.Where(r => !keysSeen.Any(k => string.Equals(k, r, StringComparison.OrdinalIgnoreCase))).ToList();

            return JsonSerializer.SerializeToElement(new
            {
                kind = "data_store_json_inspection",
                source = "IFileDataStoreService",
                data = new
                {
                    fileId,
                    filename = metadata.Filename,
                    folderPath = metadata.FolderPath,
                    contentType = metadata.ContentType,
                    sizeBytes = metadata.SizeBytes,
                    shape = "array",
                    mode = sel.SchemaOnly ? "schemaOnly" : "full",
                    detectionNote,
                    columnCount = columns.Count,
                    columns,
                    sawNonObjectRecord = sawNonObject,
                    sampleRecordCount = samples.Count,
                    sampleRecords = samples,
                    sampleColumnsIncluded = includedKeys.ToList(),
                    sampleColumnsTruncated = sel.RequestedColumns is null && keysSeen.Count > sel.MaxColumnsInSample,
                    unknownRequestedColumns = unknownColumns,
                    totalRecordCount = countTotal ? totalRecords : (long?)null,
                    // Array total comes from GetArrayLength which is always
                    // exact — no streaming cap applies here.
                    totalRecordCountComplete = countTotal ? true : (bool?)null
                }
            });
        }
    }

    private static async Task<JsonElement> InspectJsonSingleAsync(
        Stream stream, DataStoreFile metadata, Guid fileId, string? detectionNote, CancellationToken ct)
    {
        const string action = "inspect_data_store_json_file";
        if (metadata.SizeBytes > MaxJsonNonStreamingBytes)
            return Error(action,
                $"File is {metadata.SizeBytes:N0} bytes; single-object mode caps at {MaxJsonNonStreamingBytes:N0} bytes.");

        JsonDocument doc;
        try
        {
            doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (JsonException ex)
        {
            return Error(action, "JSON parse failed: " + ex.Message);
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                var topLevelFields = new List<object>();
                foreach (var prop in root.EnumerateObject())
                {
                    topLevelFields.Add(new
                    {
                        name = prop.Name,
                        valueKind = JsonElementTypeName(prop.Value),
                        preview = JsonElementPreview(prop.Value, 120)
                    });
                }
                return JsonSerializer.SerializeToElement(new
                {
                    kind = "data_store_json_inspection",
                    source = "IFileDataStoreService",
                    data = new
                    {
                        fileId,
                        filename = metadata.Filename,
                        folderPath = metadata.FolderPath,
                        contentType = metadata.ContentType,
                        sizeBytes = metadata.SizeBytes,
                        shape = "single-object",
                        detectionNote,
                        rootValueKind = JsonElementTypeName(root),
                        topLevelFieldCount = topLevelFields.Count,
                        topLevelFields,
                        maxDepth = JsonMaxDepth(root)
                    }
                });
            }
            return JsonSerializer.SerializeToElement(new
            {
                kind = "data_store_json_inspection",
                source = "IFileDataStoreService",
                data = new
                {
                    fileId,
                    filename = metadata.Filename,
                    folderPath = metadata.FolderPath,
                    contentType = metadata.ContentType,
                    sizeBytes = metadata.SizeBytes,
                    shape = "scalar",
                    detectionNote,
                    rootValueKind = JsonElementTypeName(root),
                    valuePreview = JsonElementPreview(root, 200)
                }
            });
        }
    }

    private static string? JsonElementToInferenceString(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => null,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => el.GetRawText(),
        JsonValueKind.String => el.GetString(),
        // Nested values: feed the raw JSON to the type sniffer. It won't match
        // any scalar type and will land as "text" — which is the right answer
        // for downstream-dataset/Postgres-ingest purposes (these end up as
        // jsonb-or-text columns).
        _ => el.GetRawText()
    };

    private static string JsonElementTypeName(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Object => "object",
        JsonValueKind.Array => "array[" + el.GetArrayLength().ToString(CultureInfo.InvariantCulture) + "]",
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Null => "null",
        _ => "unknown"
    };

    private static string JsonElementPreview(JsonElement el, int maxLen)
    {
        var raw = el.GetRawText();
        return raw.Length <= maxLen ? raw : raw[..maxLen] + "…";
    }

    private static int JsonMaxDepth(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                {
                    int max = 0;
                    foreach (var p in el.EnumerateObject())
                    {
                        var d = JsonMaxDepth(p.Value);
                        if (d > max) max = d;
                    }
                    return 1 + max;
                }
            case JsonValueKind.Array:
                {
                    int max = 0;
                    foreach (var it in el.EnumerateArray())
                    {
                        var d = JsonMaxDepth(it);
                        if (d > max) max = d;
                    }
                    return 1 + max;
                }
            default:
                return 0;
        }
    }

    private static async Task<JsonElement> InvokeInspectParquetFileAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "inspect_data_store_parquet_file";
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return Error(action, "dataStoreId is required.");
        if (!TryReadGuid(args, "fileId", out var fileId))
            return Error(action, "fileId is required.");
        var sampleRows = ReadInt(args, "sampleRows") ?? 20;
        if (sampleRows < 0) sampleRows = 20;
        if (sampleRows > 200) sampleRows = 200;

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return Error(action, $"View permission denied on data store {dataStoreId}.");

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        DataStoreFile metadata;
        Stream content;
        try
        {
            (metadata, content) = await files.DownloadAsync(dataStoreId, fileId, ct);
        }
        catch (FileDataStoreFileNotFoundException)
        {
            return Error(action, $"File {fileId} not found in data store {dataStoreId}.");
        }

        if (metadata.SizeBytes > MaxParquetBytes)
        {
            await content.DisposeAsync();
            return Error(action,
                $"File is {metadata.SizeBytes:N0} bytes; Parquet inspection caps at {MaxParquetBytes:N0} bytes.");
        }

        await using var stream = content;
        ParquetReader reader;
        try
        {
            reader = await ParquetReader.CreateAsync(stream, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error(action, "Parquet file open failed: " + ex.Message);
        }

        using (reader)
        {
            try
            {
                var schema = reader.Schema;
                var dataFields = schema.GetDataFields();
                var rowGroupCount = reader.RowGroupCount;

                // Footer's RowGroup metadata carries the exact per-group row
                // count — no decompression needed for the total. Sum across
                // groups for the file total.
                long totalRows = 0;
                for (int i = 0; i < rowGroupCount; i++)
                {
                    using var rgMeta = reader.OpenRowGroupReader(i);
                    totalRows += rgMeta.RowCount;
                }

                var sel = ReadColumnSelection(args);
                var headerNames = dataFields.Select(f => f.Name).ToArray();
                var (includedSet, orderedIncluded, unknownColumns, columnsTruncated) =
                    ResolveIncludedColumns(headerNames, sel);
                if (sel.RequestedColumns is not null && includedSet.Count == 0)
                    return Error(action,
                        $"None of the requested columns matched the Parquet schema. Unknown: {string.Join(", ", unknownColumns)}.");

                var columns = dataFields.Select((f, i) => new
                {
                    name = f.Name,
                    path = string.Join(".", f.Path?.ToList() ?? new List<string> { f.Name }),
                    clrType = f.ClrType?.Name ?? "unknown",
                    isNullable = f.IsNullable,
                    isArray = f.IsArray,
                    inferredType = MapParquetType(f),
                    sampled = !sel.SchemaOnly && includedSet.Contains(i)
                }).ToList();

                // schemaOnly skips column decompression entirely.
                var sampleRowsOut = new List<Dictionary<string, object?>>();
                if (!sel.SchemaOnly && sampleRows > 0 && rowGroupCount > 0)
                {
                    using var rgReader = reader.OpenRowGroupReader(0);
                    // Only decompress the columns the caller asked for — huge
                    // win on wide files where most columns are unwanted.
                    var columnData = new List<(string Name, Array Data)>(orderedIncluded.Count);
                    foreach (var idx in orderedIncluded)
                    {
                        var df = dataFields[idx];
                        var col = await rgReader.ReadColumnAsync(df, ct);
                        columnData.Add((df.Name, col.Data));
                    }
                    var groupRowCount = columnData.Count == 0 ? 0 : columnData[0].Data.Length;
                    var rowsToTake = Math.Min(sampleRows, groupRowCount);
                    for (int r = 0; r < rowsToTake; r++)
                    {
                        var row = new Dictionary<string, object?>(columnData.Count, StringComparer.Ordinal);
                        foreach (var (name, data) in columnData)
                        {
                            var raw = r < data.Length ? StringifyParquetValue(data.GetValue(r)) : null;
                            // Truncate string cells; pass-through non-strings.
                            row[name] = raw is string s ? TruncateCell(s, sel.MaxCellLength) : raw;
                        }
                        sampleRowsOut.Add(row);
                    }
                }

                return JsonSerializer.SerializeToElement(new
                {
                    kind = "data_store_parquet_inspection",
                    source = "IFileDataStoreService + Parquet.Net",
                    data = new
                    {
                        fileId,
                        filename = metadata.Filename,
                        folderPath = metadata.FolderPath,
                        contentType = metadata.ContentType,
                        sizeBytes = metadata.SizeBytes,
                        mode = sel.SchemaOnly ? "schemaOnly" : "full",
                        totalRowCount = totalRows,
                        totalRowCountComplete = true,
                        rowGroupCount,
                        columnCount = columns.Count,
                        columns,
                        sampleRowCount = sampleRowsOut.Count,
                        sampleRows = sampleRowsOut,
                        sampleColumnsTruncated = columnsTruncated,
                        sampleColumnsIncluded = orderedIncluded.Select(i => headerNames[i]).ToList(),
                        unknownRequestedColumns = unknownColumns
                    }
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Error(action, "Parquet parse failed: " + ex.Message);
            }
        }
    }

    // CLR-type → Postgres-type vocabulary used by datasets and CSV ingest.
    // Array / nested fields collapse to "text" because the AQL surface
    // doesn't have a vector column type; the user would store them as
    // serialized JSON if they bring this Parquet into a dataset.
    private static string MapParquetType(DataField field)
    {
        if (field.IsArray) return "text";
        var t = field.ClrType;
        if (t == typeof(bool)) return "boolean";
        if (t == typeof(byte) || t == typeof(sbyte)
            || t == typeof(short) || t == typeof(ushort)
            || t == typeof(int) || t == typeof(uint)
            || t == typeof(long) || t == typeof(ulong))
            return "bigint";
        if (t == typeof(float) || t == typeof(double) || t == typeof(decimal))
            return "double precision";
        if (t == typeof(DateTime) || t == typeof(DateTimeOffset))
            return "timestamptz";
        if (t == typeof(TimeSpan)) return "text";
        if (t == typeof(string)) return "text";
        if (t == typeof(byte[])) return "text";
        if (t == typeof(Guid)) return "text";
        return "text";
    }

    // Convert Parquet cell values into something System.Text.Json can render.
    // Dates → ISO 8601; byte[] → base64 (so the agent gets a textual handle
    // for binary fields without us guessing an encoding); everything else
    // passes through (numbers/booleans/strings serialize natively).
    private static object? StringifyParquetValue(object? v)
    {
        if (v is null) return null;
        switch (v)
        {
            case string s: return s;
            case bool: return v;
            case DateTime dt: return dt.ToString("O", CultureInfo.InvariantCulture);
            case DateTimeOffset dto: return dto.ToString("O", CultureInfo.InvariantCulture);
            case TimeSpan ts: return ts.ToString("c", CultureInfo.InvariantCulture);
            case byte[] bytes: return Convert.ToBase64String(bytes);
            case Guid g: return g.ToString();
            default: return v;
        }
    }

    // Best-effort UTF-8 decode with binary fallback. Counts low-ASCII control
    // chars (excluding tab / LF / CR) as a binary heuristic — over ~5% non-text
    // bytes flips us into hex-preview mode so we don't shove garbage at the
    // model. Strips a UTF-8 BOM if present so the first preview char isn't
    // U+FEFF.
    private static (string text, bool isText, string? encodingNote) DecodeBytesAsText(byte[] buffer, int length)
    {
        if (length == 0) return (string.Empty, true, null);

        int offset = 0;
        string? note = null;
        if (length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
        {
            offset = 3;
            note = "utf-8-bom";
        }

        int controlCount = 0;
        for (int i = offset; i < length; i++)
        {
            byte b = buffer[i];
            if (b < 0x20 && b != 0x09 && b != 0x0A && b != 0x0D) controlCount++;
        }
        var bodyLength = length - offset;
        bool isText = bodyLength == 0 || controlCount * 100 < bodyLength * 5;
        if (!isText)
        {
            var hex = new StringBuilder("(binary; hex preview) ", capacity: 192);
            var previewLen = Math.Min(64, length);
            for (int i = 0; i < previewLen; i++) hex.Append(buffer[i].ToString("X2", CultureInfo.InvariantCulture));
            if (length > previewLen) hex.Append("...");
            return (hex.ToString(), false, "binary");
        }
        return (Encoding.UTF8.GetString(buffer, offset, bodyLength), true, note);
    }

    // Type inference shared with the CSV inspector. Mirrors the Postgres-type
    // vocabulary the rest of the data stack uses (CSV ingest, dataset column
    // schema) so the inferred types here drop straight into a dataset
    // definition or a CSV-ingest column list.
    private static string InferColumnType(IReadOnlyList<string?> samples)
    {
        var nonEmpty = new List<string>(samples.Count);
        foreach (var s in samples)
            if (!string.IsNullOrWhiteSpace(s)) nonEmpty.Add(s!.Trim());
        if (nonEmpty.Count == 0) return "text";

        bool allBool = true;
        foreach (var s in nonEmpty)
        {
            if (bool.TryParse(s, out _)) continue;
            // Tolerate the common "1"/"0"/"yes"/"no" cases admins type in
            // operational CSVs but stay strict on text outside that set so we
            // don't infer boolean for a "Y" customer-grade column.
            var l = s.ToLowerInvariant();
            if (l is "0" or "1" or "yes" or "no" or "y" or "n" or "true" or "false") continue;
            allBool = false; break;
        }
        // Disqualify all-0/1 columns from boolean — they're almost always int
        // ids/flags the user wants as bigint. Only flip to boolean if at least
        // one value is a non-numeric truthy/falsy token.
        if (allBool)
        {
            bool sawTruthyToken = false;
            foreach (var s in nonEmpty)
            {
                if (s is "0" or "1") continue;
                sawTruthyToken = true; break;
            }
            if (sawTruthyToken) return "boolean";
        }

        bool allLong = true;
        foreach (var s in nonEmpty)
        {
            if (!long.TryParse(s, NumberStyles.Integer | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out _)) { allLong = false; break; }
        }
        if (allLong) return "bigint";

        bool allDouble = true;
        foreach (var s in nonEmpty)
        {
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            { allDouble = false; break; }
        }
        if (allDouble) return "double precision";

        bool allDate = true;
        foreach (var s in nonEmpty)
        {
            if (!DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _))
            { allDate = false; break; }
        }
        if (allDate) return "timestamptz";

        return "text";
    }

    private static bool TryReadGuid(JsonElement args, string name, out Guid id)
    {
        id = Guid.Empty;
        return args.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String
            && Guid.TryParse(v.GetString(), out id);
    }

    // Compiled regex patterns for header classification. Used to categorize
    // every column header into a small fixed taxonomy so the model can tell
    // "1890 columns look like city names + 1 column is a date" without ever
    // seeing the full list.
    private static readonly Regex HeaderDateIsoRegex = new(@"^\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2})?", RegexOptions.Compiled);
    private static readonly Regex HeaderDateSlashRegex = new(@"^\d{1,2}/\d{1,2}/\d{2,4}$", RegexOptions.Compiled);
    private static readonly Regex HeaderIntRegex = new(@"^-?\d+$", RegexOptions.Compiled);
    private static readonly Regex HeaderFloatRegex = new(@"^-?\d+\.\d+$", RegexOptions.Compiled);
    // Proper-noun-ish: starts with a letter, allows letters / spaces / common
    // name punctuation including parentheses (for country qualifiers like
    // "Congo (Brazzaville)") and backticks (transliterations like "Madinat
    // `Isa"); 2–80 chars. Matches "Paris", "New York", "St. Petersburg",
    // "São Paulo", "Bahrain-Madinat `Isa", "Congo (Brazzaville)-Brazzaville".
    private static readonly Regex HeaderAlphaNameRegex = new(@"^\p{L}[\p{L}\s\-'’`,\.\(\)/]{1,79}$", RegexOptions.Compiled);
    // Mixed letters+digits = looks like an ID code (V12, RX-7).
    private static readonly Regex HeaderAlphaNumIdRegex = new(@"^[A-Za-z][A-Za-z0-9_\-]*\d+[A-Za-z0-9_\-]*$", RegexOptions.Compiled);
    // snake/camel identifier — typical "long-format" column name like
    // record_id, customerName, value_2024.
    private static readonly Regex HeaderIdentifierRegex = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private static string ClassifyHeader(string header)
    {
        var h = (header ?? string.Empty).Trim();
        if (h.Length == 0) return "empty";
        if (HeaderDateIsoRegex.IsMatch(h)) return "date_iso";
        if (HeaderDateSlashRegex.IsMatch(h)) return "date_slash";
        if (HeaderIntRegex.IsMatch(h)) return "integer";
        if (HeaderFloatRegex.IsMatch(h)) return "float";
        if (HeaderAlphaNameRegex.IsMatch(h)) return "alpha_name";
        if (HeaderAlphaNumIdRegex.IsMatch(h)) return "alphanumeric_id";
        if (HeaderIdentifierRegex.IsMatch(h)) return "identifier";
        return "other";
    }

    // Canonical missing-value sentinels common to real-world data exports.
    // Treating these as nulls (rather than letting them flip AllNumeric/AllDate
    // to false) keeps a single "NA" row in a 1900-column weather export from
    // making every city column resolve to "text" instead of "double precision".
    // Add to this set if a new sentinel turns up in practice.
    private static readonly HashSet<string> MissingValueSentinels = new(StringComparer.OrdinalIgnoreCase)
    {
        "NA", "N/A", "n/a", "NaN", "null", "NULL", "(null)", "None", "missing", "?", "-", "--", "."
    };

    // Per-column running statistics, fed one value at a time as we stream
    // the file. Tracks numeric / date / boolean / text simultaneously so we
    // don't need to know the column type up front — at the end we pick the
    // narrowest type that accepted every non-null value. Missing-value
    // sentinels (NA, NaN, null, …) are counted in `SentinelCount` so the
    // model can report "X rows missing data" without those tokens
    // contaminating type inference.
    private sealed class ColumnStatsAccumulator
    {
        private const int DistinctCap = 200;
        private const int SampleCap = 5;

        public string Name { get; }
        public int Index { get; }

        public long NullCount { get; private set; }
        public long SentinelCount { get; private set; }
        public long NonNullCount { get; private set; }

        public double? MinNum { get; private set; }
        public double? MaxNum { get; private set; }
        public double SumNum { get; private set; }
        public bool AllNumeric { get; private set; } = true;
        public bool AllInteger { get; private set; } = true;

        public DateTime? MinDate { get; private set; }
        public DateTime? MaxDate { get; private set; }
        public bool AllDate { get; private set; } = true;

        public long TrueCount { get; private set; }
        public long FalseCount { get; private set; }
        public bool AllBoolean { get; private set; } = true;

        public HashSet<string> DistinctValues { get; } = new(StringComparer.Ordinal);
        public bool DistinctCapHit { get; private set; }
        public List<string> SampleValues { get; } = new(SampleCap);

        public ColumnStatsAccumulator(string name, int index)
        {
            Name = name;
            Index = index;
        }

        public void Observe(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) { NullCount++; return; }
            var trimmed = raw.Trim();
            if (trimmed.Length == 0) { NullCount++; return; }
            // Recognize common missing-value sentinels and count them as
            // nulls so the type detector keeps trusting the surrounding
            // numeric / date values. The sentinel is tracked separately
            // (SentinelCount) so the model can still tell the user "X rows
            // had missing-data markers".
            if (MissingValueSentinels.Contains(trimmed))
            {
                NullCount++;
                SentinelCount++;
                return;
            }
            NonNullCount++;
            if (SampleValues.Count < SampleCap) SampleValues.Add(raw);

            if (AllNumeric)
            {
                if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                {
                    if (MinNum is null || n < MinNum) MinNum = n;
                    if (MaxNum is null || n > MaxNum) MaxNum = n;
                    SumNum += n;
                    // Mark non-integer once we see a fractional part. Use a
                    // tolerance because parsed-and-then-printed doubles round-
                    // trip with tiny float error; anything bigger than that
                    // tolerance is a genuine fractional value.
                    if (AllInteger && Math.Abs(n - Math.Round(n)) > 1e-12) AllInteger = false;
                }
                else { AllNumeric = false; AllInteger = false; }
            }

            if (AllDate)
            {
                if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                {
                    if (MinDate is null || dt < MinDate) MinDate = dt;
                    if (MaxDate is null || dt > MaxDate) MaxDate = dt;
                }
                else AllDate = false;
            }

            if (AllBoolean)
            {
                var l = trimmed.ToLowerInvariant();
                if (l is "true" or "1" or "yes" or "y") TrueCount++;
                else if (l is "false" or "0" or "no" or "n") FalseCount++;
                else AllBoolean = false;
            }

            if (!DistinctCapHit)
            {
                DistinctValues.Add(trimmed);
                if (DistinctValues.Count >= DistinctCap) DistinctCapHit = true;
            }
        }

        public string ResolveType()
        {
            if (NonNullCount == 0) return "text";
            // Same precedence as the existing CSV inspector: boolean only when
            // we saw a non-numeric truthy token; numeric splits int vs double.
            if (AllBoolean && TrueCount + FalseCount > 0)
            {
                bool sawTruthyToken = false;
                foreach (var s in SampleValues)
                {
                    var l = s.Trim().ToLowerInvariant();
                    if (l is "0" or "1") continue;
                    sawTruthyToken = true;
                    break;
                }
                if (sawTruthyToken) return "boolean";
            }
            if (AllNumeric) return AllInteger ? "bigint" : "double precision";
            if (AllDate) return "timestamptz";
            return "text";
        }

        public object BuildStats(int maxCellLength)
        {
            var type = ResolveType();
            object? minValue = null;
            object? maxValue = null;
            double? mean = null;
            switch (type)
            {
                case "bigint":
                case "double precision":
                    minValue = MinNum;
                    maxValue = MaxNum;
                    if (NonNullCount > 0) mean = SumNum / NonNullCount;
                    break;
                case "timestamptz":
                    minValue = MinDate?.ToString("O", CultureInfo.InvariantCulture);
                    maxValue = MaxDate?.ToString("O", CultureInfo.InvariantCulture);
                    break;
                case "boolean":
                    minValue = null;
                    maxValue = null;
                    break;
                default:
                    if (DistinctValues.Count > 0)
                    {
                        var ordered = DistinctValues.OrderBy(s => s, StringComparer.Ordinal).ToList();
                        minValue = TruncateCell(ordered[0], maxCellLength);
                        maxValue = TruncateCell(ordered[^1], maxCellLength);
                    }
                    break;
            }
            var samples = SampleValues.Select(s => TruncateCell(s, maxCellLength)).ToList();
            return new
            {
                inferredType = type,
                nullCount = NullCount,
                sentinelNullCount = SentinelCount,
                nonNullCount = NonNullCount,
                minValue,
                maxValue,
                mean,
                trueCount = type == "boolean" ? TrueCount : (long?)null,
                falseCount = type == "boolean" ? FalseCount : (long?)null,
                distinctCount = DistinctValues.Count,
                distinctCountIsCap = DistinctCapHit,
                sampleValues = samples
            };
        }
    }

    // Auto-pick a representative subset of columns to deep-profile when the
    // caller didn't specify one. Strategy: include EVERY "odd column out"
    // (any header whose pattern differs from the dominant one — those are
    // usually the key columns like 'date' or 'id'), then fill the rest of
    // the budget with first / middle / last samples of the dominant group
    // so the agent sees representative value ranges from both ends and the
    // middle of a 1000+-column file.
    private static List<int> AutoPickProfiledColumns(
        IReadOnlyList<string> headers,
        Dictionary<string, int> patternCounts,
        int budget)
    {
        if (headers.Count == 0) return new List<int>();
        budget = Math.Min(budget, headers.Count);
        var dominant = patternCounts.OrderByDescending(kv => kv.Value).First().Key;
        var picked = new List<int>(budget);
        var seen = new HashSet<int>();
        // Pass 1: all "odd column out" columns — caps to avoid overflow when
        // the file is small and most columns are unique-pattern.
        for (int i = 0; i < headers.Count && picked.Count < budget; i++)
        {
            if (ClassifyHeader(headers[i]) != dominant)
            {
                if (seen.Add(i)) picked.Add(i);
            }
        }
        // Pass 2: spread dominant-pattern columns across the index range so
        // the sample covers both ends of a wide pivot.
        if (picked.Count < budget)
        {
            var remaining = budget - picked.Count;
            var dominantIndices = new List<int>(headers.Count);
            for (int i = 0; i < headers.Count; i++)
                if (ClassifyHeader(headers[i]) == dominant && !seen.Contains(i))
                    dominantIndices.Add(i);
            if (dominantIndices.Count <= remaining)
            {
                foreach (var i in dominantIndices) { picked.Add(i); seen.Add(i); }
            }
            else
            {
                // Evenly spaced sample across the dominant list.
                var step = (double)dominantIndices.Count / remaining;
                for (int k = 0; k < remaining; k++)
                {
                    var idx = dominantIndices[(int)Math.Floor(k * step)];
                    if (seen.Add(idx)) picked.Add(idx);
                }
            }
        }
        picked.Sort();
        return picked;
    }

    // Classify the file's layout based on header pattern uniformity. A
    // wide-pivot is the case the user's 1891-column Weather file falls into:
    // most columns share the same pattern (city names), with one or two
    // "different" columns that act as the row key (the date column). A long
    // / narrow table has heterogeneous identifier-style headers.
    private static (string shape, string shapeDescription, List<string> recommendations, string? dominantPattern,
        int dominantCount, List<string> keyColumns)
        DetectLayout(IReadOnlyList<string> headers, Dictionary<string, int> patternCounts)
    {
        var total = headers.Count;
        if (total == 0)
        {
            return ("empty", "No headers parsed.", new List<string>(), null, 0, new List<string>());
        }

        var top = patternCounts.OrderByDescending(kv => kv.Value).First();
        var dominantPattern = top.Key;
        var dominantCount = top.Value;

        // Identify the "odd column out" headers — the columns whose pattern
        // differs from the dominant. In a wide pivot these are the keys.
        var keyColumns = new List<string>();
        for (int i = 0; i < headers.Count; i++)
            if (ClassifyHeader(headers[i]) != dominantPattern) keyColumns.Add(headers[i]);

        // Wide-pivot heuristic: dominant pattern covers ≥80% of headers AND
        // there are at least 20 dominant columns AND the dominant pattern is
        // a "value-like" pattern (dates / names / numbers / IDs — anything
        // that wouldn't normally be a column NAME in a relational table).
        var dominantShare = (double)dominantCount / total;
        bool dominantIsValueLike = dominantPattern is "alpha_name" or "date_iso" or "date_slash"
            or "integer" or "float" or "alphanumeric_id";

        if (dominantShare >= 0.80 && dominantCount >= 20 && dominantIsValueLike)
        {
            var entityKind = dominantPattern switch
            {
                "alpha_name" => "entities (looks like proper names)",
                "date_iso" or "date_slash" => "dates",
                "integer" or "float" => "numeric labels",
                "alphanumeric_id" => "identifiers",
                _ => "items"
            };
            var keyDesc = keyColumns.Count == 0
                ? "no obvious row-key column"
                : keyColumns.Count == 1
                    ? $"one row-key column ('{keyColumns[0]}')"
                    : $"{keyColumns.Count} row-key columns ({string.Join(", ", keyColumns.Take(3).Select(k => $"'{k}'"))}{(keyColumns.Count > 3 ? ", …" : "")})";
            var description =
                $"Wide-pivot table. {dominantCount} of {total} columns share the same header pattern ({dominantPattern}), " +
                $"so the columns themselves carry {entityKind}. The {keyDesc} most likely identifies each row.";
            var recommendations = new List<string>
            {
                $"This is a wide / pivoted layout. Per-entity analysis (e.g. \"trend for column X\") is awkward in AQL today. " +
                    $"Unpivot to a long format with one row per (row-key × {dominantPattern.Replace("_", " ")} value) — three columns: the row key, " +
                    $"the entity (column name), and the value. Long format makes filters, joins, and group-by trivial.",
                "If the user just wants to query the wide form, ingest as a SqlType data store and define a Dataset over the table — " +
                    "AQL will see one column per entity. For long-form analysis, do the unpivot first (Python / dbt / a CodeTransformer)."
            };
            return ("wide-pivot", description, recommendations, dominantPattern, dominantCount, keyColumns);
        }

        var longDescription =
            $"Long / narrow table with {total} column{(total == 1 ? "" : "s")}. " +
            $"Headers look like normal column names (dominant pattern: {dominantPattern}). Each row is one observation.";
        var longRecs = new List<string>
        {
            "Ingest into a SqlType data store and define a Dataset for AQL queries: `FROM Dataset(\"<name>\")`."
        };
        return ("long-table", longDescription, longRecs, dominantPattern, dominantCount, keyColumns);
    }

    private static async Task<JsonElement> InvokeProfileCsvFileAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "profile_data_store_csv_file";
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return Error(action, "dataStoreId is required.");
        if (!TryReadGuid(args, "fileId", out var fileId))
            return Error(action, "fileId is required.");
        var delimiter = ReadString(args, "delimiter") ?? ",";
        if (delimiter.Length == 0 || delimiter.Length > 4)
            return Error(action, "delimiter must be 1–4 characters.");
        var maxProfiledColumns = ReadInt(args, "maxProfiledColumns") ?? 10;
        if (maxProfiledColumns <= 0) maxProfiledColumns = 10;
        if (maxProfiledColumns > 50) maxProfiledColumns = 50;
        var maxScannedRows = ReadInt(args, "maxScannedRows") ?? (int)Math.Min(int.MaxValue, MaxCountedRows);
        if (maxScannedRows < 1000) maxScannedRows = 1000;
        var profileColumnsArg = ReadStringArray(args, "profileColumns");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return Error(action, $"View permission denied on data store {dataStoreId}.");

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        DataStoreFile metadata;
        Stream content;
        try
        {
            (metadata, content) = await files.DownloadAsync(dataStoreId, fileId, ct);
        }
        catch (FileDataStoreFileNotFoundException)
        {
            return Error(action, $"File {fileId} not found in data store {dataStoreId}.");
        }

        await using var stream = content;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null,
            Delimiter = delimiter
        };
        using var csv = new CsvReader(reader, csvConfig);

        try
        {
            if (!await csv.ReadAsync()) return Error(action, "File is empty.");
            csv.ReadHeader();
        }
        catch (CsvHelperException ex)
        {
            return Error(action, "Header parse failed: " + ex.Message);
        }
        var headers = csv.HeaderRecord ?? Array.Empty<string>();
        if (headers.Length == 0)
            return Error(action, "No columns parsed from header row.");

        // Classify headers.
        var patternCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var perHeaderPattern = new string[headers.Length];
        var headerLengths = new int[headers.Length];
        for (int i = 0; i < headers.Length; i++)
        {
            var p = ClassifyHeader(headers[i]);
            perHeaderPattern[i] = p;
            patternCounts[p] = patternCounts.GetValueOrDefault(p) + 1;
            headerLengths[i] = (headers[i] ?? string.Empty).Length;
        }

        // Decide profiled-column indices.
        List<int> profiledIndices;
        var unknownColumns = new List<string>();
        if (profileColumnsArg is not null && profileColumnsArg.Count > 0)
        {
            var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length; i++)
                lookup.TryAdd(headers[i], i);
            profiledIndices = new List<int>();
            foreach (var name in profileColumnsArg)
            {
                if (lookup.TryGetValue(name, out var idx))
                {
                    if (!profiledIndices.Contains(idx)) profiledIndices.Add(idx);
                }
                else unknownColumns.Add(name);
            }
            if (profiledIndices.Count == 0)
                return Error(action,
                    $"None of the requested profile columns matched. Unknown: {string.Join(", ", unknownColumns)}.");
        }
        else
        {
            profiledIndices = AutoPickProfiledColumns(headers, patternCounts, maxProfiledColumns);
        }

        var accumulators = new Dictionary<int, ColumnStatsAccumulator>(profiledIndices.Count);
        foreach (var i in profiledIndices)
            accumulators[i] = new ColumnStatsAccumulator(headers[i], i);

        long totalRows = 0;
        bool scanComplete = true;
        string? streamingParseError = null;
        try
        {
            while (await csv.ReadAsync())
            {
                foreach (var i in profiledIndices)
                {
                    string? v = null;
                    try { v = csv.GetField(i); }
                    catch (CsvHelperException) { v = null; }
                    accumulators[i].Observe(v);
                }
                totalRows++;
                if (totalRows >= maxScannedRows) { scanComplete = false; break; }
            }
        }
        catch (CsvHelperException ex)
        {
            streamingParseError = ex.Message;
        }

        // Pattern stats — keep the 5 most populated buckets, plus include the
        // first/middle/last header from each so the model has examples.
        var orderedPatterns = patternCounts.OrderByDescending(kv => kv.Value).ToList();
        var patternStats = orderedPatterns.Select(kv =>
        {
            var examples = new List<string>();
            for (int i = 0; i < headers.Length && examples.Count < 3; i++)
                if (perHeaderPattern[i] == kv.Key) examples.Add(headers[i]);
            if (headers.Length > 6)
            {
                for (int i = headers.Length - 1; i >= 0 && examples.Count < 5; i--)
                    if (perHeaderPattern[i] == kv.Key && !examples.Contains(headers[i])) examples.Add(headers[i]);
            }
            return new
            {
                pattern = kv.Key,
                count = kv.Value,
                share = (double)kv.Value / headers.Length,
                examples
            };
        }).ToList();

        var (shape, shapeDescription, recommendations, dominantPattern, dominantCount, keyColumns) =
            DetectLayout(headers, patternCounts);

        var columnProfiles = profiledIndices.Select(i => new
        {
            name = headers[i],
            index = i,
            headerPattern = perHeaderPattern[i],
            stats = accumulators[i].BuildStats(200)
        }).ToList();

        var firstHeaders = headers.Take(Math.Min(8, headers.Length)).ToList();
        var lastHeaders = headers.Skip(Math.Max(0, headers.Length - 8)).ToList();

        return JsonSerializer.SerializeToElement(new
        {
            kind = "data_store_csv_profile",
            source = "IFileDataStoreService + CsvHelper",
            data = new
            {
                fileId,
                filename = metadata.Filename,
                folderPath = metadata.FolderPath,
                contentType = metadata.ContentType,
                sizeBytes = metadata.SizeBytes,
                delimiter,
                totalColumnCount = headers.Length,
                totalRowCount = totalRows,
                totalRowCountComplete = scanComplete,
                streamingParseError,
                headerAnalysis = new
                {
                    firstHeaders,
                    lastHeaders,
                    dominantPattern,
                    dominantCount,
                    dominantShare = headers.Length > 0 ? (double)dominantCount / headers.Length : 0,
                    patternStats,
                    longestHeader = headers.Length > 0 ? headerLengths.Max() : 0,
                    shortestHeader = headers.Length > 0 ? headerLengths.Min() : 0,
                    keyColumns
                },
                layout = new
                {
                    shape,
                    description = shapeDescription,
                    recommendations
                },
                profiledColumns = columnProfiles,
                profiledColumnCount = columnProfiles.Count,
                unknownRequestedProfileColumns = unknownColumns
            }
        });
    }

    private static async Task<JsonElement> InvokeLookupCsvRowsAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "lookup_data_store_csv_rows";
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return Error(action, "dataStoreId is required.");
        if (!TryReadGuid(args, "fileId", out var fileId))
            return Error(action, "fileId is required.");
        var delimiter = ReadString(args, "delimiter") ?? ",";
        if (delimiter.Length == 0 || delimiter.Length > 4)
            return Error(action, "delimiter must be 1–4 characters.");
        var filterColumn = ReadString(args, "filterColumn");
        var filterValue = ReadString(args, "filterValue");
        if (string.IsNullOrEmpty(filterColumn)) return Error(action, "filterColumn is required.");
        if (filterValue is null) return Error(action, "filterValue is required (use empty string to match empties).");
        var limit = ReadInt(args, "limit") ?? 20;
        if (limit <= 0) limit = 20;
        if (limit > 100) limit = 100;
        var maxScannedRows = ReadInt(args, "maxScannedRows") ?? (int)Math.Min(int.MaxValue, MaxCountedRows);
        if (maxScannedRows < 1000) maxScannedRows = 1000;
        var maxCellLength = ReadInt(args, "maxCellLength") ?? 200;
        if (maxCellLength < 0) maxCellLength = 0;
        if (maxCellLength > 2000) maxCellLength = 2000;
        var projectColumnsArg = ReadStringArray(args, "projectColumns");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return Error(action, $"View permission denied on data store {dataStoreId}.");

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        DataStoreFile metadata;
        Stream content;
        try
        {
            (metadata, content) = await files.DownloadAsync(dataStoreId, fileId, ct);
        }
        catch (FileDataStoreFileNotFoundException)
        {
            return Error(action, $"File {fileId} not found in data store {dataStoreId}.");
        }

        await using var stream = content;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null,
            Delimiter = delimiter
        };
        using var csv = new CsvReader(reader, csvConfig);

        try
        {
            if (!await csv.ReadAsync()) return Error(action, "File is empty.");
            csv.ReadHeader();
        }
        catch (CsvHelperException ex)
        {
            return Error(action, "Header parse failed: " + ex.Message);
        }
        var headers = csv.HeaderRecord ?? Array.Empty<string>();
        if (headers.Length == 0)
            return Error(action, "No columns parsed from header row.");

        var headerLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++)
            headerLookup.TryAdd(headers[i], i);

        if (!headerLookup.TryGetValue(filterColumn, out var filterIdx))
            return Error(action, $"filterColumn '{filterColumn}' not in the file's headers.");

        // Project columns: caller-provided list (case-insensitive) or
        // default to the filter column plus the first 20 other columns —
        // small enough not to blow the response for a wide file.
        List<int> projectIndices;
        var unknownProject = new List<string>();
        if (projectColumnsArg is not null && projectColumnsArg.Count > 0)
        {
            projectIndices = new List<int>();
            foreach (var name in projectColumnsArg)
            {
                if (headerLookup.TryGetValue(name, out var idx))
                {
                    if (!projectIndices.Contains(idx)) projectIndices.Add(idx);
                }
                else unknownProject.Add(name);
            }
            if (projectIndices.Count == 0)
                return Error(action,
                    $"None of the requested project columns matched. Unknown: {string.Join(", ", unknownProject)}.");
        }
        else
        {
            projectIndices = new List<int> { filterIdx };
            for (int i = 0; i < headers.Length && projectIndices.Count < 21; i++)
                if (i != filterIdx) projectIndices.Add(i);
        }

        var matches = new List<Dictionary<string, string?>>(limit);
        long scanned = 0;
        bool scanComplete = true;
        string? streamingParseError = null;
        try
        {
            while (await csv.ReadAsync())
            {
                string? fieldVal = null;
                try { fieldVal = csv.GetField(filterIdx); }
                catch (CsvHelperException) { /* treat malformed as no match */ }
                if (string.Equals(fieldVal?.Trim(), filterValue.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    var row = new Dictionary<string, string?>(projectIndices.Count, StringComparer.Ordinal);
                    foreach (var i in projectIndices)
                    {
                        string? v = null;
                        try { v = csv.GetField(i); }
                        catch (CsvHelperException) { v = null; }
                        row[headers[i]] = TruncateCell(v, maxCellLength);
                    }
                    matches.Add(row);
                    if (matches.Count >= limit) break;
                }
                scanned++;
                if (scanned >= maxScannedRows) { scanComplete = false; break; }
            }
        }
        catch (CsvHelperException ex)
        {
            streamingParseError = ex.Message;
        }

        return JsonSerializer.SerializeToElement(new
        {
            kind = "data_store_csv_lookup",
            source = "IFileDataStoreService + CsvHelper",
            data = new
            {
                fileId,
                filename = metadata.Filename,
                folderPath = metadata.FolderPath,
                sizeBytes = metadata.SizeBytes,
                filter = new { column = filterColumn, equals = filterValue },
                projectedColumns = projectIndices.Select(i => headers[i]).ToList(),
                unknownRequestedProjectColumns = unknownProject,
                rowsScanned = scanned,
                scanComplete,
                streamingParseError,
                matchCount = matches.Count,
                limitHit = matches.Count >= limit,
                rows = matches
            }
        });
    }

    private static async Task<JsonElement> InvokeProfileXlsxSheetAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "profile_data_store_xlsx_sheet";
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return Error(action, "dataStoreId is required.");
        if (!TryReadGuid(args, "fileId", out var fileId))
            return Error(action, "fileId is required.");
        var sheetName = ReadString(args, "sheetName");
        var sheetIndex = ReadInt(args, "sheetIndex");
        var headerRow = ReadInt(args, "headerRow") ?? 1;
        if (headerRow <= 0) headerRow = 1;
        var descriptionRow = ReadInt(args, "descriptionRow");
        if (descriptionRow is <= 0) descriptionRow = null;
        var maxProfiledColumns = ReadInt(args, "maxProfiledColumns") ?? 10;
        if (maxProfiledColumns <= 0) maxProfiledColumns = 10;
        if (maxProfiledColumns > 50) maxProfiledColumns = 50;
        var maxScannedRows = ReadInt(args, "maxScannedRows") ?? (int)Math.Min(int.MaxValue, MaxCountedRows);
        if (maxScannedRows < 1000) maxScannedRows = 1000;
        var profileColumnsArg = ReadStringArray(args, "profileColumns");
        if (!string.IsNullOrWhiteSpace(sheetName) && sheetIndex.HasValue)
            return Error(action, "Specify sheetName or sheetIndex, not both.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return Error(action, $"View permission denied on data store {dataStoreId}.");

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        DataStoreFile metadata;
        Stream content;
        try
        {
            (metadata, content) = await files.DownloadAsync(dataStoreId, fileId, ct);
        }
        catch (FileDataStoreFileNotFoundException)
        {
            return Error(action, $"File {fileId} not found in data store {dataStoreId}.");
        }

        if (metadata.SizeBytes > MaxXlsxBytes)
        {
            await content.DisposeAsync();
            return Error(action,
                $"File is {metadata.SizeBytes:N0} bytes; XLSX inspection caps at {MaxXlsxBytes:N0} bytes.");
        }

        await using var stream = content;
        XLWorkbook wb;
        try { wb = new XLWorkbook(stream); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error(action, "Workbook open failed: " + ex.Message);
        }

        using (wb)
        {
            IXLWorksheet? sheet;
            if (!string.IsNullOrWhiteSpace(sheetName))
            {
                sheet = wb.Worksheets.FirstOrDefault(w => string.Equals(w.Name, sheetName, StringComparison.OrdinalIgnoreCase));
                if (sheet is null)
                {
                    var available = string.Join(", ", wb.Worksheets.Select(w => $"'{w.Name}'"));
                    return Error(action, $"Sheet '{sheetName}' not found. Available: {available}.");
                }
            }
            else if (sheetIndex.HasValue)
            {
                var idx = sheetIndex.Value;
                if (idx < 1 || idx > wb.Worksheets.Count)
                    return Error(action, $"sheetIndex {idx} out of range; workbook has {wb.Worksheets.Count} sheet(s).");
                sheet = wb.Worksheets.Worksheet(idx);
            }
            else
            {
                sheet = wb.Worksheets.FirstOrDefault();
                if (sheet is null) return Error(action, "Workbook has no sheets.");
            }

            try
            {
                var range = sheet.RangeUsed();
                if (range is null)
                    return Error(action, $"Sheet '{sheet.Name}' has no used range — nothing to profile.");

                var firstRow = range.FirstRow().RowNumber();
                var lastRow = range.LastRow().RowNumber();
                var lastCol = range.LastColumn().ColumnNumber();
                if (headerRow < firstRow || headerRow > lastRow)
                    return Error(action, $"headerRow {headerRow} is outside the used range (rows {firstRow}–{lastRow}).");
                if (descriptionRow is { } dr && (dr < firstRow || dr > lastRow))
                    return Error(action, $"descriptionRow {dr} is outside the used range (rows {firstRow}–{lastRow}).");
                if (descriptionRow == headerRow)
                    return Error(action, "descriptionRow must differ from headerRow.");

                // Read headers + optional description-above row.
                var headers = new List<string>(lastCol);
                var headerOriginals = new List<string>(lastCol);
                string[]? descriptionAbove = descriptionRow.HasValue ? new string[lastCol] : null;
                for (int c = 1; c <= lastCol; c++)
                {
                    var raw = sheet.Cell(headerRow, c).GetString();
                    headerOriginals.Add(raw);
                    headers.Add(string.IsNullOrWhiteSpace(raw) ? $"col_{c}" : raw.Trim());
                    if (descriptionAbove is not null)
                        descriptionAbove[c - 1] = sheet.Cell(descriptionRow!.Value, c).GetString();
                }

                // Classify headers.
                var patternCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                var perHeaderPattern = new string[headers.Count];
                var headerLengths = new int[headers.Count];
                for (int i = 0; i < headers.Count; i++)
                {
                    var p = ClassifyHeader(headers[i]);
                    perHeaderPattern[i] = p;
                    patternCounts[p] = patternCounts.GetValueOrDefault(p) + 1;
                    headerLengths[i] = (headers[i] ?? string.Empty).Length;
                }

                // Pick profiled columns.
                List<int> profiledIndices;
                var unknownColumns = new List<string>();
                if (profileColumnsArg is not null && profileColumnsArg.Count > 0)
                {
                    var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < headers.Count; i++) lookup.TryAdd(headers[i], i);
                    profiledIndices = new List<int>();
                    foreach (var name in profileColumnsArg)
                    {
                        if (lookup.TryGetValue(name, out var idx))
                        {
                            if (!profiledIndices.Contains(idx)) profiledIndices.Add(idx);
                        }
                        else unknownColumns.Add(name);
                    }
                    if (profiledIndices.Count == 0)
                        return Error(action,
                            $"None of the requested profile columns matched. Unknown: {string.Join(", ", unknownColumns)}.");
                }
                else
                {
                    profiledIndices = AutoPickProfiledColumns(headers, patternCounts, maxProfiledColumns);
                }

                var accumulators = new Dictionary<int, ColumnStatsAccumulator>(profiledIndices.Count);
                foreach (var i in profiledIndices)
                    accumulators[i] = new ColumnStatsAccumulator(headers[i], i);

                // Stream rows. For 4000 rows × 10 profiled cols = 40k cell
                // reads — well within ClosedXML's perf budget. Going row by
                // row (not by column) so blank-row detection could be added
                // later if needed.
                var dataStart = headerRow + 1;
                long totalRows = 0;
                bool scanComplete = true;
                for (int rr = dataStart; rr <= lastRow; rr++)
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var i in profiledIndices)
                    {
                        var inf = XlsxCellToInferenceString(sheet.Cell(rr, i + 1));
                        accumulators[i].Observe(inf);
                    }
                    totalRows++;
                    if (totalRows >= maxScannedRows) { scanComplete = false; break; }
                }

                var orderedPatterns = patternCounts.OrderByDescending(kv => kv.Value).ToList();
                var patternStats = orderedPatterns.Select(kv =>
                {
                    var examples = new List<string>();
                    for (int i = 0; i < headers.Count && examples.Count < 3; i++)
                        if (perHeaderPattern[i] == kv.Key) examples.Add(headers[i]);
                    if (headers.Count > 6)
                    {
                        for (int i = headers.Count - 1; i >= 0 && examples.Count < 5; i--)
                            if (perHeaderPattern[i] == kv.Key && !examples.Contains(headers[i])) examples.Add(headers[i]);
                    }
                    return new
                    {
                        pattern = kv.Key,
                        count = kv.Value,
                        share = (double)kv.Value / headers.Count,
                        examples
                    };
                }).ToList();

                var (shape, shapeDescription, recommendations, dominantPattern, dominantCount, keyColumns) =
                    DetectLayout(headers, patternCounts);

                // Surface value-type-disagreeing columns too — for the Weather
                // file the date columns share the alpha_name header pattern
                // with the city columns, so header-pattern alone misses them
                // as keys. The agent can use both signals.
                var profiledTypes = profiledIndices.ToDictionary(i => i, i => accumulators[i].ResolveType());
                var dominantValueType = profiledTypes.Count == 0
                    ? null
                    : profiledTypes.Values
                        .GroupBy(v => v)
                        .OrderByDescending(g => g.Count())
                        .First().Key;
                var valueTypeKeyColumns = profiledTypes
                    .Where(kv => dominantValueType is not null && kv.Value != dominantValueType)
                    .Select(kv => new { name = headers[kv.Key], index = kv.Key, valueType = kv.Value })
                    .ToList();

                var columnProfiles = profiledIndices.Select(i => new
                {
                    name = headers[i],
                    index = i,
                    columnLetter = ColumnLetter(i + 1),
                    headerPattern = perHeaderPattern[i],
                    descriptionAbove = descriptionAbove?[i],
                    stats = accumulators[i].BuildStats(200)
                }).ToList();

                var firstHeaders = headers.Take(Math.Min(8, headers.Count)).ToList();
                var lastHeaders = headers.Skip(Math.Max(0, headers.Count - 8)).ToList();

                return JsonSerializer.SerializeToElement(new
                {
                    kind = "data_store_xlsx_profile",
                    source = "IFileDataStoreService + ClosedXML",
                    data = new
                    {
                        fileId,
                        filename = metadata.Filename,
                        folderPath = metadata.FolderPath,
                        contentType = metadata.ContentType,
                        sizeBytes = metadata.SizeBytes,
                        sheetName = sheet.Name,
                        sheetPosition = sheet.Position,
                        isHidden = sheet.Visibility != XLWorksheetVisibility.Visible,
                        firstUsedRow = firstRow,
                        lastUsedRow = lastRow,
                        totalColumnCount = lastCol,
                        totalRowCount = totalRows,
                        totalRowCountComplete = scanComplete,
                        headerRow,
                        descriptionRow,
                        headerAnalysis = new
                        {
                            firstHeaders,
                            lastHeaders,
                            dominantPattern,
                            dominantCount,
                            dominantShare = headers.Count > 0 ? (double)dominantCount / headers.Count : 0,
                            patternStats,
                            longestHeader = headers.Count > 0 ? headerLengths.Max() : 0,
                            shortestHeader = headers.Count > 0 ? headerLengths.Min() : 0,
                            keyColumns,
                            valueTypeKeyColumns,
                            dominantProfiledType = dominantValueType
                        },
                        layout = new
                        {
                            shape,
                            description = shapeDescription,
                            recommendations
                        },
                        profiledColumns = columnProfiles,
                        profiledColumnCount = columnProfiles.Count,
                        unknownRequestedProfileColumns = unknownColumns
                    }
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Error(action, "Sheet profile failed: " + ex.Message);
            }
        }
    }

    private static async Task<JsonElement> InvokeProfileJsonFileAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "profile_data_store_json_file";
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return Error(action, "dataStoreId is required.");
        if (!TryReadGuid(args, "fileId", out var fileId))
            return Error(action, "fileId is required.");
        var modeArg = (ReadString(args, "mode") ?? "auto").ToLowerInvariant();
        if (modeArg is not ("auto" or "ndjson" or "array" or "single"))
            return Error(action, "mode must be 'auto', 'ndjson', 'array', or 'single'.");
        var maxProfiledColumns = ReadInt(args, "maxProfiledColumns") ?? 10;
        if (maxProfiledColumns <= 0) maxProfiledColumns = 10;
        if (maxProfiledColumns > 50) maxProfiledColumns = 50;
        var maxScannedRows = ReadInt(args, "maxScannedRows") ?? (int)Math.Min(int.MaxValue, MaxCountedRows);
        if (maxScannedRows < 1000) maxScannedRows = 1000;
        var profileKeysArg = ReadStringArray(args, "profileKeys");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return Error(action, $"View permission denied on data store {dataStoreId}.");

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        DataStoreFile metadata;
        Stream content;
        try
        {
            (metadata, content) = await files.DownloadAsync(dataStoreId, fileId, ct);
        }
        catch (FileDataStoreFileNotFoundException)
        {
            return Error(action, $"File {fileId} not found in data store {dataStoreId}.");
        }

        await using var stream = content;

        string mode = modeArg;
        string? detectionNote = null;
        if (mode == "auto")
        {
            if (!stream.CanSeek)
                return Error(action, "Cannot auto-detect JSON mode on non-seekable stream; specify `mode` explicitly.");
            mode = await DetectJsonModeAsync(stream, ct);
            stream.Seek(0, SeekOrigin.Begin);
            detectionNote = $"auto-detected as {mode}";
            if (mode == "unknown")
                return Error(action, "Could not classify the JSON shape from the first 64KB. Pass `mode` explicitly.");
        }

        // Single-object: same structural summary as the inspector; column-style
        // profiling doesn't apply to a non-tabular document.
        if (mode == "single")
            return await InspectJsonSingleAsync(stream, metadata, fileId, detectionNote, ct);

        // Array mode loads the whole doc through JsonDocument — cap enforced.
        if (mode == "array" && metadata.SizeBytes > MaxJsonNonStreamingBytes)
            return Error(action,
                $"File is {metadata.SizeBytes:N0} bytes; JSON array mode caps at {MaxJsonNonStreamingBytes:N0} bytes. Convert to NDJSON to stream the whole file.");

        var keysSeen = new HashSet<string>(StringComparer.Ordinal);
        var orderedKeys = new List<string>();
        var presenceCount = new Dictionary<string, long>(StringComparer.Ordinal);
        // We don't know up front which keys will exist, so we defer accumulator
        // creation until the auto-pick step. To keep the pass single-streamed
        // we collect per-record values into a tiny in-memory buffer (key →
        // running list, capped at a few values) AND a running "saw it" count
        // for every key, then after we know which keys to profile we walk
        // back through… wait, we can't rewind a stream. Better approach:
        // accumulators for the FIRST `maxProfiledColumns` keys we see + any
        // keys named in profileKeysArg. New keys discovered after the cap is
        // hit get tracked for presence/count only — that's enough for the
        // pattern summary, and the agent can re-call with `profileKeys` if
        // it wants to drill into a key that wasn't auto-profiled.
        var allowedLower = profileKeysArg is not null
            ? new HashSet<string>(profileKeysArg.Select(k => k.ToLowerInvariant()), StringComparer.Ordinal)
            : null;
        var accumulators = new Dictionary<string, ColumnStatsAccumulator>(StringComparer.Ordinal);

        long totalRecords = 0;
        bool scanComplete = true;
        bool sawNonObject = false;
        long parseErrors = 0;
        string? firstParseError = null;

        void ObserveRecord(JsonElement record)
        {
            if (record.ValueKind != JsonValueKind.Object) { sawNonObject = true; return; }
            var seenInRecord = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prop in record.EnumerateObject())
            {
                if (keysSeen.Add(prop.Name)) orderedKeys.Add(prop.Name);
                seenInRecord.Add(prop.Name);
                presenceCount[prop.Name] = presenceCount.GetValueOrDefault(prop.Name) + 1;
                bool include = allowedLower is not null
                    ? allowedLower.Contains(prop.Name.ToLowerInvariant())
                    : accumulators.ContainsKey(prop.Name) || accumulators.Count < maxProfiledColumns;
                if (!include) continue;
                if (!accumulators.TryGetValue(prop.Name, out var acc))
                {
                    acc = new ColumnStatsAccumulator(prop.Name, accumulators.Count);
                    accumulators[prop.Name] = acc;
                }
                acc.Observe(JsonElementToInferenceString(prop.Value));
            }
            // For accumulators tied to keys NOT present in this record, count
            // a null observation so per-key stats reflect record-relative
            // sparsity, not just per-value distribution.
            foreach (var (k, a) in accumulators)
                if (!seenInRecord.Contains(k)) a.Observe(null);
            totalRecords++;
        }

        if (mode == "ndjson")
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    ObserveRecord(doc.RootElement);
                }
                catch (JsonException ex)
                {
                    parseErrors++;
                    firstParseError ??= ex.Message;
                }
                if (totalRecords >= maxScannedRows) { scanComplete = false; break; }
            }
        }
        else // array
        {
            JsonDocument doc;
            try
            {
                doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            }
            catch (JsonException ex)
            {
                return Error(action, "JSON parse failed: " + ex.Message);
            }
            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return Error(action, $"Expected JSON array, got {doc.RootElement.ValueKind}. Pass `mode` explicitly.");
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    ObserveRecord(item);
                    if (totalRecords >= maxScannedRows) { scanComplete = false; break; }
                }
            }
        }

        // Pattern analysis runs on the union of keys seen — works the same
        // way as the CSV header pattern step.
        var patternCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var perKeyPattern = new Dictionary<string, string>(StringComparer.Ordinal);
        var keyLengths = new List<int>(orderedKeys.Count);
        foreach (var k in orderedKeys)
        {
            var p = ClassifyHeader(k);
            perKeyPattern[k] = p;
            patternCounts[p] = patternCounts.GetValueOrDefault(p) + 1;
            keyLengths.Add(k.Length);
        }

        var orderedPatterns = patternCounts.Count == 0
            ? new List<KeyValuePair<string, int>>()
            : patternCounts.OrderByDescending(kv => kv.Value).ToList();
        var patternStats = orderedPatterns.Select(kv =>
        {
            var examples = new List<string>();
            foreach (var k in orderedKeys)
            {
                if (perKeyPattern[k] == kv.Key)
                {
                    examples.Add(k);
                    if (examples.Count >= 5) break;
                }
            }
            return new
            {
                pattern = kv.Key,
                count = kv.Value,
                share = orderedKeys.Count > 0 ? (double)kv.Value / orderedKeys.Count : 0,
                examples
            };
        }).ToList();

        var (shape, shapeDescription, recommendations, dominantPattern, dominantCount, keyColumns) =
            DetectLayout(orderedKeys, patternCounts);

        var profiledKeysList = accumulators.Keys.ToList();
        var profiledColumns = profiledKeysList.Select(k => new
        {
            name = k,
            keyPattern = perKeyPattern.GetValueOrDefault(k, "other"),
            presenceCount = presenceCount.GetValueOrDefault(k),
            presenceShare = totalRecords > 0 ? (double)presenceCount.GetValueOrDefault(k) / totalRecords : 0,
            stats = accumulators[k].BuildStats(200)
        }).ToList();

        // Surface keys that appeared in fewer than 50% of records — those are
        // optional fields that signal a sparse schema vs. uniform records.
        var sparseKeys = orderedKeys
            .Where(k => totalRecords > 0 && (double)presenceCount.GetValueOrDefault(k) / totalRecords < 0.5)
            .Take(10)
            .ToList();

        var firstKeys = orderedKeys.Take(Math.Min(8, orderedKeys.Count)).ToList();
        var lastKeys = orderedKeys.Skip(Math.Max(0, orderedKeys.Count - 8)).ToList();

        var unknownProfileKeys = profileKeysArg is null
            ? new List<string>()
            : profileKeysArg
                .Where(r => !orderedKeys.Any(k => string.Equals(k, r, StringComparison.OrdinalIgnoreCase)))
                .ToList();

        return JsonSerializer.SerializeToElement(new
        {
            kind = "data_store_json_profile",
            source = "IFileDataStoreService",
            data = new
            {
                fileId,
                filename = metadata.Filename,
                folderPath = metadata.FolderPath,
                contentType = metadata.ContentType,
                sizeBytes = metadata.SizeBytes,
                shape = mode,
                detectionNote,
                totalRecordCount = totalRecords,
                totalRecordCountComplete = scanComplete,
                sawNonObjectRecord = sawNonObject,
                parseErrorCount = parseErrors,
                firstParseError,
                keyAnalysis = new
                {
                    totalKeysSeen = orderedKeys.Count,
                    firstKeys,
                    lastKeys,
                    dominantPattern,
                    dominantCount,
                    dominantShare = orderedKeys.Count > 0 ? (double)dominantCount / orderedKeys.Count : 0,
                    patternStats,
                    longestKey = keyLengths.Count > 0 ? keyLengths.Max() : 0,
                    shortestKey = keyLengths.Count > 0 ? keyLengths.Min() : 0,
                    keyColumns,
                    sparseKeys
                },
                layout = new
                {
                    shape,
                    description = shapeDescription,
                    recommendations
                },
                profiledColumns,
                profiledColumnCount = profiledColumns.Count,
                unknownRequestedProfileKeys = unknownProfileKeys
            }
        });
    }

    private static async Task<JsonElement> InvokeProfileParquetFileAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "profile_data_store_parquet_file";
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return Error(action, "dataStoreId is required.");
        if (!TryReadGuid(args, "fileId", out var fileId))
            return Error(action, "fileId is required.");
        var maxProfiledColumns = ReadInt(args, "maxProfiledColumns") ?? 10;
        if (maxProfiledColumns <= 0) maxProfiledColumns = 10;
        if (maxProfiledColumns > 50) maxProfiledColumns = 50;
        var profileColumnsArg = ReadStringArray(args, "profileColumns");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return Error(action, $"View permission denied on data store {dataStoreId}.");

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        DataStoreFile metadata;
        Stream content;
        try
        {
            (metadata, content) = await files.DownloadAsync(dataStoreId, fileId, ct);
        }
        catch (FileDataStoreFileNotFoundException)
        {
            return Error(action, $"File {fileId} not found in data store {dataStoreId}.");
        }

        if (metadata.SizeBytes > MaxParquetBytes)
        {
            await content.DisposeAsync();
            return Error(action,
                $"File is {metadata.SizeBytes:N0} bytes; Parquet inspection caps at {MaxParquetBytes:N0} bytes.");
        }

        await using var stream = content;
        ParquetReader reader;
        try { reader = await ParquetReader.CreateAsync(stream, cancellationToken: ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error(action, "Parquet file open failed: " + ex.Message);
        }

        using (reader)
        {
            try
            {
                var schema = reader.Schema;
                var dataFields = schema.GetDataFields();
                var rowGroupCount = reader.RowGroupCount;

                // Free totals from the footer.
                long totalRows = 0;
                for (int i = 0; i < rowGroupCount; i++)
                {
                    using var rg = reader.OpenRowGroupReader(i);
                    totalRows += rg.RowCount;
                }

                // Classify field names. Parquet schemas are typically narrower
                // than CSVs so the pattern detection mostly identifies
                // identifier-style names. Wide-pivot Parquets still trigger
                // alpha_name / date_iso dominance correctly.
                var headerNames = dataFields.Select(f => f.Name).ToList();
                var patternCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                var perPattern = new string[dataFields.Length];
                var headerLengths = new int[dataFields.Length];
                for (int i = 0; i < dataFields.Length; i++)
                {
                    var p = ClassifyHeader(dataFields[i].Name);
                    perPattern[i] = p;
                    patternCounts[p] = patternCounts.GetValueOrDefault(p) + 1;
                    headerLengths[i] = dataFields[i].Name.Length;
                }

                // Pick profiled columns.
                List<int> profiledIndices;
                var unknownColumns = new List<string>();
                if (profileColumnsArg is not null && profileColumnsArg.Count > 0)
                {
                    var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < dataFields.Length; i++) lookup.TryAdd(dataFields[i].Name, i);
                    profiledIndices = new List<int>();
                    foreach (var name in profileColumnsArg)
                    {
                        if (lookup.TryGetValue(name, out var idx))
                        {
                            if (!profiledIndices.Contains(idx)) profiledIndices.Add(idx);
                        }
                        else unknownColumns.Add(name);
                    }
                    if (profiledIndices.Count == 0)
                        return Error(action,
                            $"None of the requested profile columns matched. Unknown: {string.Join(", ", unknownColumns)}.");
                }
                else
                {
                    profiledIndices = AutoPickProfiledColumns(headerNames, patternCounts, maxProfiledColumns);
                }

                // Aggregate footer stats across row groups for each profiled
                // column. Min / max / null count aggregate cleanly; distinct
                // is summed as an upper bound (over-counts duplicates that
                // appear in multiple row groups).
                var perColMin = new Dictionary<int, object?>();
                var perColMax = new Dictionary<int, object?>();
                var perColNullCount = new Dictionary<int, long>();
                var perColDistinct = new Dictionary<int, long?>();
                foreach (var idx in profiledIndices)
                {
                    perColMin[idx] = null; perColMax[idx] = null;
                    perColNullCount[idx] = 0; perColDistinct[idx] = null;
                }
                for (int rgIdx = 0; rgIdx < rowGroupCount; rgIdx++)
                {
                    using var rg = reader.OpenRowGroupReader(rgIdx);
                    foreach (var idx in profiledIndices)
                    {
                        var s = rg.GetStatistics(dataFields[idx]);
                        if (s is null) continue;
                        if (s.NullCount.HasValue) perColNullCount[idx] += s.NullCount.Value;
                        if (s.DistinctCount.HasValue)
                            perColDistinct[idx] = (perColDistinct[idx] ?? 0) + s.DistinctCount.Value;
                        if (s.MinValue is not null && CompareParquetValues(s.MinValue, perColMin[idx]) < 0)
                            perColMin[idx] = s.MinValue;
                        if (s.MaxValue is not null && CompareParquetValues(s.MaxValue, perColMax[idx]) > 0)
                            perColMax[idx] = s.MaxValue;
                    }
                }

                // Sample values: decompress only the profiled columns from
                // row group 0. Bounded read.
                var sampleValuesByCol = new Dictionary<int, List<object?>>();
                if (rowGroupCount > 0)
                {
                    using var rg0 = reader.OpenRowGroupReader(0);
                    foreach (var idx in profiledIndices)
                    {
                        var df = dataFields[idx];
                        var col = await rg0.ReadColumnAsync(df, ct);
                        var samples = new List<object?>(5);
                        for (int r = 0; r < col.Data.Length && samples.Count < 5; r++)
                        {
                            var v = col.Data.GetValue(r);
                            if (v is not null) samples.Add(StringifyParquetValue(v));
                        }
                        sampleValuesByCol[idx] = samples;
                    }
                }

                var orderedPatterns = patternCounts.OrderByDescending(kv => kv.Value).ToList();
                var patternStats = orderedPatterns.Select(kv =>
                {
                    var examples = new List<string>();
                    for (int i = 0; i < headerNames.Count && examples.Count < 3; i++)
                        if (perPattern[i] == kv.Key) examples.Add(headerNames[i]);
                    if (headerNames.Count > 6)
                    {
                        for (int i = headerNames.Count - 1; i >= 0 && examples.Count < 5; i--)
                            if (perPattern[i] == kv.Key && !examples.Contains(headerNames[i])) examples.Add(headerNames[i]);
                    }
                    return new
                    {
                        pattern = kv.Key,
                        count = kv.Value,
                        share = headerNames.Count > 0 ? (double)kv.Value / headerNames.Count : 0,
                        examples
                    };
                }).ToList();

                var (shape, shapeDescription, recommendations, dominantPattern, dominantCount, keyColumns) =
                    DetectLayout(headerNames, patternCounts);

                var firstHeaders = headerNames.Take(Math.Min(8, headerNames.Count)).ToList();
                var lastHeaders = headerNames.Skip(Math.Max(0, headerNames.Count - 8)).ToList();

                var profiledColumns = profiledIndices.Select(idx =>
                {
                    var df = dataFields[idx];
                    return new
                    {
                        name = df.Name,
                        index = idx,
                        headerPattern = perPattern[idx],
                        clrType = df.ClrType?.Name ?? "unknown",
                        isNullable = df.IsNullable,
                        isArray = df.IsArray,
                        inferredType = MapParquetType(df),
                        footerStats = new
                        {
                            minValue = perColMin[idx] is null ? null : StringifyParquetValue(perColMin[idx]),
                            maxValue = perColMax[idx] is null ? null : StringifyParquetValue(perColMax[idx]),
                            nullCount = perColNullCount[idx],
                            distinctCount = perColDistinct[idx]
                        },
                        sampleValues = sampleValuesByCol.GetValueOrDefault(idx) ?? new List<object?>()
                    };
                }).ToList();

                return JsonSerializer.SerializeToElement(new
                {
                    kind = "data_store_parquet_profile",
                    source = "IFileDataStoreService + Parquet.Net",
                    data = new
                    {
                        fileId,
                        filename = metadata.Filename,
                        folderPath = metadata.FolderPath,
                        contentType = metadata.ContentType,
                        sizeBytes = metadata.SizeBytes,
                        totalRowCount = totalRows,
                        totalRowCountComplete = true,
                        totalColumnCount = dataFields.Length,
                        rowGroupCount,
                        headerAnalysis = new
                        {
                            firstHeaders,
                            lastHeaders,
                            dominantPattern,
                            dominantCount,
                            dominantShare = headerNames.Count > 0 ? (double)dominantCount / headerNames.Count : 0,
                            patternStats,
                            longestHeader = headerLengths.Length > 0 ? headerLengths.Max() : 0,
                            shortestHeader = headerLengths.Length > 0 ? headerLengths.Min() : 0,
                            keyColumns
                        },
                        layout = new
                        {
                            shape,
                            description = shapeDescription,
                            recommendations
                        },
                        profiledColumns,
                        profiledColumnCount = profiledColumns.Count,
                        unknownRequestedProfileColumns = unknownColumns
                    }
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Error(action, "Parquet profile failed: " + ex.Message);
            }
        }
    }

    // Compare two Parquet footer values for min/max aggregation across row
    // groups. Footer values are box-typed (object?) and arrive as the native
    // CLR type for the column (long, double, DateTime, string, byte[]).
    // Treats null incumbent as "no value yet" so the first observation always
    // wins; matching types go through IComparable; mismatched types degrade
    // to lexical compare so we don't crash on a malformed footer.
    private static int CompareParquetValues(object? candidate, object? incumbent)
    {
        if (incumbent is null) return -1; // any candidate is "less" than no-value, so the first observation always takes
        if (candidate is null) return 1;  // a null candidate should not displace a real value
        if (candidate.GetType() == incumbent.GetType() && candidate is IComparable c)
            return c.CompareTo(incumbent);
        return string.CompareOrdinal(candidate.ToString(), incumbent.ToString());
    }

    // Shared column-selection parameters for the tabular inspectors. Wide
    // files (sensor exports, ML feature tables) routinely have thousands of
    // columns, which blows up the LLM payload if we naively dump every cell
    // for every sample row. The agent picks columns by name or accepts the
    // first-N default, and per-cell length gets clamped so a stray text
    // column can't sneak past the column count.
    private sealed record ColumnSelectionParams(
        bool SchemaOnly,
        IReadOnlyList<string>? RequestedColumns,
        int MaxColumnsInSample,
        int MaxCellLength);

    private static ColumnSelectionParams ReadColumnSelection(JsonElement args)
    {
        var schemaOnly = args.TryGetProperty("schemaOnly", out var so) && so.ValueKind == JsonValueKind.True;
        var requested = ReadStringArray(args, "columns");
        var maxColumnsInSample = ReadInt(args, "maxColumnsInSample") ?? 50;
        if (maxColumnsInSample <= 0) maxColumnsInSample = 50;
        if (maxColumnsInSample > 500) maxColumnsInSample = 500;
        var maxCellLength = ReadInt(args, "maxCellLength") ?? 200;
        if (maxCellLength < 0) maxCellLength = 0;
        if (maxCellLength > 2000) maxCellLength = 2000;
        return new ColumnSelectionParams(schemaOnly, requested, maxColumnsInSample, maxCellLength);
    }

    // Resolve which columns get sample-row + type-inference treatment given
    // the available header list and the caller's column selection. Returns
    // the included-indices set, the indices in the order the caller asked
    // for (or header order when no filter was provided), and a list of
    // requested column names that didn't match any header so the model can
    // report them back to the user. Case-insensitive name matching — CSVs
    // and Excel sheets routinely shift casing across exports.
    private static (HashSet<int> includedSet, List<int> orderedIndices, List<string> unknownColumns, bool truncated)
        ResolveIncludedColumns(IReadOnlyList<string> headers, ColumnSelectionParams sel)
    {
        if (sel.RequestedColumns is not null && sel.RequestedColumns.Count > 0)
        {
            var headerToIdx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++)
                headerToIdx.TryAdd(headers[i], i);
            var includedSet = new HashSet<int>();
            var orderedIndices = new List<int>();
            var unknown = new List<string>();
            foreach (var name in sel.RequestedColumns)
            {
                if (headerToIdx.TryGetValue(name, out var idx))
                {
                    if (includedSet.Add(idx)) orderedIndices.Add(idx);
                }
                else
                {
                    unknown.Add(name);
                }
            }
            return (includedSet, orderedIndices, unknown, truncated: false);
        }
        var max = Math.Min(headers.Count, sel.MaxColumnsInSample);
        var defaultSet = new HashSet<int>();
        var defaultOrder = new List<int>(max);
        for (int i = 0; i < max; i++) { defaultSet.Add(i); defaultOrder.Add(i); }
        return (defaultSet, defaultOrder, unknownColumns: new List<string>(), truncated: max < headers.Count);
    }

    private static List<string>? ReadStringArray(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return null;
        var list = new List<string>();
        foreach (var el in v.EnumerateArray())
            if (el.ValueKind == JsonValueKind.String && el.GetString() is { } s) list.Add(s);
        return list.Count > 0 ? list : null;
    }

    // Clamp a cell value to maxCellLength and append "…" when truncated so
    // the model can tell a real value apart from a clipped one. maxCellLength
    // of 0 collapses every value to empty — useful for "names only" passes.
    private static string? TruncateCell(string? value, int maxCellLength)
    {
        if (value is null) return null;
        if (maxCellLength == 0) return string.Empty;
        return value.Length <= maxCellLength ? value : value[..maxCellLength] + "…";
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
