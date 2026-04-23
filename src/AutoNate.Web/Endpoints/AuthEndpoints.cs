using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace AutoNate.Web.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").AllowAnonymous();

        group.MapGet("/me", (HttpContext context) =>
        {
            var user = context.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                return Results.Json(new { authenticated = false });
            }

            return Results.Json(new
            {
                authenticated = true,
                userId = user.FindFirstValue(ClaimTypes.NameIdentifier),
                username = user.FindFirstValue(ClaimTypes.Name),
                firstName = user.FindFirstValue(ClaimTypes.GivenName),
                lastName = user.FindFirstValue(ClaimTypes.Surname),
                email = user.FindFirstValue(ClaimTypes.Email),
                authSource = user.FindFirstValue("auth_source"),
                idpKey = user.FindFirstValue("idp_key")
            });
        });

        group.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        })
        .DisableAntiforgery();

        return app;
    }
}
