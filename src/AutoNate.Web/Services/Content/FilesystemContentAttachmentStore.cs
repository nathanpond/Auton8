using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Content;

public sealed class FilesystemContentAttachmentStore : IContentAttachmentStore
{
    private readonly IOptions<ContentAttachmentOptions> _options;
    private readonly ILogger<FilesystemContentAttachmentStore> _log;

    public FilesystemContentAttachmentStore(
        IOptions<ContentAttachmentOptions> options,
        ILogger<FilesystemContentAttachmentStore> log)
    {
        _options = options;
        _log = log;
    }

    public async Task<string> WriteAsync(
        Guid projectId, Guid attachmentId, Stream content, CancellationToken ct)
    {
        // storage_key is the relative path; the absolute root lives in
        // configuration so backups / migrations can be done at the directory
        // level without touching DB rows.
        var relative = Path.Combine(projectId.ToString("N"), attachmentId.ToString("N"));
        var absolute = ResolveAbsolutePath(relative);
        var directory = Path.GetDirectoryName(absolute);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await using var file = File.Create(absolute);
        await content.CopyToAsync(file, ct);
        return relative;
    }

    public Task<Stream> ReadAsync(string storageKey, CancellationToken ct)
    {
        var absolute = ResolveAbsolutePath(storageKey);
        Stream stream = File.OpenRead(absolute);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        var absolute = ResolveAbsolutePath(storageKey);
        try
        {
            if (File.Exists(absolute))
            {
                File.Delete(absolute);
            }
        }
        catch (IOException ex)
        {
            // Bytes orphan rather than block the DB delete. The DB row is
            // already gone by the time we reach here; logging is enough.
            _log.LogWarning(ex,
                "Failed to delete attachment bytes at {Path}; orphaning.",
                absolute);
        }
        return Task.CompletedTask;
    }

    private string ResolveAbsolutePath(string storageKey)
    {
        var root = _options.Value.RootPath;
        var combined = Path.Combine(root, storageKey);
        // Pin to an absolute path so relative roots (test scenarios) still
        // produce a stable filesystem location.
        return Path.IsPathRooted(combined) ? combined : Path.GetFullPath(combined);
    }
}
