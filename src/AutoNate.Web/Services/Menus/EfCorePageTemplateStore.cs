using AutoNate.Web.Models.Menus;
using AutoNate.Web.Persistence;
using AutoNate.Web.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PageTemplateEntity = AutoNate.Web.Persistence.Scaffolded.PageTemplate;

namespace AutoNate.Web.Services.Menus;

public sealed class EfCorePageTemplateStore : IPageTemplateStore
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbContextFactory;
    private readonly string _publicRootWithSep;
    private readonly string _publicUrlPrefix;

    public EfCorePageTemplateStore(
        IDbContextFactory<AutoNateDbContext> dbContextFactory,
        IDataPaths dataPaths,
        IOptions<DataOptions> dataOptions)
    {
        _dbContextFactory = dbContextFactory;
        // Snapshot the public-root path with a trailing separator so the
        // disk-path-to-URL rewrite is a cheap StartsWith + Substring. Both
        // values are runtime-immutable (read at startup) so caching them is
        // safe.
        _publicRootWithSep = dataPaths.PublicRoot.EndsWith(Path.DirectorySeparatorChar)
            ? dataPaths.PublicRoot
            : dataPaths.PublicRoot + Path.DirectorySeparatorChar;
        _publicUrlPrefix = (dataOptions.Value.PublicUrlPrefix ?? string.Empty).TrimEnd('/');
    }

    public async Task<IReadOnlyList<PageTemplate>> ListEnabledAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.PageTemplates.AsNoTracking()
            .Where(t => t.IsEnabled)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    public async Task<PageTemplate?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.PageTemplates.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Key == key, cancellationToken);
        return row is null ? null : ToModel(row);
    }

    private PageTemplate ToModel(PageTemplateEntity e) => new()
    {
        Id = e.Id,
        Key = e.Key,
        Name = e.Name,
        Description = e.Description,
        ThumbnailUrl = ResolveThumbnailUrl(e.ThumbnailUrl),
        Category = e.Category,
        IsEnabled = e.IsEnabled,
        CreatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(e.CreatedAtUtc),
        UpdatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(e.UpdatedAtUtc)
    };

    // The `thumbnail_url` column holds whichever of three forms the writer
    // had: a `data:` URI (built-in seed thumbnails), a normal http(s) URL
    // (admin-edited rows), or an absolute disk path under PublicRoot
    // (plugin-shipped PNGs PluginRuntime copied into the public data folder).
    // Only the third form needs translation — the first two travel back to
    // the SPA unchanged.
    private string? ResolveThumbnailUrl(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        // data: URIs and protocol URLs are already publicly addressable.
        if (stored.StartsWith("data:", StringComparison.Ordinal)) return stored;
        if (stored.Contains("://", StringComparison.Ordinal)) return stored;
        // Disk paths under PublicRoot get rewritten to /<PublicUrlPrefix>/<rel>.
        if (!Path.IsPathRooted(stored)) return stored;
        if (!stored.StartsWith(_publicRootWithSep, StringComparison.Ordinal)) return stored;
        var rel = stored.Substring(_publicRootWithSep.Length).Replace(Path.DirectorySeparatorChar, '/');
        return _publicUrlPrefix + "/" + rel;
    }
}
