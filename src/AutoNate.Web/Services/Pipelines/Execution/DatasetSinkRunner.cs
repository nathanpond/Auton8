using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Datasets;
using AutoNate.Web.Services.Datasets.Cached;
using AutoNate.Web.Services.DataStores.Sql;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoNate.Web.Services.Pipelines.Execution;

// Writes the upstream DataFrame into a Cached Dataset's `autonate_datastores.cache_<id>.rows`
// table. The dataset must already exist with Mode=Cached; the sink does not
// create datasets implicitly (an unsupported sink target is a clear error
// rather than silently auto-creating something Phase 6 would have to clean
// up). Truncate-and-reload semantics match CachedDatasetMaterializer.
public sealed class DatasetSinkRunner(
    IDatasetStore datasetStore,
    IDatastoresConnectionFactory connectionFactory,
    IDbContextFactory<AutoNateDbContext> dbContextFactory) : INodeRunner
{
    public string Kind => PipelineNodeKinds.DatasetSink;

    public async Task<DataFrame?> RunAsync(NodeRunnerContext context, CancellationToken cancellationToken = default)
    {
        if (!connectionFactory.IsEnabled)
        {
            throw new InvalidOperationException(
                "DatasetSinkRunner requires ConnectionStrings:Datastores; SqlType DataStores are disabled.");
        }
        if (context.Inputs.Count == 0)
        {
            return null;
        }
        var datasetName = context.Node.Key;
        var dataset = await datasetStore.GetByNameAsync(datasetName, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Pipeline node '{context.Node.Id}' (dataset-sink) references dataset '{datasetName}', which does not exist.");
        if ((DatasetMode)dataset.Mode != DatasetMode.Cached)
        {
            throw new InvalidOperationException(
                $"Pipeline node '{context.Node.Id}' targets dataset '{datasetName}', which is not Cached. " +
                "Only Cached datasets can be pipeline sinks.");
        }

        var input = context.Inputs[0];
        var schemaName = CachedDatasetSchemas.CacheSchemaFor(dataset.Id);
        await EnsureCacheTableAsync(schemaName, dataset, input, cancellationToken);
        await TruncateAsync(schemaName, cancellationToken);
        var inserted = await BinaryCopyAsync(schemaName, dataset, input, cancellationToken);
        // Refresh-stamp the dataset alongside the sink write so the dataset
        // list page reflects the in-pipeline write the same way a manual
        // refresh would.
        await datasetStore.MarkRefreshedAsync(dataset.Id, DateTime.UtcNow, cancellationToken);
        _ = inserted;
        _ = dbContextFactory;
        return null;
    }

    private async Task EnsureCacheTableAsync(
        string schemaName,
        Persistence.Scaffolded.Dataset dataset,
        DataFrame input,
        CancellationToken cancellationToken)
    {
        var columns = DecodeOrInfer(dataset, input);
        var columnDefs = string.Join(", ", columns.Select(c =>
            $"\"{c.Name}\" {ToPostgresType(c.Type)}"));
        var sql = $$"""
            CREATE SCHEMA IF NOT EXISTS "{{schemaName}}";
            CREATE TABLE IF NOT EXISTS "{{schemaName}}"."{{CachedDatasetSchemas.CacheTableName}}" ({{columnDefs}});
            """;
        await using var conn = await connectionFactory.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task TruncateAsync(string schemaName, CancellationToken cancellationToken)
    {
        var sql = $"TRUNCATE TABLE \"{schemaName}\".\"{CachedDatasetSchemas.CacheTableName}\";";
        await using var conn = await connectionFactory.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<long> BinaryCopyAsync(
        string schemaName,
        Persistence.Scaffolded.Dataset dataset,
        DataFrame input,
        CancellationToken cancellationToken)
    {
        var columns = DecodeOrInfer(dataset, input);
        if (columns.Count == 0 || input.Rows.Count == 0) return 0;
        var columnList = string.Join(", ", columns.Select(c => $"\"{c.Name}\""));
        var copySql =
            $"COPY \"{schemaName}\".\"{CachedDatasetSchemas.CacheTableName}\" ({columnList}) FROM STDIN (FORMAT BINARY)";
        await using var conn = await connectionFactory.OpenAsync(cancellationToken);
        await using var writer = await conn.BeginBinaryImportAsync(copySql, cancellationToken);
        var rowCount = 0L;
        foreach (var row in input.Rows)
        {
            await writer.StartRowAsync(cancellationToken);
            foreach (var col in columns)
            {
                var value = LookupValue(row, col.Name);
                if (value is null) { await writer.WriteNullAsync(cancellationToken); continue; }
                await WriteTypedAsync(writer, col.Type, value, cancellationToken);
            }
            rowCount++;
        }
        await writer.CompleteAsync(cancellationToken);
        return rowCount;
    }

    private static IReadOnlyList<DataColumn> DecodeOrInfer(Persistence.Scaffolded.Dataset dataset, DataFrame input)
    {
        // Prefer the dataset's declared schema; fall back to the frame's
        // own columns when the dataset is brand-new (graph creation without
        // a column-schema upload).
        var stored = DatasetSchemaCodec.Decode(dataset.ColumnSchemaJson);
        if (stored.Count > 0)
        {
            return stored.Select(c => new DataColumn(c.Name, MapPostgresType(c.PostgresType))).ToList();
        }
        return input.Columns;
    }

    private static DataColumnType MapPostgresType(string pgType) => pgType switch
    {
        "bigint" => DataColumnType.Integer,
        "double precision" => DataColumnType.Number,
        "boolean" => DataColumnType.Boolean,
        "timestamptz" => DataColumnType.Date,
        _ => DataColumnType.Text,
    };

    private static string ToPostgresType(DataColumnType t) => t switch
    {
        DataColumnType.Integer => "bigint",
        DataColumnType.Number => "double precision",
        DataColumnType.Boolean => "boolean",
        DataColumnType.Date => "timestamptz",
        _ => "text",
    };

    private static object? LookupValue(IReadOnlyDictionary<string, object?> row, string name)
    {
        if (row.TryGetValue(name, out var v)) return v;
        foreach (var kv in row)
        {
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        }
        return null;
    }

    private static async Task WriteTypedAsync(
        NpgsqlBinaryImporter writer, DataColumnType t, object value, CancellationToken cancellationToken)
    {
        switch (t)
        {
            case DataColumnType.Integer:
                await writer.WriteAsync(Convert.ToInt64(value), NpgsqlTypes.NpgsqlDbType.Bigint, cancellationToken);
                break;
            case DataColumnType.Number:
                await writer.WriteAsync(Convert.ToDouble(value), NpgsqlTypes.NpgsqlDbType.Double, cancellationToken);
                break;
            case DataColumnType.Boolean:
                await writer.WriteAsync(Convert.ToBoolean(value), NpgsqlTypes.NpgsqlDbType.Boolean, cancellationToken);
                break;
            case DataColumnType.Date:
                await writer.WriteAsync(
                    value is DateTime dt
                        ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                        : DateTime.Parse(value.ToString() ?? string.Empty, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime(),
                    NpgsqlTypes.NpgsqlDbType.TimestampTz,
                    cancellationToken);
                break;
            case DataColumnType.Json:
                await writer.WriteAsync(value.ToString() ?? string.Empty, NpgsqlTypes.NpgsqlDbType.Jsonb, cancellationToken);
                break;
            default:
                await writer.WriteAsync(value.ToString() ?? string.Empty, NpgsqlTypes.NpgsqlDbType.Text, cancellationToken);
                break;
        }
    }
}
