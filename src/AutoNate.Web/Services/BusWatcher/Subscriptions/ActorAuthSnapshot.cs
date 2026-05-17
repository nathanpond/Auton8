using System.Collections.Generic;
using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.BusWatcher.Subscriptions;

// Per-connection actor state. Loaded once at connect time so fast gates and
// subscribe-time checks don't hit the DB on every message. Outbound user→user
// edges (e.g. supervisor relationships) are loaded so per-message
// `tasks:supervisees-of:{me}` style gates can resolve synchronously without a
// DB roundtrip.
//
// Lifetime: one snapshot per websocket connection. Rebuilt when the
// AuthChangeListener detects a membership or grant change for this actor.
public sealed record ActorAuthSnapshot(
    Guid UserId,
    bool IsSuperAdmin,
    bool AuthorizationEnabled,
    IReadOnlyDictionary<string, IReadOnlySet<string>> OutboundUserEdges)
{
    // True iff the actor has an outbound `supervisor` edge to `userId`
    // (i.e. actor supervises userId).
    public bool Supervises(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        return OutboundUserEdges.TryGetValue(EdgeKinds.Supervisor, out var supervisees)
            && supervisees.Contains(userId);
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> EmptyEdges =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

    public static async Task<ActorAuthSnapshot?> LoadAsync(
        ClaimsPrincipal actor,
        IAuthorizer authorizer,
        IOptions<AuthorizationOptions> authorizationOptions,
        IDbContextFactory<AutoNateDbContext> dbFactory,
        CancellationToken cancellationToken)
    {
        var userIdClaim = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return null;
        }

        var authEnabled = authorizationOptions.Value.Enabled;
        if (!authEnabled)
        {
            // When the authorization subsystem is off, every authenticated user
            // gets the equivalent of full access — match the rest of the
            // codebase rather than gating channels. No edges needed because
            // supervisee gates also bypass when IsSuperAdmin == true.
            return new ActorAuthSnapshot(userId, IsSuperAdmin: true, AuthorizationEnabled: false, EmptyEdges);
        }

        var capabilities = await authorizer.GetCapabilitiesAsync(actor, cancellationToken);
        var edges = await ActorOutboundUserEdges.LoadAsync(dbFactory, userId, cancellationToken);
        return new ActorAuthSnapshot(userId, capabilities.IsSuperAdmin, AuthorizationEnabled: true, edges);
    }
}
