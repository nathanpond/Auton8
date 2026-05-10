using AutoNate.Web.Authorization.EndpointFilters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace AutoNate.Web.Tests.Authorization;

// Layer-1 of the long-term auth posture: every mapped /api/* endpoint must
// publish ONE of the following auth decisions via metadata:
//
//   * IAllowAnonymous                    — explicitly anonymous
//   * RequirePermissionMetadata          — gated by RequirePermission /
//                                          RequireKindPermission
//   * AuthorizationDecisionMetadata      — handler does the work
//                                          (AuthorizedInHandler / OpenToAuthenticated)
//
// Failure means a new endpoint shipped without an explicit auth decision —
// the next reviewer should pick the right marker. The metadata travels with
// the route registration so the rationale lives next to the handler, not in
// a test-only allow-list.
[Trait("Category", "Integration")]
public sealed class AuthorizationGatePresenceTests
{
    private readonly ITestOutputHelper _output;

    public AuthorizationGatePresenceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task EveryMappedEndpoint_HasExplicitAuthDecision()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        var endpoints = factory.Services
            .GetRequiredService<EndpointDataSource>().Endpoints;

        var problems = new List<string>();
        foreach (var endpoint in endpoints)
        {
            if (endpoint is not RouteEndpoint route) continue;
            var pattern = route.RoutePattern.RawText ?? "(unknown)";

            // Only audit endpoints we map ourselves under /api/*. Framework
            // helper endpoints, dev-only Swagger pages, and static-file
            // routes don't need to opt in.
            if (!IsAuditedSurface(pattern)) continue;

            var methods = route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                ?? new[] { "(any)" };
            foreach (var method in methods)
            {
                if (!HasExplicitAuthDecision(route))
                {
                    problems.Add(
                        $"{method,-6} {pattern} -- requires sign-in but no auth decision " +
                        "metadata. Pick one: RequirePermission/RequireKindPermission, " +
                        "AuthorizedInHandler(reason), or OpenToAuthenticated(reason).");
                }
            }
        }

        if (problems.Count > 0)
        {
            foreach (var p in problems) _output.WriteLine(p);
        }
        Assert.True(problems.Count == 0, $"{problems.Count} endpoint(s) failed the gate check.");
    }

    private static bool HasExplicitAuthDecision(RouteEndpoint endpoint)
    {
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null) return true;
        if (endpoint.Metadata.GetMetadata<RequirePermissionMetadata>() is not null) return true;
        if (endpoint.Metadata.GetMetadata<AuthorizationDecisionMetadata>() is not null) return true;
        return false;
    }

    private static bool IsAuditedSurface(string pattern)
        => pattern.StartsWith("/api/", StringComparison.Ordinal)
        || pattern.StartsWith("api/", StringComparison.Ordinal);
}
