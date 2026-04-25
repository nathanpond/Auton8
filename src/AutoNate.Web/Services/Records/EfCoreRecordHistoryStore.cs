using AutoNate.Web.Models.Records;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Records;

public sealed class EfCoreRecordHistoryStore(IDbContextFactory<AutoNateDbContext> dbContextFactory)
    : IRecordHistoryStore
{
    public async Task<IReadOnlyList<RecordFieldChange>> ListAsync(
        Guid recordId,
        string? fieldKey,
        int take,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(take, 1, 1000);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = dbContext.RecordFieldChanges.AsNoTracking()
            .Where(c => c.RecordId == recordId);

        if (!string.IsNullOrEmpty(fieldKey))
        {
            query = query.Where(c => c.FieldKey == fieldKey);
        }

        var rows = await query
            .OrderByDescending(c => c.ChangedAtUtc)
            .ThenByDescending(c => c.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(r => r.ToModel()).ToList();
    }
}
