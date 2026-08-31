using AutoNate.Web.Models.Forms;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using FormEntity = AutoNate.Web.Persistence.Scaffolded.Form;
using FormVersionEntity = AutoNate.Web.Persistence.Scaffolded.FormVersion;

namespace AutoNate.Web.Services.Forms;

public sealed class EfCoreFormStore(IDbContextFactory<AutoNateDbContext> dbContextFactory) : IFormStore
{
    public async Task<IReadOnlyList<FormSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Forms
            .AsNoTracking()
            .OrderByDescending(f => f.UpdatedAtUtc)
            .ThenBy(f => f.Name)
            .ToListAsync(cancellationToken);

        return rows.Select(ToSummary).ToList();
    }

    public async Task<Form?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Forms
            .AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        return entity is null ? null : ToModel(entity);
    }

    public async Task<Form?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeShortCode(shortCode);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Forms
            .AsNoTracking()
            .SingleOrDefaultAsync(f => f.ShortCode == normalized, cancellationToken);
        return entity is null ? null : ToModel(entity);
    }

    public async Task<Form> CreateAsync(CreateFormRequest request, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = NormalizeName(request.Name);
        var shortCode = NormalizeShortCode(request.ShortCode);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Form name is required.");
        }
        if (string.IsNullOrWhiteSpace(shortCode))
        {
            throw new InvalidOperationException("Form short code is required.");
        }

        var formCode = request.FormCode ?? DefaultFormCode;
        var siteAvailable = request.SiteAvailable ?? false;
        var now = DateTimeOffset.UtcNow;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await db.Forms.AnyAsync(f => f.ShortCode == shortCode, cancellationToken);
        if (existing)
        {
            throw new InvalidOperationException($"A form with short code '{shortCode}' already exists.");
        }

        var entity = new FormEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            ShortCode = shortCode,
            FormCode = formCode,
            SiteAvailable = siteAvailable,
            IsDraft = true,
            DraftVersionNumber = 1,
            PublishedVersionNumber = null,
            CreatedAtUtc = now.UtcDateTime,
            CreatedBy = actorId,
            UpdatedAtUtc = now.UtcDateTime,
            UpdatedBy = actorId
        };

        db.Forms.Add(entity);
        db.FormVersions.Add(new FormVersionEntity
        {
            Id = Guid.NewGuid(),
            FormId = entity.Id,
            VersionNumber = 1,
            Name = entity.Name,
            ShortCode = entity.ShortCode,
            FormCode = entity.FormCode,
            SiteAvailable = entity.SiteAvailable,
            Kind = FormVersionKinds.Save,
            Note = null,
            CreatedAtUtc = now.UtcDateTime,
            CreatedBy = actorId
        });

        await db.SaveChangesAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task<Form> SaveAsync(Guid id, SaveFormRequest request, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = NormalizeName(request.Name);
        var shortCode = NormalizeShortCode(request.ShortCode);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Form name is required.");
        }
        if (string.IsNullOrWhiteSpace(shortCode))
        {
            throw new InvalidOperationException("Form short code is required.");
        }

        var now = DateTimeOffset.UtcNow;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Forms.SingleOrDefaultAsync(f => f.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Form {id} not found.");

        var collision = await db.Forms.AnyAsync(f => f.ShortCode == shortCode && f.Id != id, cancellationToken);
        if (collision)
        {
            throw new InvalidOperationException($"A form with short code '{shortCode}' already exists.");
        }

        var nextVersion = entity.DraftVersionNumber + 1;

        entity.Name = name;
        entity.ShortCode = shortCode;
        entity.FormCode = request.FormCode;
        entity.SiteAvailable = request.SiteAvailable;
        entity.DraftVersionNumber = nextVersion;
        entity.IsDraft = entity.PublishedVersionNumber != nextVersion;
        entity.UpdatedAtUtc = now.UtcDateTime;
        entity.UpdatedBy = actorId;

        db.FormVersions.Add(new FormVersionEntity
        {
            Id = Guid.NewGuid(),
            FormId = entity.Id,
            VersionNumber = nextVersion,
            Name = entity.Name,
            ShortCode = entity.ShortCode,
            FormCode = entity.FormCode,
            SiteAvailable = entity.SiteAvailable,
            Kind = FormVersionKinds.Save,
            Note = null,
            CreatedAtUtc = now.UtcDateTime,
            CreatedBy = actorId
        });

        await db.SaveChangesAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Forms.SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (entity is null) return false;

        db.Forms.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Form> PublishAsync(Guid id, Guid actorId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Forms.SingleOrDefaultAsync(f => f.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Form {id} not found.");

        // Publishing snapshots the current draft as a `publish` version row,
        // then advances draft_version_number so subsequent saves don't clash
        // with the published number. Mirroring the workflow_models model
        // (publish anchored to a specific version_number).
        var publishVersion = entity.DraftVersionNumber + 1;

        db.FormVersions.Add(new FormVersionEntity
        {
            Id = Guid.NewGuid(),
            FormId = entity.Id,
            VersionNumber = publishVersion,
            Name = entity.Name,
            ShortCode = entity.ShortCode,
            FormCode = entity.FormCode,
            SiteAvailable = entity.SiteAvailable,
            Kind = FormVersionKinds.Publish,
            Note = null,
            CreatedAtUtc = now.UtcDateTime,
            CreatedBy = actorId
        });

        entity.DraftVersionNumber = publishVersion;
        entity.PublishedVersionNumber = publishVersion;
        entity.IsDraft = false;
        entity.UpdatedAtUtc = now.UtcDateTime;
        entity.UpdatedBy = actorId;

        await db.SaveChangesAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task<IReadOnlyList<FormVersion>> ListVersionsAsync(Guid formId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.FormVersions
            .AsNoTracking()
            .Where(v => v.FormId == formId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(cancellationToken);
        return rows.Select(ToVersionModel).ToList();
    }


    public async Task<Form?> RestoreAsync(Guid id, int versionNumber, Guid actorId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Forms.SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (entity is null) return null;

        var source = await db.FormVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(v => v.FormId == id && v.VersionNumber == versionNumber, cancellationToken);
        if (source is null) return null;

        var nextVersion = entity.DraftVersionNumber + 1;

        entity.Name = source.Name;
        entity.ShortCode = source.ShortCode;
        entity.FormCode = source.FormCode;
        entity.SiteAvailable = source.SiteAvailable;
        entity.DraftVersionNumber = nextVersion;
        entity.IsDraft = entity.PublishedVersionNumber != nextVersion;
        entity.UpdatedAtUtc = now.UtcDateTime;
        entity.UpdatedBy = actorId;

        db.FormVersions.Add(new FormVersionEntity
        {
            Id = Guid.NewGuid(),
            FormId = entity.Id,
            VersionNumber = nextVersion,
            Name = entity.Name,
            ShortCode = entity.ShortCode,
            FormCode = entity.FormCode,
            SiteAvailable = entity.SiteAvailable,
            Kind = FormVersionKinds.Restore,
            Note = $"Restored from v{versionNumber}",
            CreatedAtUtc = now.UtcDateTime,
            CreatedBy = actorId
        });

        await db.SaveChangesAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task<FormDraftSnapshot?> GetDraftSnapshotByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeShortCode(shortCode);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Forms
            .AsNoTracking()
            .Where(f => f.ShortCode == normalized)
            .Select(f => new FormDraftSnapshot(
                f.Id,
                f.Name,
                f.ShortCode,
                f.FormCode,
                f.SiteAvailable,
                f.DraftVersionNumber,
                f.PublishedVersionNumber))
            .SingleOrDefaultAsync(cancellationToken);
        return row;
    }

    public async Task<FormPublishedSnapshot?> GetPublishedSnapshotByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeShortCode(shortCode);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Resolve the form first so we can join the version row by the
        // currently-published version_number. site_available is read off the
        // live form (an admin can flip it without re-publishing).
        var form = await db.Forms
            .AsNoTracking()
            .SingleOrDefaultAsync(f => f.ShortCode == normalized, cancellationToken);

        if (form is null || form.PublishedVersionNumber is null || !form.SiteAvailable)
        {
            return null;
        }

        var version = await db.FormVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                v => v.FormId == form.Id && v.VersionNumber == form.PublishedVersionNumber.Value,
                cancellationToken);
        if (version is null) return null;

        return new FormPublishedSnapshot(
            FormId: form.Id,
            Name: version.Name,
            ShortCode: version.ShortCode,
            FormCode: version.FormCode,
            VersionNumber: version.VersionNumber,
            PublishedAtUtc: ToDateTimeOffset(version.CreatedAtUtc));
    }

    public async Task<FormWorkflowSnapshot?> GetWorkflowSnapshotByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeShortCode(shortCode);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var form = await db.Forms
            .AsNoTracking()
            .SingleOrDefaultAsync(f => f.ShortCode == normalized, cancellationToken);
        if (form is null) return null;

        // Workflow tasks render the published version when one exists, so an
        // admin's draft tweaks don't immediately leak into running tasks.
        // Fall back to the draft when nothing's been published — useful
        // while authoring, surfaced via IsDraftFallback so the SPA can flag
        // it. site_available is intentionally ignored here; that flag is a
        // public-surface gate, not an internal one.
        if (form.PublishedVersionNumber is int publishedNumber)
        {
            var version = await db.FormVersions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    v => v.FormId == form.Id && v.VersionNumber == publishedNumber,
                    cancellationToken);
            if (version is not null)
            {
                return new FormWorkflowSnapshot(
                    FormId: form.Id,
                    Name: version.Name,
                    ShortCode: version.ShortCode,
                    FormCode: version.FormCode,
                    PublishedVersionNumber: version.VersionNumber,
                    IsDraftFallback: false);
            }
        }

        return new FormWorkflowSnapshot(
            FormId: form.Id,
            Name: form.Name,
            ShortCode: form.ShortCode,
            FormCode: form.FormCode,
            PublishedVersionNumber: null,
            IsDraftFallback: true);
    }

    private static FormSummary ToSummary(FormEntity entity) => new(
        Id: entity.Id,
        Name: entity.Name,
        ShortCode: entity.ShortCode,
        SiteAvailable: entity.SiteAvailable,
        IsDraft: entity.IsDraft,
        DraftVersionNumber: entity.DraftVersionNumber,
        PublishedVersionNumber: entity.PublishedVersionNumber,
        UpdatedAtUtc: ToDateTimeOffset(entity.UpdatedAtUtc));

    private static Form ToModel(FormEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        ShortCode = entity.ShortCode,
        FormCode = entity.FormCode,
        SiteAvailable = entity.SiteAvailable,
        IsDraft = entity.IsDraft,
        DraftVersionNumber = entity.DraftVersionNumber,
        PublishedVersionNumber = entity.PublishedVersionNumber,
        CreatedAtUtc = ToDateTimeOffset(entity.CreatedAtUtc),
        CreatedBy = entity.CreatedBy,
        UpdatedAtUtc = ToDateTimeOffset(entity.UpdatedAtUtc),
        UpdatedBy = entity.UpdatedBy
    };

    private static FormVersion ToVersionModel(FormVersionEntity entity) => new()
    {
        Id = entity.Id,
        FormId = entity.FormId,
        VersionNumber = entity.VersionNumber,
        Name = entity.Name,
        ShortCode = entity.ShortCode,
        FormCode = entity.FormCode,
        SiteAvailable = entity.SiteAvailable,
        Kind = entity.Kind,
        Note = entity.Note,
        CreatedAtUtc = ToDateTimeOffset(entity.CreatedAtUtc),
        CreatedBy = entity.CreatedBy
    };

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
            : new DateTimeOffset(value.ToUniversalTime());

    private static string NormalizeName(string value) => value.Trim();

    private static string NormalizeShortCode(string value) =>
        value.Trim().ToLowerInvariant();

    private const string DefaultFormCode = """
        function Page({ data, onChange, onSubmit }) {
          return (
            <div className="p-3">
              <h3>New form</h3>
              <p className="text-muted">
                Edit this form's JSX in the Site Configuration → Forms editor.
              </p>
            </div>
          );
        }
        """;
}
