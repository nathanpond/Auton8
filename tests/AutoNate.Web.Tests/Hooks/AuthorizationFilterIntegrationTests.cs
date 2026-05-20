using System.Security.Claims;
using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EntityTypes;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Authorization.Selectors;
using AutoNate.Web.Hooks;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoNate.Web.Tests.Hooks;

// Exercises the full Authorizer.AuthorizeAsync → autonate.authorize filter
// path. Uses authorization-disabled mode so the "raw" decision is a
// deterministic Allow without touching the database; the filter is the
// component under test.
public sealed class AuthorizationFilterIntegrationTests
{
    [Fact]
    public async Task NoFiltersRegistered_DecisionUnchanged()
    {
        var (authorizer, _) = Build();
        var actor = BuildActor(Guid.NewGuid());

        var decision = await authorizer.AuthorizeAsync(actor, Actions.Edit, new EntityRef(EntityKinds.Record, Guid.NewGuid().ToString()));

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public async Task Plugin_CanFlipAllowToDeny()
    {
        var (authorizer, registrar) = Build();
        registrar.AddFilterAsync<AuthorizeFilterContext>(
            HookPoints.AuthorizeAuthorize, priority: 10,
            (ctx, _, _) => Task.FromResult(ctx with
            {
                CurrentDecision = new AuthDecisionDto { Effect = AuthEffectDto.Deny, Reason = "plugin-deny" }
            }));

        var decision = await authorizer.AuthorizeAsync(
            BuildActor(Guid.NewGuid()),
            Actions.View,
            new EntityRef(EntityKinds.Record, Guid.NewGuid().ToString()));

        Assert.False(decision.IsAllowed);
        Assert.Equal("plugin-deny", decision.Reason);
    }

    [Fact]
    public async Task PluginThrows_FailsClosed_ReturnsDeny()
    {
        var (authorizer, registrar) = Build();
        registrar.AddFilterAsync<AuthorizeFilterContext>(
            HookPoints.AuthorizeAuthorize, priority: 10,
            (_, _, _) => throw new InvalidOperationException("boom"));

        var decision = await authorizer.AuthorizeAsync(
            BuildActor(Guid.NewGuid()),
            Actions.View,
            new EntityRef(EntityKinds.Record, Guid.NewGuid().ToString()));

        Assert.False(decision.IsAllowed);
        Assert.Equal("filter threw", decision.Reason);
    }

    [Fact]
    public async Task MultipleFilters_RunInPriorityOrder()
    {
        var (authorizer, registrar) = Build();
        registrar.AddFilterAsync<AuthorizeFilterContext>(
            HookPoints.AuthorizeAuthorize, priority: 20,
            (ctx, _, _) => Task.FromResult(ctx with
            {
                CurrentDecision = new AuthDecisionDto
                {
                    Effect = ctx.CurrentDecision.Effect,
                    Reason = ctx.CurrentDecision.Reason + "+late"
                }
            }));
        registrar.AddFilterAsync<AuthorizeFilterContext>(
            HookPoints.AuthorizeAuthorize, priority: 5,
            (ctx, _, _) => Task.FromResult(ctx with
            {
                CurrentDecision = new AuthDecisionDto
                {
                    Effect = ctx.CurrentDecision.Effect,
                    Reason = ctx.CurrentDecision.Reason + "+early"
                }
            }));

        var decision = await authorizer.AuthorizeAsync(
            BuildActor(Guid.NewGuid()),
            Actions.View,
            new EntityRef(EntityKinds.Record, Guid.NewGuid().ToString()));

        Assert.True(decision.IsAllowed);
        Assert.EndsWith("+early+late", decision.Reason);
    }

    private static (Authorizer authorizer, HookRegistrar registrar) Build()
    {
        var registrar = new HookRegistrar(NullLogger<ActionHub>.Instance);
        var registry = new EntityRegistry(CoreEntityTypes.All);
        var compilers = new SelectorCompilerRegistry(Array.Empty<ISelectorCompiler>());
        var options = Options.Create(new AuthorizationOptions { Enabled = false });
        var authorizer = new Authorizer(
            new ThrowingDbContextFactory(),
            options,
            registry,
            compilers,
            Array.Empty<IInstanceAuthorizer>(),
            registrar.Filters,
            EmptyRecordTypeShortCodeResolver.Instance,
            NullLogger<Authorizer>.Instance);
        return (authorizer, registrar);
    }

    private static ClaimsPrincipal BuildActor(Guid userId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, authenticationType: "test");
        return new ClaimsPrincipal(identity);
    }

    private sealed class ThrowingDbContextFactory : IDbContextFactory<AutoNateDbContext>
    {
        public AutoNateDbContext CreateDbContext() =>
            throw new InvalidOperationException("Disabled-path tests must not touch the database.");
        public Task<AutoNateDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Disabled-path tests must not touch the database.");
    }
}
