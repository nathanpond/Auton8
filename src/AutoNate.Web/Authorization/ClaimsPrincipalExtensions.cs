using System.Security.Claims;

namespace AutoNate.Web.Authorization;

public static class ClaimsPrincipalExtensions
{
    // Parses the authenticated user id out of the NameIdentifier claim. Returns
    // null when the claim is absent or unparsable. Companion to
    // HttpContextActorExtensions.GetActorId — that one operates on HttpContext
    // and uses Guid.Empty as the sentinel since endpoint code wants a single
    // unconditional `if (id == Guid.Empty) return Unauthorized()` shape.
    // Authorization-layer callers prefer null because they already branch on
    // "no user identity" as a real decision case (deny vs. allow), not a
    // request short-circuit.
    public static Guid? TryGetUserId(this ClaimsPrincipal actor)
    {
        var raw = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
