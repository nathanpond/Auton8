using System.IO.Compression;
using System.Text;
using AutoNate.Web.Plugins;
using Xunit;

namespace AutoNate.Web.Tests.Plugins;

public sealed class PluginUploadValidatorTests : IDisposable
{
    private readonly string _scratchDir;

    public PluginUploadValidatorTests()
    {
        _scratchDir = Path.Combine(Path.GetTempPath(), "validator-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratchDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_scratchDir)) Directory.Delete(_scratchDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void Validate_HappyPath_ReturnsManifest()
    {
        var path = WriteZip(zip =>
        {
            AddText(zip, "plugin.json", """{"name":"Acme","version":"1.0.0","entryAssembly":"Acme.dll"}""");
            AddText(zip, "Acme.dll", "fake bytes");
        });

        var result = PluginUploadValidator.Validate(path, 1_000_000);

        Assert.True(result.Success);
        Assert.NotNull(result.Manifest);
        Assert.Equal("Acme", result.Manifest!.Name);
        Assert.Equal("Acme.dll", result.Manifest.EntryAssembly);
    }

    [Fact]
    public void Validate_RejectsZipSlip_PathContainsDotDot()
    {
        var path = WriteZip(zip =>
        {
            AddText(zip, "plugin.json", """{"name":"Acme","version":"1.0.0","entryAssembly":"Acme.dll"}""");
            AddText(zip, "../../etc/passwd", "evil");
        });

        var result = PluginUploadValidator.Validate(path, 1_000_000);

        Assert.False(result.Success);
        Assert.Equal("invalid_entry_path", result.ErrorCode);
    }

    [Fact]
    public void Validate_RejectsMissingManifest()
    {
        var path = WriteZip(zip => AddText(zip, "Acme.dll", "fake bytes"));

        var result = PluginUploadValidator.Validate(path, 1_000_000);

        Assert.False(result.Success);
        Assert.Equal("manifest_missing", result.ErrorCode);
    }

    [Fact]
    public void Validate_RejectsManifestWithoutEntryAssembly()
    {
        var path = WriteZip(zip =>
        {
            AddText(zip, "plugin.json", """{"name":"Acme","version":"1.0.0","entryAssembly":""}""");
        });

        var result = PluginUploadValidator.Validate(path, 1_000_000);

        Assert.False(result.Success);
        Assert.Equal("manifest_missing_fields", result.ErrorCode);
    }

    [Fact]
    public void Validate_RejectsMissingEntryAssembly()
    {
        var path = WriteZip(zip =>
        {
            AddText(zip, "plugin.json", """{"name":"Acme","version":"1.0.0","entryAssembly":"Acme.dll"}""");
            // No Acme.dll in the zip.
        });

        var result = PluginUploadValidator.Validate(path, 1_000_000);

        Assert.False(result.Success);
        Assert.Equal("entry_assembly_missing", result.ErrorCode);
    }

    [Fact]
    public void Validate_RejectsOversizeUncompressed()
    {
        var path = WriteZip(zip =>
        {
            AddText(zip, "plugin.json", """{"name":"Acme","version":"1.0.0","entryAssembly":"Acme.dll"}""");
            AddText(zip, "Acme.dll", new string('x', 200));
        });

        var result = PluginUploadValidator.Validate(path, maxUncompressedBytes: 100);

        Assert.False(result.Success);
        Assert.Equal("uncompressed_too_large", result.ErrorCode);
    }

    [Fact]
    public void Validate_RejectsInvalidZip()
    {
        var path = Path.Combine(_scratchDir, "not-a-zip.zip");
        File.WriteAllText(path, "this is not a zip file");

        var result = PluginUploadValidator.Validate(path, 1_000_000);

        Assert.False(result.Success);
        Assert.Equal("invalid_zip", result.ErrorCode);
    }

    private string WriteZip(Action<ZipArchive> populate)
    {
        var path = Path.Combine(_scratchDir, $"test-{Guid.NewGuid():N}.zip");
        using (var fs = File.Create(path))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            populate(zip);
        }
        return path;
    }

    private static void AddText(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
