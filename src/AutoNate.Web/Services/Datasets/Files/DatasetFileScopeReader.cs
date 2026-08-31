using System.Runtime.CompilerServices;
using System.Text.Json;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.DataStores.File;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Datasets.Files;

// Resolves a Files-backed dataset's scope to a sequence of parsed rows.
// Owns the per-file Stream lifetime so callers (DatasetExecutor, the
// CachedDatasetMaterializer's file branch) only see DatasetColumn-shaped
// row dictionaries.
//
// Scope semantics:
//   FileScopeKind = "file"   → exactly one file at FileScopePath.
//   FileScopeKind = "folder" → every immediate-child file under
//                              FileScopePath (non-recursive, ".keep"
//                              excluded). Each file's header must match
//                              the dataset's locked column schema; the
//                              first mismatched file aborts the read so
//                              the caller sees a clean
//                              DatasetFileSchemaMismatchException naming
//                              the offending file.
//
// Per-query I/O cost is linear in the scope's file bytes, so Cached mode
// is the right default for any folder scope of meaningful size — Virtual
// re-reads every query.
public sealed class DatasetFileScopeReader(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    IFileDataStoreService fileService,
    DatasetFileParserRegistry parserRegistry)
{
    public const string ScopeFile = "file";
    public const string ScopeFolder = "folder";

    public IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ReadRowsAsync(
        Dataset dataset,
        CancellationToken cancellationToken)
    {
        // Eagerly validate the dataset's Files-scope so misconfiguration
        // surfaces at the call site instead of on first MoveNextAsync.
        ArgumentNullException.ThrowIfNull(dataset);
        if (string.IsNullOrWhiteSpace(dataset.ParserKind))
        {
            throw new InvalidOperationException(
                $"Dataset '{dataset.Name}' is Files-backed but has no parser_kind set.");
        }
        if (string.IsNullOrWhiteSpace(dataset.FileScopeKind) ||
            string.IsNullOrWhiteSpace(dataset.FileScopePath))
        {
            throw new InvalidOperationException(
                $"Dataset '{dataset.Name}' is Files-backed but has no file scope set.");
        }
        var schema = DatasetSchemaCodec.Decode(dataset.ColumnSchemaJson);
        if (schema.Count == 0)
        {
            throw new InvalidOperationException(
                $"Dataset '{dataset.Name}' has no column schema; cannot stream rows.");
        }
        var parser = parserRegistry.Get(dataset.ParserKind);
        var options = DecodeOptions(dataset.ParserOptionsJson);
        return ReadIteratorAsync(dataset, schema, parser, options, cancellationToken);
    }

    private async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ReadIteratorAsync(
        Dataset dataset,
        IReadOnlyList<DatasetColumn> schema,
        IDatasetFileParser parser,
        IReadOnlyDictionary<string, string> options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var files = await ResolveFilesAsync(dataset, cancellationToken);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (_, stream) = await fileService.DownloadAsync(
                dataset.SourceId, file.Id, cancellationToken);
            await using (stream)
            {
                var path = JoinPath(file.FolderPath, file.Filename);
                await foreach (var row in parser.ReadAsync(
                    stream, path, schema, options, cancellationToken))
                {
                    yield return row;
                }
            }
        }
    }

    private async Task<IReadOnlyList<DataStoreFile>> ResolveFilesAsync(
        Dataset dataset, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (string.Equals(dataset.FileScopeKind, ScopeFile, StringComparison.OrdinalIgnoreCase))
        {
            var (folder, filename) = SplitFilePath(dataset.FileScopePath!);
            // Filename uniqueness in (datastore, folder) is enforced case-
            // insensitively (uq_datastore_files_path), so a case-insensitive
            // match here is consistent with how the file was originally
            // uploaded.
#pragma warning disable CA1304, CA1311 // PG uses the same case folding.
            var entity = await db.DataStoreFiles.AsNoTracking()
                .SingleOrDefaultAsync(
                    f => f.DataStoreId == dataset.SourceId
                         && f.FolderPath == folder
                         && f.Filename.ToLower() == filename.ToLower(),
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Dataset '{dataset.Name}' references file '{dataset.FileScopePath}', " +
                    "which does not exist in the underlying data store.");
#pragma warning restore CA1304, CA1311
            return new[] { entity };
        }
        if (string.Equals(dataset.FileScopeKind, ScopeFolder, StringComparison.OrdinalIgnoreCase))
        {
            var folder = NormalizeFolder(dataset.FileScopePath!);
            var entries = await db.DataStoreFiles.AsNoTracking()
                .Where(f => f.DataStoreId == dataset.SourceId
                            && f.FolderPath == folder
                            && f.Filename != ".keep")
                .OrderBy(f => f.Filename)
                .ToListAsync(cancellationToken);
            return entries;
        }
        throw new InvalidOperationException(
            $"Dataset '{dataset.Name}' has unknown file scope kind '{dataset.FileScopeKind}'.");
    }

    private static IReadOnlyDictionary<string, string> DecodeOptions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            // A corrupted options blob shouldn't crash the read — fall
            // through with defaults. The dataset's locked column schema is
            // still authoritative.
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static (string Folder, string Filename) SplitFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("File scope path is empty.");
        var normalized = path.StartsWith('/') ? path : "/" + path;
        var lastSlash = normalized.LastIndexOf('/');
        var folder = lastSlash == 0 ? "/" : normalized[..lastSlash];
        var filename = normalized[(lastSlash + 1)..];
        if (filename.Length == 0)
            throw new InvalidOperationException(
                $"File scope path '{path}' has no filename segment.");
        return (folder, filename);
    }

    private static string NormalizeFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "/";
        var v = folder.StartsWith('/') ? folder : "/" + folder;
        if (v.Length > 1 && v.EndsWith('/')) v = v[..^1];
        return v;
    }

    private static string JoinPath(string folder, string filename)
    {
        return folder.EndsWith('/') ? folder + filename : folder + "/" + filename;
    }
}
