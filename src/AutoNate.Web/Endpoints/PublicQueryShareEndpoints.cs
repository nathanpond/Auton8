using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AutoNate.Web.Endpoints;

// Anonymous redemption endpoint for saved-query share tokens (Phase 3 of
// the Data Stores plan). The URL the issuer pastes into Slack /
// email / a wiki page hits this — there is no cookie required and no
// CSRF token to fetch. The request runs the query under the issuer's
// identity so the resulting rows respect the issuer's data grants. A
// revoked / expired / exhausted token returns 404 indistinguishable from
// "no such token" so a probe can't enumerate.
public static class PublicQueryShareEndpoints
{
    public static IEndpointRouteBuilder MapPublicQueryShareEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public/queries").AllowAnonymous();

        // GET so the link is bookmarkable. Parameter binding values flow
        // through query-string `?param=value` pairs; absence is a 400 if
        // the underlying AQL references `:param`. Hard cap of 1000 rows
        // matches the SPA executor.
        group.MapGet("/share/{token}", async (
            string token,
            HttpContext http,
            ISavedQueryShareTokenStore tokenStore,
            IDbContextFactory<AutoNateDbContext> dbContextFactory,
            IShareIssuerPrincipalFactory principalFactory,
            IAqlExecutor executor,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("PublicQueryShare");
            var redeemed = await tokenStore.RedeemAsync(token, DateTime.UtcNow, ct);
            if (redeemed is null) return Results.NotFound();

            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            var saved = await db.SavedQueries.AsNoTracking()
                .SingleOrDefaultAsync(q => q.Id == redeemed.SavedQueryId, ct);
            if (saved is null) return Results.NotFound();

            var issuer = await principalFactory.BuildAsync(redeemed.IssuedBy, ct);
            if (issuer is null)
            {
                // The issuer account is gone — refuse the link rather than
                // running as someone the system can't authorize.
                log.LogWarning(
                    "Share token {Token} for saved query {QueryId} references a missing issuer {IssuerId}; refusing.",
                    redeemed.Id, redeemed.SavedQueryId, redeemed.IssuedBy);
                return Results.NotFound();
            }

            var parameters = ExtractParameters(http);
            try
            {
                var result = await executor.ExecuteBoundAsync(
                    saved.QueryText, parameters, issuer, hardCap: 1000, ct);
                return Results.Ok(result);
            }
            catch (AqlParameterBindingException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
            catch (AqlValidationException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).WithName("PublicSharedQuery");

        return app;
    }

    // Query string → parameter dictionary. Reserved system params (`token`)
    // are filtered out, and the bind name is the key minus the leading `:`
    // if the caller sent one (`:foo` or `foo` both bind `foo`).
    private static IReadOnlyDictionary<string, string> ExtractParameters(HttpContext http)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in http.Request.Query)
        {
            var key = kv.Key.TrimStart(':');
            if (string.IsNullOrEmpty(key)) continue;
            if (string.Equals(key, "token", StringComparison.OrdinalIgnoreCase)) continue;
            dict[key] = kv.Value.ToString();
        }
        return dict;
    }
}
