using AutoNate.Web.Persistence.Scaffolded;

namespace AutoNate.Web.Services.DataStores.File;

// Folder paths are POSIX-style with a leading "/" and no trailing "/".
// Root = "/". Empty input is normalized to "/". Casing on filenames is
// preserved on disk but compared case-insensitively in the metadata index
// (uq_datastore_files_path uses LOWER(filename)).
public sealed record class FileEntry(
    Guid Id,
    string FolderPath,
    string Filename,
    long SizeBytes,
    string? ContentType,
    DateTime UploadedAtUtc);

public sealed record class FolderEntry(string FolderPath);

public sealed record class FileListing(
    IReadOnlyList<FolderEntry> Folders,
    IReadOnlyList<FileEntry> Files);

public sealed class FileDataStoreNotFoundException(Guid id)
    : Exception($"Data store '{id}' was not found or is not a FileType store.");

public sealed class FileDataStoreFileNotFoundException(Guid datastoreId, Guid fileId)
    : Exception($"File '{fileId}' was not found in data store '{datastoreId}'.");

public sealed class FileDataStoreFilenameConflictException(string filename)
    : Exception($"A file named '{filename}' already exists in this folder.");

public interface IFileDataStoreService
{
    Task<FileListing> ListAsync(
        Guid datastoreId,
        string folderPath,
        CancellationToken cancellationToken = default);

    // Upload streams bytes to disk under DataPaths.DatastoresRoot and writes
    // the metadata row. Conflicts on (datastoreId, folder, filename) surface
    // as FileDataStoreFilenameConflictException.
    Task<DataStoreFile> UploadAsync(
        Guid datastoreId,
        string folderPath,
        string filename,
        string? contentType,
        Stream content,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<(DataStoreFile Metadata, Stream Content)> DownloadAsync(
        Guid datastoreId,
        Guid fileId,
        CancellationToken cancellationToken = default);

    Task DeleteFileAsync(
        Guid datastoreId,
        Guid fileId,
        CancellationToken cancellationToken = default);

    // Folder ops are purely metadata — the disk layout is flat per-file. The
    // SPA needs CreateFolder so empty folders can be shown; deletes cascade
    // to every file at or below the path.
    Task CreateFolderAsync(
        Guid datastoreId,
        string folderPath,
        CancellationToken cancellationToken = default);

    Task DeleteFolderAsync(
        Guid datastoreId,
        string folderPath,
        CancellationToken cancellationToken = default);
}
