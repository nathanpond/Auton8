using System.Text;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.DataConnectors;
using AutoNate.Web.Services.DataStores;
using AutoNate.Web.Services.DataStores.Sql;
using AutoNate.Web.Services.Datasets.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AutoNate.Web.Services.Datasets.Cached;

// Drains a dataset's source into its `autonate_datastores.cache_<id>.rows`
// table. v1 truncate-and-reload semantics — every refresh fully repopulates
// the cache. Incremental refresh per connector cursor is Phase 2.1.
public sealed class CachedDatasetMaterializer(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    IDatastoresConnectionFactory connectionFactory,
    IDataConnectorHandlerRegistry handlerRegistry,
    IDatasetStore datasetStore,
    DatasetFileScopeReader fileScopeReader,
    ILogger<CachedDatasetMaterializer> log) : ICachedDatasetMaterializer
{
    public async Task RefreshAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        if (!connectionFactory.IsEnabled)
        {
            throw new InvalidOperationException(
                "Cannot refresh cached dataset: ConnectionStrings:Datastores is not configured.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var dataset = await db.Datasets.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == datasetId, cancellationToken)
            ?? throw new DatasetNotFoundException(datasetId);

        if ((DatasetMode)dataset.Mode != DatasetMode.Cached)
        {
            // Cron / manual refresh on a Virtual dataset is a no-op rather
            // than an error so the projection tick can sweep every dataset
            // without per-row guards.
            return;
        }

        var schema = DatasetSchemaCodec.Decode(dataset.ColumnSchemaJson);
        if (schema.Count == 0)
        {
            throw new InvalidOperationException(
                $"Dataset '{dataset.Name}' has no column schema; cannot materialize.");
        }

        await EnsureCacheTableAsync(dataset.Id, schema, cancellationToken);
        await TruncateAsync(dataset.Id, cancellationToken);

        var rowCount = 0L;
        if (string.Equals(dataset.SourceKind, DatasetSourceKinds.DataStore, StringComparison.OrdinalIgnoreCase))
        {
            var datastoreKind = await GetDataStoreKindAsync(db, dataset.SourceId, cancellationToken);
            if (datastoreKind == DataStoreKind.SqlType)
            {
                rowCount = await CopyFromSqlSourceAsync(dataset, schema, cancellationToken);
            }
            else if (datastoreKind == DataStoreKind.FileType)
            {
                rowCount = await CopyFromFileSourceAsync(dataset, schema, cancellationToken);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unsupported datastore kind '{datastoreKind}' for cached dataset.");
            }
        }
        else if (string.Equals(dataset.SourceKind, DatasetSourceKinds.DataConnector, StringComparison.OrdinalIgnoreCase))
        {
            rowCount = await CopyFromConnectorSourceAsync(dataset, schema, db, cancellationToken);
        }
        else
        {
            throw new InvalidOperationException($"Unknown dataset source kind '{dataset.SourceKind}'.");
        }

        await datasetStore.MarkRefreshedAsync(dataset.Id, DateTime.UtcNow, cancellationToken);
        log.LogInformation(
            "Refreshed cached dataset {Id} ({Name}); {Count} rows materialized.",
            dataset.Id, dataset.Name, rowCount);
    }

    private async Task EnsureCacheTableAsync(
        Guid datasetId, IReadOnlyList<DatasetColumn> schema, CancellationToken ct)
    {
        var schemaName = CachedDatasetSchemas.CacheSchemaFor(datasetId);
        var columnDefs = string.Join(", ", schema.Select(c =>
            $"{DatasetSqlBuilder.QuoteIdent(c.Name)} {EnsureSafeType(c.PostgresType)}"));
        var sql = $$"""
            CREATE SCHEMA IF NOT EXISTS {{DatasetSqlBuilder.QuoteIdent(schemaName)}};
            CREATE TABLE IF NOT EXISTS {{DatasetSqlBuilder.QuoteIdent(schemaName)}}.{{DatasetSqlBuilder.QuoteIdent(CachedDatasetSchemas.CacheTableName)}} ({{columnDefs}});
            """;
        await using var conn = await connectionFactory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task TruncateAsync(Guid datasetId, CancellationToken ct)
    {
        var schemaName = CachedDatasetSchemas.CacheSchemaFor(datasetId);
        var sql = $"TRUNCATE TABLE {DatasetSqlBuilder.QuoteIdent(schemaName)}.{DatasetSqlBuilder.QuoteIdent(CachedDatasetSchemas.CacheTableName)};";
        await using var conn = await connectionFactory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<long> CopyFromSqlSourceAsync(
        Dataset dataset, IReadOnlyList<DatasetColumn> schema, CancellationToken ct)
    {
        var sourceSchema = SqlDataStoreProvisioner.SchemaNameFor(dataset.SourceId);
        var sourceTable = dataset.SourceTableName
            ?? throw new InvalidOperationException("Cached SQL dataset is missing SourceTableName.");
        var cacheSchema = CachedDatasetSchemas.CacheSchemaFor(dataset.Id);
        var columnList = string.Join(", ", schema.Select(c => DatasetSqlBuilder.QuoteIdent(c.Name)));

        var sql = $$"""
            INSERT INTO {{DatasetSqlBuilder.QuoteIdent(cacheSchema)}}.{{DatasetSqlBuilder.QuoteIdent(CachedDatasetSchemas.CacheTableName)}} ({{columnList}})
            SELECT {{columnList}} FROM {{DatasetSqlBuilder.QuoteIdent(sourceSchema)}}.{{DatasetSqlBuilder.QuoteIdent(sourceTable)}};
            """;
        await using var conn = await connectionFactory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        var inserted = await cmd.ExecuteNonQueryAsync(ct);
        return inserted;
    }

    private async Task<long> CopyFromFileSourceAsync(
        Dataset dataset,
        IReadOnlyList<DatasetColumn> schema,
        CancellationToken ct)
    {
        // DatasetFileScopeReader resolves the scope (single file or every
        // immediate-child file of a folder), opens each file's stream, and
        // streams parsed rows. v1 materializes into a list before COPY so
        // the BulkInsertAsync surface is shared with the connector path;
        // folder scopes that don't fit in memory are a Phase 2.1 follow-
        // up (true streaming COPY off the parser's IAsyncEnumerable).
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in fileScopeReader.ReadRowsAsync(dataset, ct))
        {
            rows.Add(row);
        }
        return await BulkInsertAsync(dataset.Id, schema, rows, ct);
    }

    private async Task<long> CopyFromConnectorSourceAsync(
        Dataset dataset,
        IReadOnlyList<DatasetColumn> schema,
        AutoNateDbContext db,
        CancellationToken ct)
    {
        var connector = await db.DataConnectors.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == dataset.SourceId, ct)
            ?? throw new InvalidOperationException(
                $"Connector source '{dataset.SourceId}' not found for dataset '{dataset.Name}'.");
        if (!handlerRegistry.TryGet(connector.Kind, out var handler))
        {
            throw new InvalidOperationException(
                $"No handler registered for connector kind '{connector.Kind}'.");
        }
        var state = new ConnectorRefreshState(connector.LastFetchedAtUtc, connector.Cursor);
        var sink = new BufferingFetchSink();
        await handler.FetchAsync(connector, state, sink, ct);
        return await BulkInsertAsync(dataset.Id, schema, sink.Rows, ct);
    }

    private async Task<long> BulkInsertAsync(
        Guid datasetId,
        IReadOnlyList<DatasetColumn> schema,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0) return 0;
        var schemaName = CachedDatasetSchemas.CacheSchemaFor(datasetId);
        var columnList = string.Join(", ", schema.Select(c => DatasetSqlBuilder.QuoteIdent(c.Name)));
        var copySql =
            $"COPY {DatasetSqlBuilder.QuoteIdent(schemaName)}.{DatasetSqlBuilder.QuoteIdent(CachedDatasetSchemas.CacheTableName)} ({columnList}) FROM STDIN (FORMAT BINARY)";
        await using var conn = await connectionFactory.OpenAsync(ct);
        await using var writer = await conn.BeginBinaryImportAsync(copySql, ct);
        foreach (var row in rows)
        {
            await writer.StartRowAsync(ct);
            foreach (var col in schema)
            {
                var value = LookupValue(row, col.Name);
                if (value is null)
                {
                    await writer.WriteNullAsync(ct);
                    continue;
                }
                await WriteTypedAsync(writer, col.PostgresType, value, ct);
            }
        }
        await writer.CompleteAsync(ct);
        return rows.Count;
    }

    private static object? LookupValue(IReadOnlyDictionary<string, object?> row, string field)
    {
        foreach (var kv in row)
        {
            if (string.Equals(kv.Key, field, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }
        return null;
    }

    private static async Task WriteTypedAsync(
        NpgsqlBinaryImporter writer, string pgType, object value, CancellationToken ct)
    {
        switch (pgType)
        {
            case "bigint":
                await writer.WriteAsync(Convert.ToInt64(value), NpgsqlTypes.NpgsqlDbType.Bigint, ct);
                break;
            case "double precision":
                await writer.WriteAsync(Convert.ToDouble(value), NpgsqlTypes.NpgsqlDbType.Double, ct);
                break;
            case "boolean":
                await writer.WriteAsync(Convert.ToBoolean(value), NpgsqlTypes.NpgsqlDbType.Boolean, ct);
                break;
            case "timestamptz":
                await writer.WriteAsync(
                    value is DateTime dt
                        ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                        : DateTime.Parse(value.ToString() ?? string.Empty, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime(),
                    NpgsqlTypes.NpgsqlDbType.TimestampTz, ct);
                break;
            default:
                await writer.WriteAsync(value.ToString() ?? string.Empty, NpgsqlTypes.NpgsqlDbType.Text, ct);
                break;
        }
    }

    private static string EnsureSafeType(string pgType) => pgType switch
    {
        "bigint" or "double precision" or "boolean" or "timestamptz" or "text" => pgType,
        _ => "text",
    };

    private static async Task<DataStoreKind?> GetDataStoreKindAsync(
        AutoNateDbContext db, Guid datastoreId, CancellationToken ct)
    {
        var kind = await db.DataStores.AsNoTracking()
            .Where(d => d.Id == datastoreId)
            .Select(d => (short?)d.Kind)
            .SingleOrDefaultAsync(ct);
        return kind is null ? null : (DataStoreKind)kind.Value;
    }

    private sealed class BufferingFetchSink : IConnectorFetchSink
    {
        public List<IReadOnlyDictionary<string, object?>> Rows { get; } = new();

        public Task WriteRowAsync(IReadOnlyDictionary<string, object?> row, CancellationToken cancellationToken = default)
        {
            Rows.Add(row);
            return Task.CompletedTask;
        }

        public async Task WriteBlobAsync(string filename, Stream content, CancellationToken cancellationToken = default)
        {
            // Blobs aren't materialized into the row cache; they belong on
            // disk under a future per-dataset blob root. Drain the stream
            // so callers can dispose it cleanly.
            using var sink = new MemoryStream();
            await content.CopyToAsync(sink, cancellationToken);
        }
    }
}
