using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

// Shared helper for resolving a batch of user GUIDs into display names.
// Endpoints that surface "who did this" (e.g. version history) hit this so
// the SPA can render names without an extra round-trip.
//
// Display rule: "FirstName LastName" if either is non-empty (trimmed), else
// the username. Falls back to null when the id is missing from local_users
// (e.g. an externally-provisioned actor whose row was later deleted).
public static class UserDisplayName
{
    public static async Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(
        AutoNateDbContext db, IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();
        var rows = await db.LocalUsers.AsNoTracking()
            .Where(u => ids.Contains(u.UserId))
            .Select(u => new { u.UserId, u.FirstName, u.LastName, u.Username })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.UserId, r => Format(r.FirstName, r.LastName, r.Username));
    }

    public static string Format(string? firstName, string? lastName, string username)
    {
        var fn = (firstName ?? string.Empty).Trim();
        var ln = (lastName ?? string.Empty).Trim();
        if (fn.Length > 0 && ln.Length > 0) return $"{fn} {ln}";
        if (fn.Length > 0) return fn;
        if (ln.Length > 0) return ln;
        return username;
    }
}
