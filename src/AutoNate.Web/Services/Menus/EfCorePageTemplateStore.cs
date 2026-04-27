using AutoNate.Web.Models.Menus;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using PageTemplateEntity = AutoNate.Web.Persistence.Scaffolded.PageTemplate;

namespace AutoNate.Web.Services.Menus;

public sealed class EfCorePageTemplateStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory) : IPageTemplateStore
{
    public async Task<IReadOnlyList<PageTemplate>> ListEnabledAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.PageTemplates.AsNoTracking()
            .Where(t => t.IsEnabled)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    public async Task<PageTemplate?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.PageTemplates.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Key == key, cancellationToken);
        return row is null ? null : ToModel(row);
    }

    private static PageTemplate ToModel(PageTemplateEntity e) => new()
    {
        Id = e.Id,
        Key = e.Key,
        Name = e.Name,
        Description = e.Description,
        DefaultPath = e.DefaultPath,
        IsEnabled = e.IsEnabled,
        CreatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(e.CreatedAtUtc),
        UpdatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(e.UpdatedAtUtc)
    };
}
