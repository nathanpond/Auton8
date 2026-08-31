namespace AutoNate.Web.Persistence.Scaffolded;

// Dataset metadata row (docs/plans/2026-05-30-data-stores-implementation.md
// Phase 2). One Dataset = one queryable surface fronted by AQL via
// `FROM Dataset("<name>")`. Virtual datasets execute against their source
// in-place; Cached datasets are materialized into
// `autonate_datastores.cache_<id>` by DatasetRefreshProjection.
public partial class Dataset
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    // 1 = Virtual (passthrough), 2 = Cached. See
    // AutoNate.Web.Services.Datasets.DatasetMode.
    public short Mode { get; set; }

    // JSON-encoded column schema: `[{name, type}, ...]` where type is one of
    // the AQL column-type strings (text, bigint, double precision, boolean,
    // timestamptz). Locked at Dataset creation time so the AQL surface has a
    // stable contract regardless of underlying source drift.
    public string ColumnSchemaJson { get; set; } = "[]";

    // Cron expression (5-field) for cached refresh. NULL for Virtual datasets;
    // also NULL for Cached datasets whose refresh is manual-only.
    public string? RefreshCron { get; set; }

    // Last successful refresh (Cached datasets only).
    public DateTime? LastRefreshedAtUtc { get; set; }

    // For v1 a Dataset has exactly one source (multi-source aggregation is
    // a Phase 2.1/3 follow-up — the validator rejects multi-source datasets
    // with a clean error today). SourceKind = "datastore" | "dataconnector".
    public string SourceKind { get; set; } = "datastore";

    public Guid SourceId { get; set; }

    // For datastore sources, names the table within the per-datastore schema
    // (`ds_<datastoreid>.<TableName>`). NULL for connector sources and for
    // FileType datastores (file scope is carried by the file_scope_* /
    // parser_* columns below).
    public string? SourceTableName { get; set; }

    // Files-datastore scope. Required when SourceKind="datastore" and the
    // datastore's Kind is FileType; NULL otherwise. "file" → FileScopePath
    // is the full file path (folder + filename); "folder" → FileScopePath
    // is a folder path and every immediate-child file participates as one
    // row stream (strict schema match, .keep excluded).
    public string? FileScopeKind { get; set; }

    public string? FileScopePath { get; set; }

    // Content parser. "csv" today; registry-based so other formats can be
    // added without changing the executor.
    public string? ParserKind { get; set; }

    // Flat string→string options consumed by the parser (CSV: delimiter,
    // encoding, hasHeader). JSON-encoded so a parser can grow its option
    // surface without a schema migration.
    public string? ParserOptionsJson { get; set; }

    public Guid OwnerUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public Guid UpdatedBy { get; set; }
}
