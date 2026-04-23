using AutoNate.Web.Models;
using AutoNate.Web.Services.Auth;
using Microsoft.AspNetCore.Authorization;

namespace AutoNate.Web.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .RequireAuthorization();

        group.MapGet("/", async (ILocalUserStore store, CancellationToken cancellationToken) =>
        {
            var users = await store.ListAsync(cancellationToken);
            return Results.Ok(users);
        });

        group.MapPost("/", async (
            CreateUserRequest request,
            ILocalUserStore store,
            CancellationToken cancellationToken) =>
        {
            var user = await store.CreateAsync(
                request.Username,
                request.FirstName,
                request.LastName,
                request.Password,
                request.Email,
                cancellationToken);
            return Results.Created($"/api/users/{user.Id}", user);
        }).DisableAntiforgery();

        group.MapPut("/{id:long}", async (
            long id,
            UpdateUserRequest request,
            ILocalUserStore store,
            CancellationToken cancellationToken) =>
        {
            var updated = await store.UpdateAsync(
                id,
                request.Username,
                request.FirstName,
                request.LastName,
                request.Email,
                cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }).DisableAntiforgery();

        group.MapPost("/{id:long}/password", async (
            long id,
            ResetPasswordRequest request,
            ILocalUserStore store,
            CancellationToken cancellationToken) =>
        {
            var ok = await store.ResetPasswordAsync(id, request.Password, cancellationToken);
            return ok ? Results.NoContent() : Results.NotFound();
        }).DisableAntiforgery();

        group.MapDelete("/{id:long}", async (
            long id,
            ILocalUserStore store,
            CancellationToken cancellationToken) =>
        {
            var ok = await store.DeleteAsync(id, cancellationToken);
            return ok ? Results.NoContent() : Results.NotFound();
        }).DisableAntiforgery();

        return app;
    }

    public sealed record CreateUserRequest(
        string Username,
        string FirstName,
        string LastName,
        string Password,
        string? Email);

    public sealed record UpdateUserRequest(
        string Username,
        string FirstName,
        string LastName,
        string Email);

    public sealed record ResetPasswordRequest(string Password);
}
