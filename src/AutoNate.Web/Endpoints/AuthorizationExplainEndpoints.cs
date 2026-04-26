using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;

namespace AutoNate.Web.Endpoints;

// Admin-only inspection endpoint backing the effective-permissions debugger
// page. Given a user, action, kind, and target id, returns the final
// allow/deny decision along with the per-grant trace that produced it. The
// payload is shaped for the SPA to render directly — principals carry their
// display names so admins don't have to cross-reference roles/groups by id.
public static class AuthorizationExplainEndpoints
{
    public static IEndpointRouteBuilder MapAuthorizationExplainEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/explain").RequireAuthorization();

        group.MapPost("/", async (
            ExplainRequest request,
            IAuthorizer authorizer,
            CancellationToken ct) =>
        {
            if (!Guid.TryParse(request.AsUserId, out var userId))
            {
                return Results.BadRequest(new { error = "asUserId must be a GUID." });
            }

            if (string.IsNullOrWhiteSpace(request.Action))
            {
                return Results.BadRequest(new { error = "action is required." });
            }

            if (string.IsNullOrWhiteSpace(request.TargetKind))
            {
                return Results.BadRequest(new { error = "targetKind is required." });
            }

            var target = new EntityRef(request.TargetKind, request.TargetId ?? string.Empty);
            var explanation = await authorizer.ExplainAsync(userId, request.Action, target, ct);

            return Results.Ok(new ExplainResponse(
                explanation.Effect.ToString().ToLowerInvariant(),
                explanation.Reason,
                explanation.AsUserId,
                explanation.IsSuperAdmin,
                explanation.GroupIds,
                explanation.RoleIds,
                explanation.Grants.Select(g => new ExplainGrantDto(
                    g.PrincipalKind,
                    g.PrincipalId,
                    g.PrincipalName,
                    g.Action,
                    g.SelectorString,
                    g.Effect.ToString().ToLowerInvariant(),
                    g.Matched,
                    g.Error)).ToList()));
        }).DisableAntiforgery();

        return app;
    }

    public sealed record ExplainRequest(
        string AsUserId,
        string Action,
        string TargetKind,
        string? TargetId);

    public sealed record ExplainResponse(
        string Effect,
        string Reason,
        Guid AsUserId,
        bool IsSuperAdmin,
        IReadOnlyList<Guid> GroupIds,
        IReadOnlyList<Guid> RoleIds,
        IReadOnlyList<ExplainGrantDto> Grants);

    public sealed record ExplainGrantDto(
        string PrincipalKind,
        string PrincipalId,
        string? PrincipalName,
        string Action,
        string SelectorString,
        string Effect,
        bool? Matched,
        string? Error);
}
