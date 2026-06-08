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

// Drives CsvIngestor.IngestAsync when the target table already exists.
// Insert = fail with a conflict so the operator can choose what to do.
// Append = add the new rows on top, but only if (name,type) sets match.
// Replace = drop + recreate + COPY the new rows.
public enum CsvIngestMode
{
    Insert = 0,
    Append = 1,
    Replace = 2,
}

public sealed record class CsvIngestResult(
    Guid TableId,
    string SchemaName,
    string TableName,
    long RowsInserted,
    bool Replaced,
    bool Appended,
    long? PreviousRowCount,
    bool SchemaChanged);

// Thrown by CsvIngestor.IngestAsync when a metadata row for the target
// (datastoreId, sanitizedTable) already exists and the caller did not opt
// in to replace. The endpoint converts this to a 409 carrying these fields
// so the SPA can present the schema-diff / bound-dataset impact warning
// before retrying with mode=replace.
public sealed class DataStoreTableExistsException(
    Guid existingTableId,
    string sanitizedTableName,
    long existingRowCount,
    IReadOnlyList<CsvColumn> existingColumns)
    : Exception($"A table named '{sanitizedTableName}' already exists in this datastore.")
{
    public Guid ExistingTableId { get; } = existingTableId;
    public string SanitizedTableName { get; } = sanitizedTableName;
    public long ExistingRowCount { get; } = existingRowCount;
    public IReadOnlyList<CsvColumn> ExistingColumns { get; } = existingColumns;
}

// Thrown when mode=Append but the new CSV's (name,type) set differs from
// the existing table's schema. Carries both schemas so the endpoint can
// surface a 409 the SPA renders as a same-as-conflict view with the
// Append button forced disabled.
public sealed class DataStoreTableSchemaMismatchException(
    Guid existingTableId,
    string sanitizedTableName,
    long existingRowCount,
    IReadOnlyList<CsvColumn> existingColumns,
    IReadOnlyList<CsvColumn> incomingColumns)
    : Exception($"Append rejected — table '{sanitizedTableName}' has a different schema than the uploaded CSV.")
{
    public Guid ExistingTableId { get; } = existingTableId;
    public string SanitizedTableName { get; } = sanitizedTableName;
    public long ExistingRowCount { get; } = existingRowCount;
    public IReadOnlyList<CsvColumn> ExistingColumns { get; } = existingColumns;
    public IReadOnlyList<CsvColumn> IncomingColumns { get; } = incomingColumns;
}

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
        CsvIngestMode mode = CsvIngestMode.Insert,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0)
            throw new ArgumentException("At least one column is required.", nameof(columns));
        if (!connectionFactory.IsEnabled)
            throw new InvalidOperationException("SqlType DataStores feature is disabled.");

        // Re-sanitize: the caller-supplied columns aren't required to be the
        // unmodified PreviewAsync output, so we can't assume names already
        // passed SanitizeColumnName. They land in `CREATE TABLE "..."` and
        // `COPY ("...")` quoted identifiers below; an unescaped `"` would
        // break out of the quote.
        var sanitized = new List<CsvColumn>(columns.Count);
        for (var i = 0; i < columns.Count; i++)
        {
            sanitized.Add(new CsvColumn(SanitizeColumnName(columns[i].Name, i), columns[i].PostgresType));
        }
        columns = sanitized;

        var sanitizedTable = SanitizeTableName(tableName);
        var schema = SqlDataStoreProvisioner.SchemaNameFor(dataStoreId);
        var qualified = $"\"{schema}\".\"{sanitizedTable}\"";

        // Conflict + mode handling. The (datastoreId, schemaName, tableName)
        // unique index would catch a duplicate at SaveChanges time, but EF
        // surfaces that as a generic DbUpdateException — much harder for the
        // endpoint layer to translate into a structured 409 carrying the
        // existing schema. Do the lookup explicitly so the conflict path is
        // first-class. The race against a concurrent ingest still leans on
        // the unique constraint; that path produces a 500 which is the right
        // outcome for a genuinely concurrent operator action.
        await using var preDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await preDb.DataStoreTables.AsNoTracking()
            .SingleOrDefaultAsync(
                t => t.DataStoreId == dataStoreId && t.TableName == sanitizedTable,
                cancellationToken);

        var schemaChanged = false;
        long? previousRowCount = null;
        var isAppend = false;
        if (existing is not null)
        {
            var existingColumns = DecodeColumns(existing.ColumnSchemaJson);
            previousRowCount = existing.RowCount;

            switch (mode)
            {
                case CsvIngestMode.Insert:
                    throw new DataStoreTableExistsException(
                        existing.Id, sanitizedTable, existing.RowCount, existingColumns);

                case CsvIngestMode.Append:
                    // Append refuses on any schema delta — the COPY would
                    // either fail on a missing column or silently NULL out
                    // a value the operator expected to be present. Force
                    // them through the conflict UI to either fix the CSV or
                    // pick Replace explicitly.
                    if (!ColumnsEqualBySet(existingColumns, columns))
                    {
                        throw new DataStoreTableSchemaMismatchException(
                            existing.Id, sanitizedTable, existing.RowCount,
                            existingColumns, columns);
                    }
                    // Use the existing table's column order/types for the
                    // COPY so a reordered CSV still lines up correctly.
                    columns = existingColumns;
                    isAppend = true;
                    break;

                case CsvIngestMode.Replace:
                    schemaChanged = !ColumnsEqualBySet(existingColumns, columns);
                    // Drop the physical table; the metadata row gets reused
                    // below so per-table grants + dataset bindings keep
                    // pointing at the same record.
                    await using (var dropConn = await connectionFactory.OpenAsync(cancellationToken))
                    {
                        await using var dropCmd = new NpgsqlCommand($"DROP TABLE IF EXISTS {qualified} CASCADE;", dropConn);
                        await dropCmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                    log.LogInformation(
                        "Dropped existing table {Schema}.{Table} for replace-ingest in datastore {Id}.",
                        schema, sanitizedTable, dataStoreId);
                    break;
            }
        }

        // Create table. Append re-runs CREATE TABLE IF NOT EXISTS as a
        // belt-and-braces check; the schema-set match above already
        // verified the columns match.
        if (!isAppend)
        {
            await using var conn = await connectionFactory.OpenAsync(cancellationToken);
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

            // For append, the canonical column order comes from the existing
            // table — map the incoming CSV's header positions to the
            // canonical names so the COPY writes the right field into the
            // right column even if the CSV's columns are reordered.
            var csvHeaders = parser.HeaderRecord
                ?? throw new InvalidOperationException("CSV header could not be parsed.");
            var csvHeaderIndexByCanonicalName = new Dictionary<string, int>(columns.Count, StringComparer.Ordinal);
            for (var i = 0; i < csvHeaders.Length; i++)
            {
                var canonical = SanitizeColumnName(csvHeaders[i], i);
                if (!csvHeaderIndexByCanonicalName.ContainsKey(canonical))
                    csvHeaderIndexByCanonicalName[canonical] = i;
            }

            await using var conn = await connectionFactory.OpenAsync(cancellationToken);
            var columnList = string.Join(", ", columns.Select(c => $"\"{c.Name}\""));
            var copySql = $"COPY {qualified} ({columnList}) FROM STDIN (FORMAT BINARY)";
            await using var writer = await conn.BeginBinaryImportAsync(copySql, cancellationToken);
            while (await parser.ReadAsync())
            {
                await writer.StartRowAsync(cancellationToken);
                for (var i = 0; i < columns.Count; i++)
                {
                    var csvIdx = csvHeaderIndexByCanonicalName.TryGetValue(columns[i].Name, out var idx) ? idx : i;
                    var raw = parser.GetField(csvIdx);
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

        // Metadata row — UPDATE on replace/append (preserves Id), INSERT on
        // first ingest. Append adds to the prior row count; replace resets.
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        DataStoreTable entity;
        if (existing is not null)
        {
            entity = await db.DataStoreTables.SingleAsync(t => t.Id == existing.Id, cancellationToken);
            if (isAppend)
            {
                entity.RowCount = existing.RowCount + rowsInserted;
                // ColumnSchemaJson stays as-is — we already enforced match.
            }
            else
            {
                entity.ColumnSchemaJson = JsonSerializer.Serialize(columns);
                entity.RowCount = rowsInserted;
            }
        }
        else
        {
            entity = new DataStoreTable
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
        }
        await db.SaveChangesAsync(cancellationToken);
        log.LogInformation(
            "CSV {Mode} {Rows} rows into {Schema}.{Table} for datastore {Id}.",
            existing is null ? "ingested" : isAppend ? "appended" : "re-ingested",
            rowsInserted, schema, sanitizedTable, dataStoreId);
        return new CsvIngestResult(
            entity.Id, schema, sanitizedTable, rowsInserted,
            Replaced: existing is not null && !isAppend,
            Appended: isAppend,
            PreviousRowCount: previousRowCount,
            SchemaChanged: schemaChanged);
    }

    private static IReadOnlyList<CsvColumn> DecodeColumns(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<CsvColumn>();
        try
        {
            return JsonSerializer.Deserialize<List<CsvColumn>>(json) ?? new List<CsvColumn>();
        }
        catch (JsonException)
        {
            // A hand-edited or corrupted row shouldn't block the replace
            // path — fall through with an empty list, which forces the
            // schemaChanged flag on (every new column looks like an add).
            return Array.Empty<CsvColumn>();
        }
    }

    // Set-equality on (name, postgresType). Order doesn't matter — COPY
    // names the columns explicitly — but each column in one side must have
    // an exact match (same name + same type) in the other.
    private static bool ColumnsEqualBySet(IReadOnlyList<CsvColumn> a, IReadOnlyList<CsvColumn> b)
    {
        if (a.Count != b.Count) return false;
        var aByName = a.ToDictionary(c => c.Name, c => c.PostgresType, StringComparer.Ordinal);
        foreach (var col in b)
        {
            if (!aByName.TryGetValue(col.Name, out var aType)) return false;
            if (!string.Equals(aType, col.PostgresType, StringComparison.Ordinal)) return false;
        }
        return true;
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
