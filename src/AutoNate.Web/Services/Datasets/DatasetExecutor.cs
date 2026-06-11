using System.Diagnostics;
using System.Security.Claims;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.DataStores.Sql;
using AutoNate.Web.Services.Query;
using AutoNate.Web.Services.Query.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AutoNate.Web.Services.Datasets;

// Unified executor (docs/plans/2026-05-30-data-stores-implementation.md
// Phase 2). The same SQL builder + reader handles Virtual + datastore(SQL)
// and Cached + any source; the difference is only which (schema, table)
// pair the query targets. Virtual + File reads from datastore_files metadata
// in-process; Virtual + Connector is rejected (REST/SMB use Cached only).
public sealed class DatasetExecutor(
    IDatastoresConnectionFactory connectionFactory,
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    ILogger<DatasetExecutor> log) : IDatasetExecutor
{
    public async Task<QueryResult> ExecuteAsync(
        Dataset dataset,
        AqlQuery query,
        IReadOnlyList<QueryColumn> schema,
        ClaimsPrincipal actor,
        int? hardCap,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var mode = (DatasetMode)dataset.Mode;

        if (mode == DatasetMode.Cached)
        {
            return await ExecuteSqlAsync(
                CachedDatasetSchemas.CacheSchemaFor(dataset.Id),
                CachedDatasetSchemas.CacheTableName,
                schema, query, hardCap, sw, cancellationToken);
        }

        // Virtual mode: route by source kind.
        if (string.Equals(dataset.SourceKind, DatasetSourceKinds.DataStore, StringComparison.OrdinalIgnoreCase))
        {
            var datastoreKind = await GetDataStoreKindAsync(dataset.SourceId, cancellationToken);
            if (datastoreKind == DataStores.DataStoreKind.SqlType)
            {
                if (string.IsNullOrWhiteSpace(dataset.SourceTableName))
                {
                    throw new DatasetExecutionException(
                        "Virtual SQL dataset is missing a SourceTableName.");
                }
                var schemaName = SqlDataStoreProvisioner.SchemaNameFor(dataset.SourceId);
                return await ExecuteSqlAsync(
                    schemaName, dataset.SourceTableName!,
                    schema, query, hardCap, sw, cancellationToken);
            }
            if (datastoreKind == DataStores.DataStoreKind.FileType)
            {
                return await ExecuteFileMetadataAsync(
                    dataset, query, schema, hardCap, sw, cancellationToken);
            }
            throw new DatasetExecutionException(
                $"Virtual dataset source datastore kind '{datastoreKind}' is not supported.");
        }

        if (string.Equals(dataset.SourceKind, DatasetSourceKinds.DataConnector, StringComparison.OrdinalIgnoreCase))
        {
            throw new DatasetExecutionException(
                "REST/SMB DataConnector sources require Cached mode — Virtual mode is not supported.");
        }

        throw new DatasetExecutionException($"Unknown dataset source kind '{dataset.SourceKind}'.");
    }

    private async Task<QueryResult> ExecuteSqlAsync(
        string schemaName,
        string tableName,
        IReadOnlyList<QueryColumn> schema,
        AqlQuery query,
        int? hardCap,
        Stopwatch sw,
        CancellationToken cancellationToken)
    {
        if (!connectionFactory.IsEnabled)
        {
            throw new DatasetExecutionException(
                "SqlType DataStores feature is disabled — ConnectionStrings:Datastores is not configured.");
        }
        var built = DatasetSqlBuilder.Build(schemaName, tableName, schema, query, hardCap);
        await using var conn = await connectionFactory.OpenAsync(cancellationToken);
        built.Command.Connection = conn;

        var columns = built.Projection.Count > 0
            ? built.Projection.Select(p => new QueryColumnMeta(p.DisplayName, p.DataType)).ToList()
            : schema.Select(c => new QueryColumnMeta(c.Name, c.DataType)).ToList();
        var rows = new List<IReadOnlyDictionary<string, object?>>();

        await using var reader = await built.Command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                var isNull = await reader.IsDBNullAsync(i, cancellationToken);
                var value = isNull ? null : reader.GetValue(i);
                row[name] = value;
            }
            rows.Add(row);
        }
        log.LogDebug(
            "Dataset SQL query against {Schema}.{Table} returned {Count} rows in {Elapsed}ms.",
            schemaName, tableName, rows.Count, sw.ElapsedMilliseconds);
        return new QueryResult(columns, rows, rows.Count, false, sw.ElapsedMilliseconds);
    }

    private async Task<QueryResult> ExecuteFileMetadataAsync(
        Dataset dataset,
        AqlQuery query,
        IReadOnlyList<QueryColumn> schema,
        int? hardCap,
        Stopwatch sw,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var files = await db.DataStoreFiles.AsNoTracking()
            .Where(f => f.DataStoreId == dataset.SourceId && f.Filename != ".keep")
            .ToListAsync(cancellationToken);

        IEnumerable<IReadOnlyDictionary<string, object?>> rows = files
            .Select(f => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Id"] = f.Id,
                ["FolderPath"] = f.FolderPath,
                ["Filename"] = f.Filename,
                ["SizeBytes"] = f.SizeBytes,
                ["ContentType"] = f.ContentType,
                ["UploadedAtUtc"] = f.UploadedAtUtc,
            });
        if (query.Where is not null)
        {
            rows = rows.Where(r => InMemoryWhere.Match(r, query.Where));
        }
        var materialized = rows.ToList();
        var limit = query.Limit ?? hardCap;
        if (limit is { } l && materialized.Count > l)
        {
            materialized = materialized.Take(l).ToList();
        }
        var columns = schema.Select(c => new QueryColumnMeta(c.Name, c.DataType)).ToList();
        return new QueryResult(columns, materialized, materialized.Count, false, sw.ElapsedMilliseconds);
    }

    private async Task<DataStores.DataStoreKind?> GetDataStoreKindAsync(
        Guid datastoreId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var kind = await db.DataStores.AsNoTracking()
            .Where(d => d.Id == datastoreId)
            .Select(d => (short?)d.Kind)
            .SingleOrDefaultAsync(cancellationToken);
        return kind is null ? null : (DataStores.DataStoreKind)kind.Value;
    }
}
