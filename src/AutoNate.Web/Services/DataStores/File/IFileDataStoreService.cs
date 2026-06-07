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

    // Returns the deleted entity so callers (the audit-event publisher in
    // particular) know what folder/filename was removed without a second
    // round-trip.
    Task<DataStoreFile> DeleteFileAsync(
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

    // Returns the count of file rows deleted so the audit-event publisher
    // can record the blast radius without re-querying.
    Task<int> DeleteFolderAsync(
        Guid datastoreId,
        string folderPath,
        CancellationToken cancellationToken = default);

    // Rename and/or move a single file. At least one of newFolderPath or
    // newFilename must be set. Same folder + same name is a no-op. The on-
    // disk storage key is unchanged (folder/name are metadata-only). Returns
    // the pre-mutation folder/filename alongside the post-mutation entity
    // so the audit-event publisher can record "from where, to where"
    // without a second read.
    Task<(string PreviousFolderPath, string PreviousFilename, DataStoreFile Current)>
        RenameOrMoveFileAsync(
            Guid datastoreId,
            Guid fileId,
            string? newFolderPath,
            string? newFilename,
            Guid actorId,
            CancellationToken cancellationToken = default);

    // Copy a single file's bytes and metadata. newFilename defaults to the
    // source filename. Allocates a fresh fileId + storage key and duplicates
    // bytes on disk so the source and copy are independent. Returns the
    // source and the new copy so the audit event can carry both ids/paths.
    Task<(DataStoreFile Source, DataStoreFile Copy)> CopyFileAsync(
        Guid datastoreId,
        Guid fileId,
        string targetFolderPath,
        string? newFilename,
        Guid actorId,
        CancellationToken cancellationToken = default);

    // Rename and/or move a folder. Rewrites folder_path on every file at
    // or below oldPath. Rejected on root, on moving a folder into its own
    // descendants, and on case-insensitive name collisions in the target.
    Task<int> RenameOrMoveFolderAsync(
        Guid datastoreId,
        string oldPath,
        string newPath,
        Guid actorId,
        CancellationToken cancellationToken = default);

    // Recursive folder copy. Each file under oldPath is duplicated to the
    // matching position under newPath with a fresh fileId and a fresh
    // on-disk copy of its bytes. Returns the number of file rows created.
    Task<int> CopyFolderAsync(
        Guid datastoreId,
        string oldPath,
        string newPath,
        Guid actorId,
        CancellationToken cancellationToken = default);
}
