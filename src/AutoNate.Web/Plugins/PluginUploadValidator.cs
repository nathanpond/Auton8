using System.IO.Compression;
using System.Text.Json;

namespace AutoNate.Web.Plugins;

public sealed record PluginUploadValidationResult(
    bool Success,
    PluginManifest? Manifest,
    string? ErrorCode,
    string? ErrorMessage);

// Validates a plugin .zip on disk. Cheap inspection only — does not load any
// assembly. Loading happens later, on enable.
public static class PluginUploadValidator
{
    public const string ManifestFileName = "plugin.json";

    public static PluginUploadValidationResult Validate(string zipPath, long maxUncompressedBytes)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);

            // Zip-slip / oversize / rooted-path checks happen first because
            // we'll trust everything else once they pass.
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.FullName)) continue;
                var normalized = entry.FullName.Replace('\\', '/');
                if (Path.IsPathRooted(normalized) || normalized.Contains("..", StringComparison.Ordinal))
                {
                    return new(false, null, "invalid_entry_path",
                        $"Zip entry '{entry.FullName}' has an unsafe path.");
                }

                // entry.Length is the central-directory size field, which the
                // uploader controls independently of the deflate stream — a
                // crafted archive can declare tiny entries and still expand to
                // gigabytes. So this is only a cheap early rejection of the
                // honest oversize case; the real cap is enforced on the bytes
                // actually written, by PluginZipExtractor (#63).
                total += entry.Length;
                if (total > maxUncompressedBytes)
                {
                    return new(false, null, "uncompressed_too_large",
                        $"Uncompressed size exceeds the limit of {maxUncompressedBytes} bytes.");
                }
            }

            var manifestEntry = archive.GetEntry(ManifestFileName);
            if (manifestEntry is null)
            {
                return new(false, null, "manifest_missing",
                    $"Zip is missing {ManifestFileName} at root.");
            }

            PluginManifest? manifest;
            try
            {
                using var stream = manifestEntry.Open();
                manifest = JsonSerializer.Deserialize<PluginManifest>(stream, ManifestJsonOptions);
            }
            catch (JsonException ex)
            {
                return new(false, null, "manifest_invalid_json", $"plugin.json is not valid JSON: {ex.Message}");
            }

            if (manifest is null
                || string.IsNullOrWhiteSpace(manifest.Name)
                || string.IsNullOrWhiteSpace(manifest.Version)
                || string.IsNullOrWhiteSpace(manifest.EntryAssembly))
            {
                return new(false, null, "manifest_missing_fields",
                    "plugin.json must contain non-empty name, version, and entryAssembly.");
            }

            // Look for the entry assembly anywhere in the zip — most plugins
            // have it at root, but a build that places it under a subfolder
            // is also valid as long as the loader can later find it.
            var entryAssembly = manifest.EntryAssembly;
            var entryAssemblyExists = false;
            foreach (var e in archive.Entries)
            {
                var name = Path.GetFileName(e.FullName);
                if (string.Equals(name, entryAssembly, StringComparison.OrdinalIgnoreCase))
                {
                    entryAssemblyExists = true;
                    break;
                }
            }
            if (!entryAssemblyExists)
            {
                return new(false, null, "entry_assembly_missing",
                    $"Entry assembly '{entryAssembly}' is not present in the zip.");
            }

            return new(true, manifest, null, null);
        }
        catch (InvalidDataException)
        {
            return new(false, null, "invalid_zip", "Uploaded file is not a valid zip archive.");
        }
    }

    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web);
}
