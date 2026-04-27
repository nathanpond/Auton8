using System.Security.Claims;
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

namespace AutoNate.Web.Tests.Authorization;

public sealed class AuthorizerTests
{
    [Fact]
    public async Task AuthorizeAsync_WhenDisabled_AllowsRegardless()
    {
        var authorizer = BuildAuthorizer(enabled: false);
        var actor = BuildActor(Guid.NewGuid());

        var decision = await authorizer.AuthorizeAsync(
            actor,
            Actions.Edit,
            new EntityRef(EntityKinds.Record, Guid.NewGuid().ToString()));

        Assert.Equal(AuthEffect.Allow, decision.Effect);
    }

    [Fact]
    public async Task FilterQueryAsync_WhenDisabled_ReturnsSourceUnchanged()
    {
        var authorizer = BuildAuthorizer(enabled: false);
        var actor = BuildActor(Guid.NewGuid());
        var source = new[] { "a", "b", "c" }.AsQueryable();

        var filtered = await authorizer.FilterQueryAsync(
            db: null!,
            actor: actor,
            kind: EntityKinds.Record,
            action: Actions.View,
            source: source);

        Assert.Same(source, filtered);
    }

    [Fact]
    public async Task GetCapabilitiesAsync_WhenDisabled_ReturnsAllAllowed()
    {
        var authorizer = BuildAuthorizer(enabled: false);
        var actor = BuildActor(Guid.NewGuid());

        var summary = await authorizer.GetCapabilitiesAsync(actor);

        Assert.False(summary.IsSuperAdmin);
        Assert.True(summary.Capabilities[EntityKinds.Record][Actions.View]);
        Assert.True(summary.Capabilities[EntityKinds.WorkflowModel][Actions.Publish]);
    }

    private static Authorizer BuildAuthorizer(bool enabled)
    {
        var registry = new EntityRegistry(CoreEntityTypes.All);
        var compilers = new SelectorCompilerRegistry(Array.Empty<ISelectorCompiler>());
        var options = Options.Create(new AuthorizationOptions { Enabled = enabled });
        return new Authorizer(
            new ThrowingDbContextFactory(),
            options,
            registry,
            compilers,
            Array.Empty<IInstanceAuthorizer>(),
            new HookRegistrar(NullLogger<ActionHub>.Instance).Filters,
            NullLogger<Authorizer>.Instance);
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
