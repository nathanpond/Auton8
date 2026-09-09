using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoNate.Web.Tests;

// #215. ContentAuthorizer memoizes GetAllowedIdsAsync per (user, kind, action)
// in a field on the scoped instance. That memo was a plain Dictionary, on the
// stated premise that "endpoint flow is sequential await — no Task.WhenAll
// across this service". NotesQueryEntity broke the premise: a bare
// `FROM Notes` fans out five or six GetAllowedIdsAsync calls under one
// Task.WhenAll, and two of them finishing together corrupted the Dictionary.
//
// The symptom was remote from the cause. The AQL endpoint catches everything
// and returns 400, so the only evidence was
// NotesQueryEndpointTests.FromNotes_OnEmpty_ReturnsSchemaColumns intermittently
// reporting "Expected: OK / Actual: BadRequest" under full-suite load.
//
// This test asserts the property the memo actually needs — that concurrent
// callers of one scoped authorizer neither throw nor disagree — rather than
// re-testing the Notes endpoint, so it still guards the memo if the Notes
// entity ever stops fanning out.
public sealed class ContentAuthorizerConcurrencyTests
{
    private static readonly string[] Kinds =
    [
        ContentKinds.Project, ContentKinds.Cabinet, ContentKinds.Notebook,
        ContentKinds.Page, ContentKinds.Folder, ContentKinds.Document
    ];

    private static readonly string[] AuthorizerActions =
        [Actions.View, Actions.Edit, Actions.Delete];

    // Above the pool's thread count on any dev machine, so the barrier releases
    // a genuine burst rather than a staggered few.
    private const int Parallelism = 18;

    [Fact]
    public async Task Concurrent_callers_share_one_authorizer_without_corrupting_its_memo()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();

        using var scope = factory.Services.CreateScope();
        var db = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<AutoNateDbContext>>()
            .CreateDbContextAsync();
        var userId = await db.LocalUsers.Select(u => u.UserId).FirstAsync();

        var actor = new ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            "test"));

        // Without this the whole file is vacuous: an unresolvable actor makes
        // every call return the ContentAccessSet.Empty singleton before the
        // memo is ever touched, and both tests below pass no matter what.
        Assert.NotNull(actor.TryGetUserId());

        // One scoped authorizer, as a request would have.
        //
        // Simply calling GetAllowedIdsAsync N times in a loop does NOT overlap
        // them: on a warm connection the whole computation completes
        // synchronously, so caller 2 finds caller 1's entry already stored and
        // the memo is never written concurrently. That is why this failure only
        // ever showed up under full-suite load, where waiting on the pool makes
        // the computation yield and several callers finish in a convoy.
        //
        // Two things together reproduce the convoy deterministically: the
        // barrier releases every caller at once, and YieldingDbContextFactory
        // guarantees the computation actually yields, so all of them are still
        // in flight when the first one returns. A barrier alone is not enough —
        // that was tried, and the calls still completed one after another.
        for (var round = 0; round < 12; round++)
        {
            var authorizer = NewAuthorizer(factory);

            using var gate = new Barrier(Parallelism);
            var calls = Enumerable.Range(0, Parallelism)
                .Select(i => Task.Run(async () =>
                {
                    var kind = Kinds[i % Kinds.Length];
                    var action = AuthorizerActions[(i / Kinds.Length) % AuthorizerActions.Length];
                    gate.SignalAndWait();
                    return await authorizer.GetAllowedIdsAsync(
                        actor, kind, action, CancellationToken.None);
                }))
                .ToList();

            // Awaits every task even if one faults, so a failing round reports
            // the authorizer's own exception rather than Task.WhenAll's first.
            var results = new List<ContentAccessSet>(calls.Count);
            var failures = new List<Exception>();
            foreach (var call in calls)
            {
                try
                {
                    results.Add(await call);
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }

            Assert.True(
                failures.Count == 0,
                $"Round {round}: {failures.Count} of {calls.Count} concurrent " +
                $"GetAllowedIdsAsync calls threw. First: {failures.FirstOrDefault()}");

            // A corrupted memo can also lose or duplicate an entry rather than
            // throw, so assert every call produced a set, not merely that none
            // threw.
            Assert.Equal(calls.Count, results.Count);
            Assert.All(results, Assert.NotNull);
        }
    }

    [Fact]
    public async Task The_memo_computes_once_per_key_under_concurrent_callers()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();

        using var scope = factory.Services.CreateScope();
        var db = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<AutoNateDbContext>>()
            .CreateDbContextAsync();
        var userId = await db.LocalUsers.Select(u => u.UserId).FirstAsync();

        var actor = new ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            "test"));

        // Without this the whole file is vacuous: an unresolvable actor makes
        // every call return the ContentAccessSet.Empty singleton before the
        // memo is ever touched, and both tests below pass no matter what.
        Assert.NotNull(actor.TryGetUserId());

        var authorizer = NewAuthorizer(factory);

        // The memo exists to collapse repeat work; guarding only the writes
        // would leave twenty callers each computing the same key. Released
        // together (see the note above — an ungated loop completes
        // synchronously and never overlaps), simultaneous asks for ONE key must
        // still yield a single shared result: reference equality is what proves
        // one computation was shared rather than twenty equal ones produced.
        using var gate = new Barrier(Parallelism);
        var tasks = Enumerable.Range(0, Parallelism)
            .Select(_ => Task.Run(async () =>
            {
                gate.SignalAndWait();
                return await authorizer.GetAllowedIdsAsync(
                    actor, ContentKinds.Page, Actions.View, CancellationToken.None);
            }))
            .ToList();

        var sets = await Task.WhenAll(tasks);
        var distinct = sets.Cast<object>().Distinct(ReferenceEqualityComparer.Instance).Count();
        Assert.True(
            distinct == 1,
            $"{distinct} distinct ContentAccessSet instances came back for one " +
            $"cache key across {sets.Length} simultaneous callers; the memo " +
            "computed more than once.");
    }

    // ContentAuthorizer is constructed directly rather than resolved, so the
    // DbContext factory can be wrapped. Nothing about the authorizer changes.
    private static IContentAuthorizer NewAuthorizer(AutoNateWebApplicationFactory factory) =>
        new ContentAuthorizer(
            new YieldingDbContextFactory(
                factory.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>()),
            NullLogger<ContentAuthorizer>.Instance);

    // On a warm connection the whole of ComputeAllowedIdsAsync can complete
    // without ever yielding, which serializes callers and hides the race this
    // file exists for. A real await in front of every context makes the
    // computation behave the way it does under load, where waiting on the pool
    // is what makes it yield.
    private sealed class YieldingDbContextFactory(
        IDbContextFactory<AutoNateDbContext> inner)
        : IDbContextFactory<AutoNateDbContext>
    {
        public AutoNateDbContext CreateDbContext() => inner.CreateDbContext();

        public async Task<AutoNateDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(15, cancellationToken);
            return await inner.CreateDbContextAsync(cancellationToken);
        }
    }
}
