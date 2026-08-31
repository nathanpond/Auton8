using System.Text;
using AutoNate.Web.Services.Datasets;
using AutoNate.Web.Services.Datasets.Files;
using Xunit;

namespace AutoNate.Web.Tests.Datasets;

// Covers the surface DatasetExecutor + CachedDatasetMaterializer expose
// to users via Virtual+File / Cached+File datasets: schema inference at
// preview time, row streaming at execute time, and strict header
// validation across single-file vs folder-union scopes (the folder case
// reuses ReadAsync per file, so a single-file test of schema-mismatch
// covers the folder-union failure mode too — DatasetFileScopeReader names
// the offending file in the wrapping exception path).
public sealed class CsvFileParserTests
{
    private static Stream Csv(string content) =>
        new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task PreviewAsync_InfersTypes_FromHeaderAndSample()
    {
        var parser = new CsvFileParser();
        await using var stream = Csv("id,name,score\n1,alpha,9.5\n2,beta,3.1\n");

        var columns = await parser.PreviewAsync(stream, options: null, CancellationToken.None);

        Assert.Collection(columns,
            c => { Assert.Equal("id", c.Name); Assert.Equal("bigint", c.PostgresType); },
            c => { Assert.Equal("name", c.Name); Assert.Equal("text", c.PostgresType); },
            c => { Assert.Equal("score", c.Name); Assert.Equal("double precision", c.PostgresType); });
    }

    [Fact]
    public async Task PreviewAsync_SanitizesUnsafeColumnNames()
    {
        // Spaces, punctuation, leading digits all flow through the same
        // SanitizeColumnName the SqlType ingest uses — keeps schema names
        // valid for downstream Cached materialization.
        var parser = new CsvFileParser();
        await using var stream = Csv("Order ID,Item-Name,3rdPartyRef\n1,apple,X\n");

        var columns = await parser.PreviewAsync(stream, options: null, CancellationToken.None);

        Assert.Equal(new[] { "order_id", "item_name", "c_3rdpartyref" },
            columns.Select(c => c.Name).ToArray());
    }

    [Fact]
    public async Task ReadAsync_YieldsTypedRows_KeyedBySchemaColumnNames()
    {
        var parser = new CsvFileParser();
        var schema = new List<DatasetColumn>
        {
            new("id", "bigint"),
            new("name", "text"),
        };
        await using var stream = Csv("id,name\n7,alpha\n42,beta\n");

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in parser.ReadAsync(stream, "/test.csv", schema, options: null, CancellationToken.None))
        {
            rows.Add(row);
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal(7L, rows[0]["id"]);
        Assert.Equal("alpha", rows[0]["name"]);
        Assert.Equal(42L, rows[1]["id"]);
        Assert.Equal("beta", rows[1]["name"]);
    }

    [Fact]
    public async Task ReadAsync_IgnoresExtraColumns_NotInDatasetSchema()
    {
        // A file with extra trailing columns is fine; only the columns
        // declared on the dataset are projected. This lets folder-union
        // tolerate parallel files where someone added a column to a later
        // export but the dataset's locked contract hasn't changed.
        var parser = new CsvFileParser();
        var schema = new List<DatasetColumn> { new("id", "bigint") };
        await using var stream = Csv("id,name,extra\n1,alpha,zzz\n");

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in parser.ReadAsync(stream, "/x.csv", schema, options: null, CancellationToken.None))
        {
            rows.Add(row);
        }

        Assert.Single(rows);
        Assert.Equal(1L, rows[0]["id"]);
        Assert.False(rows[0].ContainsKey("name"));
    }

    [Fact]
    public async Task ReadAsync_ThrowsSchemaMismatch_WhenExpectedColumnIsMissing()
    {
        var parser = new CsvFileParser();
        var schema = new List<DatasetColumn>
        {
            new("id", "bigint"),
            new("missing", "text"),
        };
        await using var stream = Csv("id,name\n1,alpha\n");

        var enumerator = parser.ReadAsync(
            stream, "/folder/bad.csv", schema, options: null, CancellationToken.None)
            .GetAsyncEnumerator();

        var ex = await Assert.ThrowsAsync<DatasetFileSchemaMismatchException>(
            async () => await enumerator.MoveNextAsync());

        Assert.Equal("/folder/bad.csv", ex.SourcePath);
        Assert.Contains("missing", ex.Message);
    }

    [Fact]
    public async Task ReadAsync_HonorsDelimiterOption()
    {
        var parser = new CsvFileParser();
        var schema = new List<DatasetColumn>
        {
            new("a", "text"),
            new("b", "text"),
        };
        await using var stream = Csv("a|b\nleft|right\n");

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in parser.ReadAsync(
            stream,
            "/pipe.csv",
            schema,
            new Dictionary<string, string> { ["delimiter"] = "|" },
            CancellationToken.None))
        {
            rows.Add(row);
        }

        Assert.Single(rows);
        Assert.Equal("left", rows[0]["a"]);
        Assert.Equal("right", rows[0]["b"]);
    }

    [Fact]
    public async Task ReadAsync_HeaderOnlyFile_ReturnsNoRows_WithoutError()
    {
        // The folder-union scope can legitimately have a file with no
        // data rows yet — empty isn't a schema mismatch.
        var parser = new CsvFileParser();
        var schema = new List<DatasetColumn> { new("id", "bigint") };
        await using var stream = Csv("id\n");

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in parser.ReadAsync(stream, "/empty.csv", schema, options: null, CancellationToken.None))
        {
            rows.Add(row);
        }

        Assert.Empty(rows);
    }

    [Fact]
    public void ReadAsync_ValidatesArguments_AtCallSite_NotOnEnumeration()
    {
        // The iterator is intentionally split so null-arg checks throw at
        // the call site (CA1822 + S4456). A typo upstream — passing null
        // for the dataset schema — must blow up where the call lives.
        var parser = new CsvFileParser();
        Assert.Throws<ArgumentNullException>(() =>
            parser.ReadAsync(stream: null!, "/x.csv",
                new List<DatasetColumn>(), null, CancellationToken.None));
    }
}

// Covers the pass-through parser. The contract is intentionally tiny:
// one row per file with the file's UTF-8 text in `content`. Folder-union
// scopes therefore produce one row per file via DatasetFileScopeReader,
// without any per-format knowledge in the dataset layer.
public sealed class RawFileParserTests
{
    private static Stream Bytes(string content) =>
        new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task PreviewAsync_AlwaysReturnsSingleContentColumn()
    {
        var parser = new RawFileParser();
        await using var stream = Bytes("anything at all");

        var columns = await parser.PreviewAsync(stream, options: null, CancellationToken.None);

        var col = Assert.Single(columns);
        Assert.Equal("content", col.Name);
        Assert.Equal("text", col.PostgresType);
    }

    [Fact]
    public async Task ReadAsync_YieldsOneRow_WithFullFileText()
    {
        var parser = new RawFileParser();
        var schema = new List<DatasetColumn> { new("content", "text") };
        const string body = "line one\nline two\n";
        await using var stream = Bytes(body);

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in parser.ReadAsync(stream, "/notes.txt", schema, null, CancellationToken.None))
        {
            rows.Add(row);
        }

        var single = Assert.Single(rows);
        Assert.Equal(body, single["content"]);
    }

    [Fact]
    public async Task ReadAsync_NullsOutExtraSchemaColumns_DatasetAuthorDeclaredButRawCantPopulate()
    {
        // If the dataset's locked schema includes a column we can't fill
        // (an LLM added it, or the author copy-pasted), we still return
        // a row with that key present and null — keeps downstream
        // consumers' iteration shape consistent across files.
        var parser = new RawFileParser();
        var schema = new List<DatasetColumn>
        {
            new("content", "text"),
            new("metadata", "text"),
        };
        await using var stream = Bytes("hello");

        IReadOnlyDictionary<string, object?>? row = null;
        await foreach (var r in parser.ReadAsync(stream, "/x.txt", schema, null, CancellationToken.None))
        {
            row = r;
        }

        Assert.NotNull(row);
        Assert.Equal("hello", row!["content"]);
        Assert.Null(row["metadata"]);
    }

    [Fact]
    public async Task ReadAsync_RejectsSchemaWithoutContentColumn()
    {
        // Refusing here means a misconfigured dataset surfaces at parse
        // time with a clear schema-mismatch error instead of silently
        // returning rows of nulls.
        var parser = new RawFileParser();
        var schema = new List<DatasetColumn> { new("body", "text") };
        await using var stream = Bytes("anything");

        var ex = await Assert.ThrowsAsync<DatasetFileSchemaMismatchException>(async () =>
        {
            await foreach (var _ in parser.ReadAsync(stream, "/bad.txt", schema, null, CancellationToken.None))
            { }
        });
        Assert.Equal("/bad.txt", ex.SourcePath);
    }
}
