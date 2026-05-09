using System.Security.Claims;

namespace AutoNate.Web.Endpoints;

public static class HttpContextActorExtensions
{
    // Returns the authenticated user id parsed from the NameIdentifier claim,
    // or Guid.Empty when the claim is missing or unparsable. Endpoints use the
    // Guid.Empty sentinel to short-circuit to Unauthorized() without having to
    // catch a parse exception.
    public static Guid GetActorId(this HttpContext http)
    {
        var raw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}
