using System.Security.Claims;
using AutoNate.Web.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Query;

// Builds a ClaimsPrincipal for the user who issued a saved-query share
// token (Phase 3 of the Data Stores plan). Anonymous redemption of a token
// runs the underlying query as the issuer so the data they're sharing is
// gated by the grants the issuer already has — not by the anonymous
// browser session, which has none. Mirrors the claim layout of the cookie
// sign-in path so IAuthorizer's grant resolution behaves the same.
public interface IShareIssuerPrincipalFactory
{
    Task<ClaimsPrincipal?> BuildAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed class ShareIssuerPrincipalFactory(
    IDbContextFactory<AutoNateDbContext> dbContextFactory) : IShareIssuerPrincipalFactory
{
    public async Task<ClaimsPrincipal?> BuildAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.LocalUsers.AsNoTracking()
            .SingleOrDefaultAsync(u => u.UserId == userId, cancellationToken);
        if (user is null) return null;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(ClaimTypes.GivenName, user.FirstName ?? string.Empty),
            new(ClaimTypes.Surname, user.LastName ?? string.Empty),
        };
        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
