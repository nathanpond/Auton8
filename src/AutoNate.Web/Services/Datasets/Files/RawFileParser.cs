using System.Runtime.CompilerServices;
using System.Text;

namespace AutoNate.Web.Services.Datasets.Files;

// Pass-through parser: emits one row per file with a single `content`
// column holding the file's UTF-8 text. Folder scopes therefore yield one
// row per immediate-child file — useful for pipelines that want to handle
// per-file parsing themselves in a downstream transformer (LLM
// summarization, custom format dissection, batch text processing) without
// teaching the dataset layer about each format.
//
// The dataset's locked schema must declare a "content" column (PreviewAsync
// returns exactly that, so authors who click "Preview schema" get a
// matching contract automatically). The PostgresType is allowed to be
// anything text-shaped; the parser writes a string regardless.
public sealed class RawFileParser : IDatasetFileParser
{
    public const string KindName = "raw";
    public const string ContentColumnName = "content";

    public string Kind => KindName;

    public Task<IReadOnlyList<DatasetColumn>> PreviewAsync(
        Stream stream,
        IReadOnlyDictionary<string, string>? options,
        CancellationToken cancellationToken)
    {
        // Schema is constant — every Raw-backed dataset has the same shape
        // regardless of which file the SPA samples for preview. We don't
        // even need to touch the stream.
        IReadOnlyList<DatasetColumn> columns = new[]
        {
            new DatasetColumn(ContentColumnName, "text"),
        };
        return Task.FromResult(columns);
    }

    public IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ReadAsync(
        Stream stream,
        string sourcePath,
        IReadOnlyList<DatasetColumn> expectedSchema,
        IReadOnlyDictionary<string, string>? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(expectedSchema);
        if (expectedSchema.Count == 0
            || !expectedSchema.Any(c => string.Equals(c.Name, ContentColumnName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DatasetFileSchemaMismatchException(
                sourcePath, expectedSchema, new[] { ContentColumnName });
        }
        return ReadIteratorAsync(stream, expectedSchema, cancellationToken);
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ReadIteratorAsync(
        Stream stream,
        IReadOnlyList<DatasetColumn> expectedSchema,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        // Any column besides `content` in the dataset's locked schema gets
        // a null value — the dataset author chose to declare it, but Raw
        // has nothing to populate it with.
        var row = new Dictionary<string, object?>(expectedSchema.Count, StringComparer.Ordinal);
        foreach (var col in expectedSchema)
        {
            row[col.Name] = string.Equals(col.Name, ContentColumnName, StringComparison.OrdinalIgnoreCase)
                ? content
                : null;
        }
        yield return row;
    }
}
