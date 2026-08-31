namespace AutoNate.Web.Services.Datasets.Files;

// Pluggable parser for Files-datastore-backed datasets. The dataset's
// scope (single file or single folder) is owned by DatasetExecutor /
// CachedDatasetMaterializer; the parser only knows how to inspect or
// stream one file at a time.
//
// PreviewAsync samples the file at create time so the SPA can populate
// the new dataset's locked column schema. ReadAsync streams every row
// from a file at execute / materialize time and is responsible for
// rejecting files whose header doesn't match the dataset's locked schema
// (by name; types come from the dataset, not the file).
public interface IDatasetFileParser
{
    // Stable identifier persisted in datasets.parser_kind. Compared
    // case-insensitively by DatasetFileParserRegistry.
    string Kind { get; }

    Task<IReadOnlyList<DatasetColumn>> PreviewAsync(
        Stream stream,
        IReadOnlyDictionary<string, string>? options,
        CancellationToken cancellationToken);

    IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ReadAsync(
        Stream stream,
        string sourcePath,
        IReadOnlyList<DatasetColumn> expectedSchema,
        IReadOnlyDictionary<string, string>? options,
        CancellationToken cancellationToken);
}

// Thrown by ReadAsync when a file's header set doesn't match the dataset's
// locked column schema (set equality on column names). The sourcePath
// names the offending file so a folder-scoped dataset reports which file
// broke the union.
public sealed class DatasetFileSchemaMismatchException(
    string sourcePath,
    IReadOnlyList<DatasetColumn> expected,
    IReadOnlyList<string> actualHeader)
    : Exception(
        $"File '{sourcePath}' has columns [{string.Join(", ", actualHeader)}] " +
        $"but dataset schema expects [{string.Join(", ", expected.Select(c => c.Name))}].")
{
    public string SourcePath { get; } = sourcePath;
    public IReadOnlyList<DatasetColumn> Expected { get; } = expected;
    public IReadOnlyList<string> ActualHeader { get; } = actualHeader;
}
