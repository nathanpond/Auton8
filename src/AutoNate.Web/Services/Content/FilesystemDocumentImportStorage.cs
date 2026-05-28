using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Content;

public sealed class FilesystemDocumentImportStorage : IDocumentImportStorage
{
    private readonly IOptions<DocumentImportOptions> _options;
    private readonly ILogger<FilesystemDocumentImportStorage> _log;

    public FilesystemDocumentImportStorage(
        IOptions<DocumentImportOptions> options,
        ILogger<FilesystemDocumentImportStorage> log)
    {
        _options = options;
        _log = log;
    }

    public async Task<string> WriteAsync(Guid documentId, Stream content, CancellationToken ct)
    {
        var absolute = ResolveAbsolutePath(documentId);
        var directory = Path.GetDirectoryName(absolute);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await using var file = File.Create(absolute);
        await content.CopyToAsync(file, ct);
        return absolute;
    }

    public Task<Stream> ReadAsync(Guid documentId, CancellationToken ct)
    {
        var absolute = ResolveAbsolutePath(documentId);
        Stream stream = File.OpenRead(absolute);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(Guid documentId, CancellationToken ct)
    {
        var absolute = ResolveAbsolutePath(documentId);
        try
        {
            if (File.Exists(absolute))
            {
                File.Delete(absolute);
            }
        }
        catch (IOException ex)
        {
            // Same rationale as FilesystemContentAttachmentStore: the Document
            // row is the durable record, the stash is transient. Log + orphan
            // rather than fail the cleanup endpoint.
            _log.LogWarning(ex,
                "Failed to delete document import stash at {Path}; orphaning.",
                absolute);
        }
        return Task.CompletedTask;
    }

    public bool Exists(Guid documentId)
    {
        return File.Exists(ResolveAbsolutePath(documentId));
    }

    private string ResolveAbsolutePath(Guid documentId)
    {
        // Filenames are Guid.ToString("N") (no dashes) — there is no
        // attacker-controlled component in the path, so traversal is
        // structurally impossible. We pin to the configured root and
        // use the extension `.docx` for both `.docx` and `.dotx`
        // uploads because the editor parses either OOXML container
        // identically; the `kind` discriminator lives on the Document
        // row, not the file name.
        var root = _options.Value.RootPath;
        var combined = Path.Combine(root, documentId.ToString("N") + ".docx");
        return Path.IsPathRooted(combined) ? combined : Path.GetFullPath(combined);
    }
}
