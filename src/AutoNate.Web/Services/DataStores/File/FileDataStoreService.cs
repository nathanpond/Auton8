using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AutoNate.Web.Services.DataStores.File;

// FileType data store operations. File bytes live under
// DataPaths.DatastoresRoot/{datastoreId:N}/{fileId:N} (flat layout —
// folder structure is metadata-only). Metadata in datastore_files; the
// unique index uq_datastore_files_path enforces (datastore, folder,
// LOWER(filename)) so re-uploading the same name to the same folder is
// a 409 unless caller chooses to delete first.
public sealed class FileDataStoreService(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    IDataPaths dataPaths,
    ILogger<FileDataStoreService> log) : IFileDataStoreService
{
    private const string PgUniqueViolation = "23505";

    public async Task<FileListing> ListAsync(
        Guid datastoreId, string folderPath, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeFolderPath(folderPath);
        await EnsureFileTypeAsync(datastoreId, cancellationToken);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        // Files in this folder.
        var files = await db.DataStoreFiles.AsNoTracking()
            .Where(f => f.DataStoreId == datastoreId && f.FolderPath == normalized)
            .OrderBy(f => f.Filename)
            .ToListAsync(cancellationToken);

        // Immediate subfolders: any folder_path strictly under `normalized`
        // whose next path segment we extract as the displayed child folder.
        var prefix = normalized == "/" ? "/" : normalized + "/";
        var deeperFolders = await db.DataStoreFiles.AsNoTracking()
            .Where(f => f.DataStoreId == datastoreId
                        && f.FolderPath != normalized
                        && f.FolderPath.StartsWith(prefix))
            .Select(f => f.FolderPath)
            .Distinct()
            .ToListAsync(cancellationToken);

        var childFolders = deeperFolders
            .Select(p => ExtractImmediateChildFolder(p, prefix))
            .Where(p => p is not null)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new FolderEntry(p!))
            .ToList();

        var fileEntries = files
            .Select(f => new FileEntry(
                f.Id, f.FolderPath, f.Filename, f.SizeBytes, f.ContentType, f.UploadedAtUtc))
            .ToList();

        return new FileListing(childFolders, fileEntries);
    }

    public async Task<DataStoreFile> UploadAsync(
        Guid datastoreId,
        string folderPath,
        string filename,
        string? contentType,
        Stream content,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var normalized = NormalizeFolderPath(folderPath);
        var sanitized = SanitizeFilename(filename);
        await EnsureFileTypeAsync(datastoreId, cancellationToken);

        var fileId = Guid.NewGuid();
        var (absolutePath, storageKey) = ResolveStoragePath(datastoreId, fileId);
        var directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        long sizeBytes;
        await using (var file = System.IO.File.Create(absolutePath))
        {
            await content.CopyToAsync(file, cancellationToken);
            sizeBytes = file.Length;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = new DataStoreFile
        {
            Id = fileId,
            DataStoreId = datastoreId,
            FolderPath = normalized,
            Filename = sanitized,
            StorageKey = storageKey,
            SizeBytes = sizeBytes,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? null : contentType,
            UploadedBy = actorId,
            UploadedAtUtc = DateTime.UtcNow
        };
        db.DataStoreFiles.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PgUniqueViolation)
        {
            // Roll back the bytes — the metadata row never landed.
            TryDeleteOrphan(absolutePath);
            throw new FileDataStoreFilenameConflictException(sanitized);
        }
        return entity;
    }

    public async Task<(DataStoreFile Metadata, Stream Content)> DownloadAsync(
        Guid datastoreId, Guid fileId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.DataStoreFiles.AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == fileId && f.DataStoreId == datastoreId, cancellationToken)
            ?? throw new FileDataStoreFileNotFoundException(datastoreId, fileId);
        var absolutePath = ResolveAbsolutePath(entity.StorageKey);
        Stream stream = System.IO.File.OpenRead(absolutePath);
        return (entity, stream);
    }

    public async Task DeleteFileAsync(
        Guid datastoreId, Guid fileId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.DataStoreFiles
            .SingleOrDefaultAsync(f => f.Id == fileId && f.DataStoreId == datastoreId, cancellationToken)
            ?? throw new FileDataStoreFileNotFoundException(datastoreId, fileId);
        db.DataStoreFiles.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        TryDeleteOrphan(ResolveAbsolutePath(entity.StorageKey));
    }

    public async Task CreateFolderAsync(
        Guid datastoreId, string folderPath, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeFolderPath(folderPath);
        if (normalized == "/") return;
        await EnsureFileTypeAsync(datastoreId, cancellationToken);

        // Folders are metadata-only. We synthesize an empty marker by inserting
        // a placeholder file with a sentinel filename ".keep" when the folder
        // is otherwise empty. The listing query collapses these into folder
        // entries via the prefix scan above.
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var exists = await db.DataStoreFiles.AsNoTracking()
            .AnyAsync(
                f => f.DataStoreId == datastoreId
                     && (f.FolderPath == normalized
                         || f.FolderPath.StartsWith(normalized + "/")),
                cancellationToken);
        if (exists) return;

        var placeholder = new DataStoreFile
        {
            Id = Guid.NewGuid(),
            DataStoreId = datastoreId,
            FolderPath = normalized,
            Filename = ".keep",
            StorageKey = string.Empty,
            SizeBytes = 0,
            ContentType = null,
            UploadedBy = Guid.Empty,
            UploadedAtUtc = DateTime.UtcNow
        };
        db.DataStoreFiles.Add(placeholder);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PgUniqueViolation)
        {
            // Concurrent creation — the folder exists now, which is the
            // desired post-condition, so swallow.
            log.LogDebug(ex, "CreateFolder lost a race; folder already exists.");
        }
    }

    public async Task DeleteFolderAsync(
        Guid datastoreId, string folderPath, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeFolderPath(folderPath);
        if (normalized == "/")
            throw new InvalidOperationException("Cannot delete the root folder.");
        await EnsureFileTypeAsync(datastoreId, cancellationToken);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var doomed = await db.DataStoreFiles
            .Where(f => f.DataStoreId == datastoreId
                        && (f.FolderPath == normalized
                            || f.FolderPath.StartsWith(normalized + "/")))
            .ToListAsync(cancellationToken);
        if (doomed.Count == 0) return;

        var storageKeys = doomed
            .Where(f => !string.IsNullOrEmpty(f.StorageKey))
            .Select(f => f.StorageKey)
            .ToList();
        db.DataStoreFiles.RemoveRange(doomed);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var key in storageKeys)
        {
            TryDeleteOrphan(ResolveAbsolutePath(key));
        }
    }

    private async Task EnsureFileTypeAsync(Guid datastoreId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var kind = await db.DataStores.AsNoTracking()
            .Where(d => d.Id == datastoreId)
            .Select(d => (short?)d.Kind)
            .SingleOrDefaultAsync(cancellationToken);
        if (kind is null || (DataStoreKind)kind.Value != DataStoreKind.FileType)
        {
            throw new FileDataStoreNotFoundException(datastoreId);
        }
    }

    private (string AbsolutePath, string StorageKey) ResolveStoragePath(Guid datastoreId, Guid fileId)
    {
        var relative = Path.Combine(datastoreId.ToString("N"), fileId.ToString("N"));
        var absolute = ResolveAbsolutePath(relative);
        return (absolute, relative);
    }

    private string ResolveAbsolutePath(string storageKey)
    {
        var combined = Path.Combine(dataPaths.DatastoresRoot, storageKey);
        return Path.IsPathRooted(combined) ? combined : Path.GetFullPath(combined);
    }

    private void TryDeleteOrphan(string absolutePath)
    {
        try
        {
            if (System.IO.File.Exists(absolutePath))
            {
                System.IO.File.Delete(absolutePath);
            }
        }
        catch (IOException ex)
        {
            log.LogWarning(ex, "Failed to delete datastore-file bytes at {Path}; orphaning.", absolutePath);
        }
    }

    private static string NormalizeFolderPath(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return "/";
        var trimmed = folderPath.Trim();
        if (!trimmed.StartsWith('/')) trimmed = "/" + trimmed;
        // Collapse trailing slashes (except root).
        while (trimmed.Length > 1 && trimmed.EndsWith('/'))
        {
            trimmed = trimmed[..^1];
        }
        // Reject path-traversal attempts.
        if (trimmed.Contains("..", StringComparison.Ordinal) || trimmed.Contains("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid folder path.", nameof(folderPath));
        }
        return trimmed;
    }

    private static string SanitizeFilename(string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            throw new ArgumentException("Filename is required.", nameof(filename));
        var trimmed = filename.Trim();
        if (trimmed.Contains('/', StringComparison.Ordinal)
            || trimmed.Contains('\\', StringComparison.Ordinal)
            || trimmed == "." || trimmed == "..")
        {
            throw new ArgumentException("Filename cannot contain path separators.", nameof(filename));
        }
        // Strip control chars and disallowed FS chars conservatively.
        var invalid = Path.GetInvalidFileNameChars();
        if (trimmed.IndexOfAny(invalid) >= 0)
        {
            throw new ArgumentException("Filename contains invalid characters.", nameof(filename));
        }
        return trimmed;
    }

    private static string? ExtractImmediateChildFolder(string fullPath, string prefix)
    {
        if (!fullPath.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var remainder = fullPath[prefix.Length..];
        if (remainder.Length == 0) return null;
        var slash = remainder.IndexOf('/', StringComparison.Ordinal);
        var first = slash < 0 ? remainder : remainder[..slash];
        // prefix already has the leading "/" and trailing "/" (root case: "/")
        var rebuilt = prefix == "/" ? "/" + first : prefix + first;
        return rebuilt;
    }
}
