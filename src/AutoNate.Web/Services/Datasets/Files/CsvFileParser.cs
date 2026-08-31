using System.Globalization;
using System.Runtime.CompilerServices;
using AutoNate.Web.Services.DataStores.Sql;
using CsvHelper;
using CsvHelper.Configuration;

namespace AutoNate.Web.Services.Datasets.Files;

// CSV parser for Files-datastore-backed datasets. Reuses the same
// type-inference + column-name sanitization heuristics CsvIngestor uses
// for SqlType ingest so the inferred schema matches whatever the user
// would have seen ingesting the same file into a SqlType datastore.
//
// Options (flat string→string, persisted in datasets.parser_options):
//   delimiter  — single character, default ","
//   hasHeader  — "true" / "false", default "true"
//
// Encoding is UTF-8 today. Source file bytes live on disk; the executor
// hands the parser a Stream so we don't pin file paths into this layer.
public sealed class CsvFileParser : IDatasetFileParser
{
    public const string KindName = "csv";

    private const int SampleSize = 200;
    private const int MaxColumnCount = 256;

    public string Kind => KindName;

    public async Task<IReadOnlyList<DatasetColumn>> PreviewAsync(
        Stream stream,
        IReadOnlyDictionary<string, string>? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var config = BuildConfig(options);
        using var reader = new StreamReader(stream, leaveOpen: true);
        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync())
        {
            return Array.Empty<DatasetColumn>();
        }
        string[] headers;
        if (config.HasHeaderRecord)
        {
            if (!csv.ReadHeader() || csv.HeaderRecord is null)
            {
                throw new InvalidOperationException("CSV has no header row.");
            }
            headers = csv.HeaderRecord;
        }
        else
        {
            var first = ReadRecord(csv);
            headers = new string[first.Length];
            for (var i = 0; i < first.Length; i++) headers[i] = $"col_{i + 1}";
        }
        if (headers.Length == 0 || headers.Length > MaxColumnCount)
        {
            throw new InvalidOperationException(
                $"CSV column count {headers.Length} is out of range (1..{MaxColumnCount}).");
        }

        var sample = new List<string?[]>(SampleSize);
        while (sample.Count < SampleSize && await csv.ReadAsync())
        {
            sample.Add(ReadRecord(csv));
        }

        var columns = new List<DatasetColumn>(headers.Length);
        for (var i = 0; i < headers.Length; i++)
        {
            var name = CsvSchemaHeuristics.SanitizeColumnName(headers[i], i);
            var type = CsvSchemaHeuristics.InferType(sample, i);
            columns.Add(new DatasetColumn(name, type));
        }
        return columns;
    }

    public IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ReadAsync(
        Stream stream,
        string sourcePath,
        IReadOnlyList<DatasetColumn> expectedSchema,
        IReadOnlyDictionary<string, string>? options,
        CancellationToken cancellationToken)
    {
        // Split into a wrapper + iterator so argument null-checks throw at
        // call time (instead of being deferred until the caller starts
        // enumerating, which would obscure the real call site).
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(expectedSchema);
        return ReadIteratorAsync(stream, sourcePath, expectedSchema, options, cancellationToken);
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ReadIteratorAsync(
        Stream stream,
        string sourcePath,
        IReadOnlyList<DatasetColumn> expectedSchema,
        IReadOnlyDictionary<string, string>? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var config = BuildConfig(options);
        using var reader = new StreamReader(stream, leaveOpen: true);
        using var csv = new CsvReader(reader, config);

        // Map each expected column to the file's column index by sanitized
        // name. A missing expected column is a hard schema mismatch — the
        // dataset's AQL contract can't be honored without it.
        if (!await csv.ReadAsync())
        {
            // Empty file with no header — only OK if the dataset schema is
            // also empty (zero-column datasets shouldn't exist, but don't
            // throw on a zero-row file).
            if (expectedSchema.Count == 0) yield break;
            throw new DatasetFileSchemaMismatchException(
                sourcePath, expectedSchema, Array.Empty<string>());
        }
        string[] header;
        if (config.HasHeaderRecord)
        {
            if (!csv.ReadHeader() || csv.HeaderRecord is null)
            {
                throw new DatasetFileSchemaMismatchException(
                    sourcePath, expectedSchema, Array.Empty<string>());
            }
            header = csv.HeaderRecord;
        }
        else
        {
            // Synthesize positional headers so the index lookup below works
            // uniformly. The expected schema must use the same names.
            var firstRow = ReadRecord(csv);
            header = new string[firstRow.Length];
            for (var i = 0; i < firstRow.Length; i++) header[i] = $"col_{i + 1}";
            // We've already consumed the first record into firstRow; emit it
            // before continuing the loop.
            yield return CoerceRow(expectedSchema, header, firstRow, sourcePath);
        }

        var sanitized = new string[header.Length];
        for (var i = 0; i < header.Length; i++)
        {
            sanitized[i] = CsvSchemaHeuristics.SanitizeColumnName(header[i], i);
        }
        var indexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < sanitized.Length; i++) indexByName[sanitized[i]] = i;

        foreach (var col in expectedSchema)
        {
            if (!indexByName.ContainsKey(col.Name))
            {
                throw new DatasetFileSchemaMismatchException(
                    sourcePath, expectedSchema, sanitized);
            }
        }

        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = ReadRecord(csv);
            yield return BuildRowFromIndex(expectedSchema, indexByName, record);
        }
    }

    private static CsvConfiguration BuildConfig(IReadOnlyDictionary<string, string>? options)
    {
        var delimiter = ",";
        var hasHeader = true;
        if (options is not null)
        {
            if (options.TryGetValue("delimiter", out var d) && !string.IsNullOrEmpty(d))
            {
                delimiter = d;
            }
            if (options.TryGetValue("hasHeader", out var h))
            {
                hasHeader = !string.Equals(h, "false", StringComparison.OrdinalIgnoreCase);
            }
        }
        return new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = hasHeader,
            Delimiter = delimiter,
            BadDataFound = null,
            MissingFieldFound = null,
        };
    }

    private static string?[] ReadRecord(CsvReader csv)
    {
        var fieldCount = csv.Parser.Count;
        var row = new string?[fieldCount];
        for (var i = 0; i < fieldCount; i++) row[i] = csv.GetField(i);
        return row;
    }

    private static IReadOnlyDictionary<string, object?> CoerceRow(
        IReadOnlyList<DatasetColumn> expected,
        IReadOnlyList<string> header,
        string?[] record,
        string sourcePath)
    {
        var indexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            indexByName[CsvSchemaHeuristics.SanitizeColumnName(header[i], i)] = i;
        }
        foreach (var col in expected)
        {
            if (!indexByName.ContainsKey(col.Name))
            {
                throw new DatasetFileSchemaMismatchException(
                    sourcePath, expected, header);
            }
        }
        return BuildRowFromIndex(expected, indexByName, record);
    }

    private static IReadOnlyDictionary<string, object?> BuildRowFromIndex(
        IReadOnlyList<DatasetColumn> expected,
        IReadOnlyDictionary<string, int> indexByName,
        string?[] record)
    {
        var row = new Dictionary<string, object?>(expected.Count, StringComparer.Ordinal);
        foreach (var col in expected)
        {
            var idx = indexByName[col.Name];
            var raw = idx < record.Length ? record[idx] : null;
            row[col.Name] = CsvSchemaHeuristics.Coerce(raw, col.PostgresType);
        }
        return row;
    }
}
