using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests.Support;

/// <summary>
/// Convenience base for E2E test classes. Holds the shared
/// <see cref="AutoNateE2EFixture"/> and offers a one-call helper to spin up an
/// isolated <see cref="IBrowserContext"/> already signed in as the seeded
/// admin. Inheriting types automatically pick up the
/// <see cref="AutoNateE2ECollection"/> attribute via xUnit's attribute lookup,
/// so no per-class <c>[Collection(...)]</c> repetition is required.
/// </summary>
[Collection(AutoNateE2ECollection.Name)]
public abstract class E2ETestBase
{
    protected AutoNateE2EFixture Fixture { get; }

    protected E2ETestBase(AutoNateE2EFixture fixture) => Fixture = fixture;

    /// <summary>
    /// Opens a fresh browser context (own cookie jar), signs in as the seeded
    /// <c>admin</c>/<c>admin</c> super-admin, and returns a disposable session
    /// that closes the context on <c>await using</c> exit. The returned page is
    /// already at <c>/home</c> after the post-login redirect.
    /// </summary>
    protected async Task<SignedInSession> NewSignedInAsAdminAsync()
    {
        var context = await Fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        await AutoNateE2EFixture.SignInAsAdminAsync(page);
        return new SignedInSession(context, page);
    }
}

/// <summary>
/// Owned pairing of a browser context and a signed-in page. Disposes the
/// context (and therefore all of its pages) on scope exit so tests can use the
/// idiomatic <c>await using var session = …</c> pattern.
/// </summary>
public sealed class SignedInSession : IAsyncDisposable
{
    public IBrowserContext Context { get; }
    public IPage Page { get; }

    internal SignedInSession(IBrowserContext context, IPage page)
    {
        Context = context;
        Page = page;
    }

    public ValueTask DisposeAsync() => Context.DisposeAsync();
}
