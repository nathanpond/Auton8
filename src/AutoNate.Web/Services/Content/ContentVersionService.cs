using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Content;

public sealed class ContentVersionService : IContentVersionService
{
    private readonly IOptions<ContentVersioningOptions> _options;

    public ContentVersionService(IOptions<ContentVersioningOptions> options)
    {
        _options = options;
    }

    public async Task<int?> SnapshotPageBeforeChangeAsync(
        AutoNateDbContext db, Guid pageId, string priorTitle, string priorBodyJsonb,
        string kind, string? note, Guid actorId, DateTime nowUtc, CancellationToken ct)
    {
        // Session rollup: an autosave PATCH inside an active editing session
        // does NOT write a new row — the previous session-start row stays
        // put (it already captures the pre-session state) and the live page
        // row absorbs the change. A new row is only written when the most
        // recent row is from a different session: different author, different
        // kind (e.g. 'manual' from initial create or 'restore'), or stale by
        // more than ContentVersioning:SessionGapMinutes.
        if (kind == ContentVersionKinds.Autosave)
        {
            var mostRecent = await db.PageVersions.AsNoTracking()
                .Where(v => v.PageId == pageId)
                .OrderByDescending(v => v.VersionNumber)
                .Select(v => new { v.Kind, v.CreatedBy, v.CreatedAtUtc })
                .FirstOrDefaultAsync(ct);
            if (mostRecent != null
                && mostRecent.Kind == ContentVersionKinds.Autosave
                && mostRecent.CreatedBy == actorId
                && (nowUtc - mostRecent.CreatedAtUtc) < _options.Value.SessionGap)
            {
                return null;
            }
        }

        var page = await db.Pages.FirstAsync(p => p.Id == pageId, ct);
        var versionNumber = page.CurrentVersionNumber;
        db.PageVersions.Add(new PageVersion
        {
            Id = Guid.NewGuid(),
            PageId = pageId,
            VersionNumber = versionNumber,
            Title = priorTitle,
            BodyJsonb = priorBodyJsonb,
            Kind = kind,
            Note = note,
            CreatedAtUtc = nowUtc,
            CreatedBy = actorId
        });
        page.CurrentVersionNumber = versionNumber + 1;
        return versionNumber + 1;
    }

    public async Task<int> RestorePageAsync(
        AutoNateDbContext db, Guid pageId, int targetVersionNumber, string? note,
        Guid actorId, DateTime nowUtc, CancellationToken ct)
    {
        var page = await db.Pages.FirstAsync(p => p.Id == pageId, ct);
        var target = await db.PageVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.PageId == pageId && v.VersionNumber == targetVersionNumber, ct);
        if (target is null)
        {
            throw new InvalidOperationException(
                $"Page {pageId} has no version {targetVersionNumber}.");
        }

        // Snapshot current as kind='restore' first so the restore is itself
        // reversible.
        var snapshotNumber = page.CurrentVersionNumber;
        db.PageVersions.Add(new PageVersion
        {
            Id = Guid.NewGuid(),
            PageId = pageId,
            VersionNumber = snapshotNumber,
            Title = page.Title,
            BodyJsonb = page.BodyJsonb,
            Kind = ContentVersionKinds.Restore,
            Note = note,
            CreatedAtUtc = nowUtc,
            CreatedBy = actorId
        });

        page.Title = target.Title;
        page.BodyJsonb = target.BodyJsonb;
        page.CurrentVersionNumber = snapshotNumber + 1;
        page.UpdatedAtUtc = nowUtc;
        page.UpdatedBy = actorId;
        return snapshotNumber;
    }

    public async Task DeletePageVersionAsync(
        AutoNateDbContext db, Guid pageId, int versionNumber, CancellationToken ct)
    {
        var page = await db.Pages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pageId, ct);
        if (page is null) return;

        // current_version_number is the *next* number to be assigned; the
        // highest-numbered existing version is current - 1.
        var currentLive = page.CurrentVersionNumber - 1;
        if (versionNumber == currentLive)
        {
            throw new InvalidOperationException(
                "Cannot delete the current version.");
        }

        var existingCount = await db.PageVersions
            .CountAsync(v => v.PageId == pageId, ct);
        if (existingCount <= 1)
        {
            throw new InvalidOperationException(
                "Cannot delete the only version of a page.");
        }

        var target = await db.PageVersions
            .FirstOrDefaultAsync(v => v.PageId == pageId && v.VersionNumber == versionNumber, ct);
        if (target is null)
        {
            throw new InvalidOperationException(
                $"Page {pageId} has no version {versionNumber}.");
        }
        db.PageVersions.Remove(target);
    }

    public async Task<int?> SnapshotNoteBeforeChangeAsync(
        AutoNateDbContext db, Guid noteId, string? priorTitle, string priorNoteKind,
        string priorContentJsonb, string kind, string? note, Guid actorId, DateTime nowUtc,
        CancellationToken ct)
    {
        // Mirror of the page rollup. See SnapshotPageBeforeChangeAsync above
        // for the design rationale; the criteria and effects are identical.
        if (kind == ContentVersionKinds.Autosave)
        {
            var mostRecent = await db.NoteVersions.AsNoTracking()
                .Where(v => v.NoteId == noteId)
                .OrderByDescending(v => v.VersionNumber)
                .Select(v => new { v.Kind, v.CreatedBy, v.CreatedAtUtc })
                .FirstOrDefaultAsync(ct);
            if (mostRecent != null
                && mostRecent.Kind == ContentVersionKinds.Autosave
                && mostRecent.CreatedBy == actorId
                && (nowUtc - mostRecent.CreatedAtUtc) < _options.Value.SessionGap)
            {
                return null;
            }
        }

        var n = await db.Notes.FirstAsync(x => x.Id == noteId, ct);
        var versionNumber = n.CurrentVersionNumber;
        db.NoteVersions.Add(new NoteVersion
        {
            Id = Guid.NewGuid(),
            NoteId = noteId,
            VersionNumber = versionNumber,
            Title = priorTitle,
            NoteKind = priorNoteKind,
            ContentJsonb = priorContentJsonb,
            Kind = kind,
            Note = note,
            CreatedAtUtc = nowUtc,
            CreatedBy = actorId
        });
        n.CurrentVersionNumber = versionNumber + 1;
        return versionNumber + 1;
    }

    public async Task<int> RestoreNoteAsync(
        AutoNateDbContext db, Guid noteId, int targetVersionNumber, string? note,
        Guid actorId, DateTime nowUtc, CancellationToken ct)
    {
        var n = await db.Notes.FirstAsync(x => x.Id == noteId, ct);
        var target = await db.NoteVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.NoteId == noteId && v.VersionNumber == targetVersionNumber, ct);
        if (target is null)
        {
            throw new InvalidOperationException(
                $"Note {noteId} has no version {targetVersionNumber}.");
        }
        var snapshotNumber = n.CurrentVersionNumber;
        db.NoteVersions.Add(new NoteVersion
        {
            Id = Guid.NewGuid(),
            NoteId = noteId,
            VersionNumber = snapshotNumber,
            Title = n.Title,
            NoteKind = n.NoteKind,
            ContentJsonb = n.ContentJsonb,
            Kind = ContentVersionKinds.Restore,
            Note = note,
            CreatedAtUtc = nowUtc,
            CreatedBy = actorId
        });

        // Note: note_kind is immutable post-create per design D11, so a
        // restore deliberately does not change it. We rely on the create
        // path having enforced that the snapshot has the same kind.
        n.Title = target.Title;
        n.ContentJsonb = target.ContentJsonb;
        n.CurrentVersionNumber = snapshotNumber + 1;
        n.UpdatedAtUtc = nowUtc;
        n.UpdatedBy = actorId;
        return snapshotNumber;
    }

    public async Task DeleteNoteVersionAsync(
        AutoNateDbContext db, Guid noteId, int versionNumber, CancellationToken ct)
    {
        var n = await db.Notes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == noteId, ct);
        if (n is null) return;

        var currentLive = n.CurrentVersionNumber - 1;
        if (versionNumber == currentLive)
        {
            throw new InvalidOperationException(
                "Cannot delete the current version.");
        }

        var existingCount = await db.NoteVersions
            .CountAsync(v => v.NoteId == noteId, ct);
        if (existingCount <= 1)
        {
            throw new InvalidOperationException(
                "Cannot delete the only version of a note.");
        }

        var target = await db.NoteVersions
            .FirstOrDefaultAsync(v => v.NoteId == noteId && v.VersionNumber == versionNumber, ct);
        if (target is null)
        {
            throw new InvalidOperationException(
                $"Note {noteId} has no version {versionNumber}.");
        }
        db.NoteVersions.Remove(target);
    }
}
