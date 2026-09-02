using System.Net;
using AutoNate.Web.Authorization;
using AutoNate.Web.Services.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

// archived-87: fifteen EntityKinds had no enforcement test at all.
//
// AuthorizationGatePresenceTests proves every mapped endpoint carries an
// explicit auth decision, but it reads route metadata — it never calls the
// endpoint, so it cannot tell a correct gate from one wired to the wrong
// (EntityKind, Action) pair. A route gated on `Dataset:List` when it should be
// `DataStore:List` passes presence and leaks to anyone holding a dataset
// grant.
//
// This closes that specific hole for the kind-level gates: each case proves
// the endpoint denies without a grant, and — the part that catches a
// mis-wired pair — that it opens for the kind and action actually declared
// on the route, and stays shut for a different kind's grant.
//
// Instance-scoped kinds (Cabinet, Notebook, Folder, Document, Project, Query,
// DataStore detail) go through RequirePermission with a resource id and are
// covered by their own authorizer tests; this file deliberately covers the
// RequireKindPermission surface, which is where a wrong pair is invisible.
[Trait("Category", "Integration")]
public sealed class KindGateEnforcementTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Dictionary<string, string?> EnforceConfig() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    // (route, kind the route declares, action the route declares, an unrelated
    // kind whose grant must NOT open it).
    public static TheoryData<string, string, string, string> KindGatedRoutes() => new()
    {
        { "/api/pipelines/", EntityKinds.Pipeline, Actions.List, EntityKinds.Dataset },
        { "/api/datasets/", EntityKinds.Dataset, Actions.List, EntityKinds.Pipeline },
        { "/api/dataconnectors/", EntityKinds.DataConnector, Actions.List, EntityKinds.Dataset },
        { "/api/admin/plugins/", EntityKinds.Plugin, Actions.Manage, EntityKinds.SiteConfig },
        { "/api/system-issues/", EntityKinds.SystemIssue, Actions.View, EntityKinds.SiteConfig },
        { "/api/admin/site-settings/", EntityKinds.SiteConfig, Actions.View, EntityKinds.SystemIssue },
        { "/api/admin/appearance/", EntityKinds.SiteConfig, Actions.View, EntityKinds.Plugin },
        { "/api/admin/registry/", EntityKinds.SiteConfig, Actions.View, EntityKinds.Plugin }
    };

    [Theory]
    [MemberData(nameof(KindGatedRoutes))]
    public async Task Route_WithoutTheDeclaredGrant_IsForbidden(
        string route, string kind, string action, string unrelatedKind)
    {
        _ = kind;
        _ = action;
        _ = unrelatedKind;

        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var client = await SignedInClientAsync(factory);

        var resp = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Theory]
    [MemberData(nameof(KindGatedRoutes))]
    public async Task Route_WithTheDeclaredGrant_IsAllowed(
        string route, string kind, string action, string unrelatedKind)
    {
        _ = unrelatedKind;

        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        await GrantAsync(factory, kind, action);
        var client = await SignedInClientAsync(factory);

        var resp = await client.GetAsync(route);

        // Not asserting 200 specifically: some of these return 204/empty
        // depending on seeded state. What matters is that the gate opened.
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // The case presence-checking cannot see. If a route were gated on the
    // wrong kind, the grant for that other kind would open it — so this
    // failing is the signal that the pair is mis-wired.
    [Theory]
    [MemberData(nameof(KindGatedRoutes))]
    public async Task Route_WithAnotherKindsGrant_IsStillForbidden(
        string route, string kind, string action, string unrelatedKind)
    {
        _ = kind;

        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        await GrantAsync(factory, unrelatedKind, action);
        var client = await SignedInClientAsync(factory);

        var resp = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    private static async Task GrantAsync(
        AutoNateWebApplicationFactory factory, string kind, string action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, AdminUserId.ToString(),
            action, $"/{kind}/*", "allow", 0), AdminUserId);
    }

    private static async Task<HttpClient> SignedInClientAsync(AutoNateWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        // Dev auto-login skips POSTs, so land the cookie with a GET first.
        (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
        return client;
    }
}
