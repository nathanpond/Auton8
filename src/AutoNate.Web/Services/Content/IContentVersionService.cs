using AutoNate.Web.Persistence;

namespace AutoNate.Web.Services.Content;

// Owns version-history bookkeeping for pages and notes. All methods participate
// in the caller's transaction — the version writes and the current-row update
// must commit atomically so a reader never sees a torn state where the page
// body has changed but the version row is missing (or vice versa).
public interface IContentVersionService
{
    // Snapshots the *prior* state of the page into page_versions when the
    // change qualifies as a new editing session, otherwise rolls the autosave
    // into the existing session row and returns null. Returns the new
    // version_number that was written, or null when the autosave was rolled
    // into the previous session (no row written, current_version_number not
    // bumped). Callers should skip the PageVersionCreated audit event when
    // the result is null.
    Task<int?> SnapshotPageBeforeChangeAsync(
        AutoNateDbContext db,
        Guid pageId,
        string priorTitle,
        string priorBodyJsonb,
        string kind,
        string? note,
        Guid actorId,
        DateTime nowUtc,
        CancellationToken ct);

    // Replaces the page's current title/body with the snapshot at the chosen
    // version_number, capturing the current state as a kind='restore' version
    // first. Returns the version_number created for the restore snapshot.
    Task<int> RestorePageAsync(
        AutoNateDbContext db,
        Guid pageId,
        int targetVersionNumber,
        string? note,
        Guid actorId,
        DateTime nowUtc,
        CancellationToken ct);

    // Removes a single non-current page version. Throws InvalidOperationException
    // if the caller asks to delete the current version or the only existing
    // version. Caller handles the deletions_locked check upstream.
    Task DeletePageVersionAsync(
        AutoNateDbContext db,
        Guid pageId,
        int versionNumber,
        CancellationToken ct);

    // Same shape as the Page methods, scoped to a note. Returns null when
    // the autosave was rolled into the previous session row.
    Task<int?> SnapshotNoteBeforeChangeAsync(
        AutoNateDbContext db,
        Guid noteId,
        string? priorTitle,
        string priorNoteKind,
        string priorContentJsonb,
        string kind,
        string? note,
        Guid actorId,
        DateTime nowUtc,
        CancellationToken ct);

    Task<int> RestoreNoteAsync(
        AutoNateDbContext db,
        Guid noteId,
        int targetVersionNumber,
        string? note,
        Guid actorId,
        DateTime nowUtc,
        CancellationToken ct);

    Task DeleteNoteVersionAsync(
        AutoNateDbContext db,
        Guid noteId,
        int versionNumber,
        CancellationToken ct);

    // Document version lifecycle — same shape as the Page methods.
    // Returns null when the autosave was rolled into the previous session
    // row (no new version_number written; current_version_number unchanged).
    Task<int?> SnapshotDocumentBeforeChangeAsync(
        AutoNateDbContext db,
        Guid documentId,
        string priorTitle,
        string priorBodyJsonb,
        string kind,
        string? note,
        Guid actorId,
        DateTime nowUtc,
        CancellationToken ct);

    Task<int> RestoreDocumentAsync(
        AutoNateDbContext db,
        Guid documentId,
        int targetVersionNumber,
        string? note,
        Guid actorId,
        DateTime nowUtc,
        CancellationToken ct);

    Task DeleteDocumentVersionAsync(
        AutoNateDbContext db,
        Guid documentId,
        int versionNumber,
        CancellationToken ct);
}
