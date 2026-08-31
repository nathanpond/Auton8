using System.IO.Compression;

namespace AutoNate.Web.Plugins;

/// <summary>
/// Extracts a plugin archive with a cap on the bytes actually written (#63).
/// </summary>
/// <remarks>
/// <see cref="PluginUploadValidator"/> sums <c>ZipArchiveEntry.Length</c>, which
/// comes from the zip's central directory and is therefore uploader-controlled.
/// #63 proposed forging it small to slip a bomb past that gate; measured, that
/// does not work on .NET, because <see cref="ZipArchive"/> truncates each entry
/// stream at the declared size — understating it yields fewer bytes, not more
/// (pinned by PluginZipExtractorTests).
///
/// This exists so the guarantee does not rest on that runtime detail. The cap
/// is applied to the bytes actually written, so an archive is bounded by what
/// it delivers rather than by what it claims; and entry paths are re-checked
/// here, in the code that creates files, rather than trusted from the earlier
/// validation pass.
/// </remarks>
public static class PluginZipExtractor
{
    public static void ExtractWithCap(string zipPath, string destinationFolder, long maxUncompressedBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolder);
        if (maxUncompressedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxUncompressedBytes));
        }

        Directory.CreateDirectory(destinationFolder);
        var root = Path.GetFullPath(destinationFolder);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);
        long written = 0;

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.FullName)) continue;

            var normalized = entry.FullName.Replace('\\', '/');
            if (Path.IsPathRooted(normalized) || normalized.Split('/').Contains(".."))
            {
                throw new PluginZipExtractionException(
                    $"Zip entry '{entry.FullName}' has an unsafe path.");
            }

            var target = Path.GetFullPath(Path.Combine(root, normalized));
            if (!target.StartsWith(rootWithSeparator, StringComparison.Ordinal))
            {
                throw new PluginZipExtractionException(
                    $"Zip entry '{entry.FullName}' resolves outside the plugin folder.");
            }

            // Directory entries carry a trailing slash and no content.
            if (normalized.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            using var source = entry.Open();
            using var destination = File.Create(target);
            written += CopyCapped(source, destination, maxUncompressedBytes - written, maxUncompressedBytes);
        }
    }

    private static long CopyCapped(Stream source, Stream destination, long remaining, long cap)
    {
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (read > remaining)
            {
                // Write nothing further: the caller deletes the folder, and
                // stopping here bounds the damage to one buffer past the cap.
                throw new PluginZipExtractionException(
                    $"Uncompressed size exceeds the limit of {cap} bytes.");
            }
            destination.Write(buffer, 0, read);
            remaining -= read;
            copied += read;
        }
        return copied;
    }
}

public sealed class PluginZipExtractionException : Exception
{
    public PluginZipExtractionException(string message) : base(message) { }
}
