using System.Globalization;
using System.Text.Json;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AutoNate.Web.Services.DataStores.Sql;

public sealed record class CsvIngestPreview(
    string SuggestedTableName,
    IReadOnlyList<CsvColumn> Columns,
    int SampleRowCount);

public sealed record class CsvColumn(string Name, string PostgresType);

public sealed record class CsvIngestResult(Guid TableId, string SchemaName, string TableName, long RowsInserted);

// CSV → Postgres table ingestor for SqlType DataStores. v1 is straight-
// through: read the header, infer column types from a sample, CREATE
// TABLE, COPY remaining rows in. Schema confirmation in the SPA happens
// against PreviewAsync; commit is IngestAsync.
public sealed class CsvIngestor(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    IDatastoresConnectionFactory connectionFactory,
    ILogger<CsvIngestor> log)
{
    private const int SampleSize = 200;
    private const long MaxColumnCount = 256;

    public async Task<CsvIngestPreview> PreviewAsync(
        Stream csv, string filename, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(csv);
        using var reader = new StreamReader(csv, leaveOpen: true);
        using var parser = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null,
        });
        if (!await parser.ReadAsync() || !parser.ReadHeader())
        {
            throw new InvalidOperationException("CSV has no header row.");
        }
        var headers = parser.HeaderRecord
            ?? throw new InvalidOperationException("CSV header could not be parsed.");
        if (headers.Length == 0 || headers.Length > MaxColumnCount)
        {
            throw new InvalidOperationException($"CSV column count {headers.Length} is out of range (1..{MaxColumnCount}).");
        }

        var sample = new List<string?[]>(SampleSize);
        while (sample.Count < SampleSize && await parser.ReadAsync())
        {
            var row = new string?[headers.Length];
            for (var i = 0; i < headers.Length; i++)
            {
                row[i] = parser.GetField(i);
            }
            sample.Add(row);
        }

        var columns = new List<CsvColumn>(headers.Length);
        for (var i = 0; i < headers.Length; i++)
        {
            var name = SanitizeColumnName(headers[i], i);
            var inferred = InferType(sample, i);
            columns.Add(new CsvColumn(name, inferred));
        }
        var suggestedTable = SanitizeTableName(filename);
        return new CsvIngestPreview(suggestedTable, columns, sample.Count);
    }

    public async Task<CsvIngestResult> IngestAsync(
        Guid dataStoreId,
        string tableName,
        IReadOnlyList<CsvColumn> columns,
        Stream csv,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0)
            throw new ArgumentException("At least one column is required.", nameof(columns));
        if (!connectionFactory.IsEnabled)
            throw new InvalidOperationException("SqlType DataStores feature is disabled.");

        var sanitizedTable = SanitizeTableName(tableName);
        var schema = SqlDataStoreProvisioner.SchemaNameFor(dataStoreId);
        var qualified = $"\"{schema}\".\"{sanitizedTable}\"";

        // Create table.
        await using (var conn = await connectionFactory.OpenAsync(cancellationToken))
        {
            var columnDefs = string.Join(", ", columns.Select(c =>
                $"\"{c.Name}\" {EnsureSafePostgresType(c.PostgresType)}"));
            var createSql = $"CREATE TABLE IF NOT EXISTS {qualified} ({columnDefs});";
            await using var cmd = new NpgsqlCommand(createSql, conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Copy rows via binary COPY.
        long rowsInserted = 0;
        using (var reader = new StreamReader(csv, leaveOpen: true))
        using (var parser = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null,
        }))
        {
            if (!await parser.ReadAsync() || !parser.ReadHeader())
                throw new InvalidOperationException("CSV has no header row.");

            await using var conn = await connectionFactory.OpenAsync(cancellationToken);
            var columnList = string.Join(", ", columns.Select(c => $"\"{c.Name}\""));
            var copySql = $"COPY {qualified} ({columnList}) FROM STDIN (FORMAT BINARY)";
            await using var writer = await conn.BeginBinaryImportAsync(copySql, cancellationToken);
            while (await parser.ReadAsync())
            {
                await writer.StartRowAsync(cancellationToken);
                for (var i = 0; i < columns.Count; i++)
                {
                    var raw = parser.GetField(i);
                    if (raw is null || raw.Length == 0)
                    {
                        await writer.WriteNullAsync(cancellationToken);
                        continue;
                    }
                    await WriteTypedAsync(writer, columns[i].PostgresType, raw, cancellationToken);
                }
                rowsInserted++;
            }
            await writer.CompleteAsync(cancellationToken);
        }

        // Metadata row.
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = new DataStoreTable
        {
            Id = Guid.NewGuid(),
            DataStoreId = dataStoreId,
            SchemaName = schema,
            TableName = sanitizedTable,
            ColumnSchemaJson = JsonSerializer.Serialize(columns),
            RowCount = rowsInserted,
            CreatedBy = actorId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.DataStoreTables.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        log.LogInformation(
            "CSV ingested {Rows} rows into {Schema}.{Table} for datastore {Id}.",
            rowsInserted, schema, sanitizedTable, dataStoreId);
        return new CsvIngestResult(entity.Id, schema, sanitizedTable, rowsInserted);
    }

    private static async Task WriteTypedAsync(
        NpgsqlBinaryImporter writer, string pgType, string raw, CancellationToken ct)
    {
        switch (pgType)
        {
            case "bigint":
                if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                    await writer.WriteAsync(l, NpgsqlTypes.NpgsqlDbType.Bigint, ct);
                else
                    await writer.WriteNullAsync(ct);
                break;
            case "double precision":
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    await writer.WriteAsync(d, NpgsqlTypes.NpgsqlDbType.Double, ct);
                else
                    await writer.WriteNullAsync(ct);
                break;
            case "boolean":
                if (bool.TryParse(raw, out var b))
                    await writer.WriteAsync(b, NpgsqlTypes.NpgsqlDbType.Boolean, ct);
                else
                    await writer.WriteNullAsync(ct);
                break;
            case "timestamptz":
                if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                    await writer.WriteAsync(dt, NpgsqlTypes.NpgsqlDbType.TimestampTz, ct);
                else
                    await writer.WriteNullAsync(ct);
                break;
            default:
                await writer.WriteAsync(raw, NpgsqlTypes.NpgsqlDbType.Text, ct);
                break;
        }
    }

    private static string InferType(IReadOnlyList<string?[]> sample, int columnIndex)
    {
        if (sample.Count == 0) return "text";
        var allInt = true;
        var allDouble = true;
        var allBool = true;
        var allDateTime = true;
        var anyValue = false;
        foreach (var row in sample)
        {
            if (columnIndex >= row.Length) continue;
            var v = row[columnIndex];
            if (v is null || v.Length == 0) continue;
            anyValue = true;
            if (allInt && !long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                allInt = false;
            if (allDouble && !double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                allDouble = false;
            if (allBool && !(string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(v, "false", StringComparison.OrdinalIgnoreCase)))
                allBool = false;
            if (allDateTime && !DateTime.TryParse(v, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _))
                allDateTime = false;
        }
        if (!anyValue) return "text";
        if (allInt) return "bigint";
        if (allDouble) return "double precision";
        if (allBool) return "boolean";
        if (allDateTime) return "timestamptz";
        return "text";
    }

    private static string SanitizeColumnName(string raw, int index)
    {
        var trimmed = (raw ?? "").Trim();
        if (trimmed.Length == 0) trimmed = $"col_{index + 1}";
        var sb = new System.Text.StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_') sb.Append(char.ToLowerInvariant(ch));
            else sb.Append('_');
        }
        var name = sb.ToString();
        if (name.Length == 0 || !char.IsLetter(name[0])) name = "c_" + name;
        if (name.Length > 63) name = name[..63];
        return name;
    }

    private static string SanitizeTableName(string raw)
    {
        var withoutExt = Path.GetFileNameWithoutExtension(raw ?? "");
        var sanitized = SanitizeColumnName(withoutExt, 0);
        return string.IsNullOrWhiteSpace(sanitized) ? "table_1" : sanitized;
    }

    // Allowlist Postgres types to prevent injection via CSV preview round-trip.
    private static string EnsureSafePostgresType(string pgType) => pgType switch
    {
        "bigint" or "double precision" or "boolean" or "timestamptz" or "text" => pgType,
        _ => "text",
    };
}
