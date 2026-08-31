using System.IO.Compression;
using System.Text;
using AutoNate.Web.Plugins;
using Xunit;

namespace AutoNate.Web.Tests.Security;

// #63 hardening. The issue's stated exploit — forge a tiny declared size in the
// central directory, sail past the gate, expand to gigabytes — does not work on
// .NET, because ZipArchive truncates each entry stream at the declared size
// (pinned below). What remains, and what PluginZipExtractor adds, is
// enforcement that does not depend on that: the cap is applied to bytes
// actually written, and entry paths are re-checked by the code that creates the
// files rather than trusted from the earlier validation pass.
public sealed class PluginZipExtractorTests : IDisposable
{
    private readonly string _work = Path.Combine(
        Path.GetTempPath(), "autonate-zip-tests-" + Guid.NewGuid().ToString("N"));

    public PluginZipExtractorTests() => Directory.CreateDirectory(_work);

    public void Dispose()
    {
        try { Directory.Delete(_work, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Extracts_a_normal_archive()
    {
        var zip = MakeZip(("plugin.json", "{}"), ("lib/thing.txt", "hello"));
        var dest = Path.Combine(_work, "out");

        PluginZipExtractor.ExtractWithCap(zip, dest, 1024 * 1024);

        Assert.Equal("{}", File.ReadAllText(Path.Combine(dest, "plugin.json")));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(dest, "lib", "thing.txt")));
    }

    // #63 described forging the central directory so a tiny declared size
    // passes the gate while the stream expands to gigabytes. That exploit does
    // not work on .NET: ZipArchive truncates the entry stream at the declared
    // uncompressed size, so understating it yields *fewer* bytes, not more.
    // This test pins that behaviour, because it is the reason the declared-size
    // check is adequate against this particular trick — and it would fail
    // loudly if a future runtime stopped truncating, which is exactly when the
    // byte-counting extractor below becomes load-bearing.
    [Fact]
    public void A_forged_small_declared_size_truncates_rather_than_expanding()
    {
        var payload = new string('\0', 8 * 1024 * 1024);
        var zip = MakeZip(("plugin.json", "{}"), ("big.bin", payload));
        ForgeDeclaredUncompressedSizes(zip, declaredSize: 10);

        var dest = Path.Combine(_work, "out");
        PluginZipExtractor.ExtractWithCap(zip, dest, 1024 * 1024);

        var written = new FileInfo(Path.Combine(dest, "big.bin")).Length;
        Assert.Equal(10, written);
    }

    [Fact]
    public void Refuses_content_larger_than_the_cap_even_when_declared_honestly()
    {
        var zip = MakeZip(("plugin.json", "{}"), ("big.bin", new string('x', 200_000)));
        var dest = Path.Combine(_work, "out");

        Assert.Throws<PluginZipExtractionException>(
            () => PluginZipExtractor.ExtractWithCap(zip, dest, 50_000));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("nested/../../escape.txt")]
    public void Refuses_zip_slip_paths(string entryName)
    {
        var zip = MakeZipRaw((entryName, "pwned"));
        var dest = Path.Combine(_work, "out");

        var ex = Assert.Throws<PluginZipExtractionException>(
            () => PluginZipExtractor.ExtractWithCap(zip, dest, 1024 * 1024));
        Assert.Contains("unsafe path", ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_work, "escape.txt")));
    }

    // ---- helpers ----

    private string MakeZip(params (string Name, string Content)[] entries) => MakeZipRaw(entries);

    private string MakeZipRaw(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(_work, Guid.NewGuid().ToString("N") + ".zip");
        using var fs = File.Create(path);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
            using var stream = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(content);
            stream.Write(bytes, 0, bytes.Length);
        }
        return path;
    }

    // Rewrites the uncompressed-size field of every central-directory record.
    // ZipArchive reports entry.Length from there, so after this the archive
    // lies about how much it expands to — which is precisely the input the old
    // validator accepted.
    private static void ForgeDeclaredUncompressedSizes(string zipPath, uint declaredSize)
    {
        var bytes = File.ReadAllBytes(zipPath);
        ReadOnlySpan<byte> signature = [0x50, 0x4B, 0x01, 0x02]; // PK\x01\x02
        var patched = 0;
        for (var i = 0; i + 28 < bytes.Length; i++)
        {
            if (bytes[i] != signature[0] || bytes[i + 1] != signature[1]
                || bytes[i + 2] != signature[2] || bytes[i + 3] != signature[3])
            {
                continue;
            }
            // Central directory header: uncompressed size is 4 bytes at +24.
            BitConverter.GetBytes(declaredSize).CopyTo(bytes, i + 24);
            patched++;
        }
        Assert.True(patched > 0, "No central-directory headers found to forge.");
        File.WriteAllBytes(zipPath, bytes);
    }
}
