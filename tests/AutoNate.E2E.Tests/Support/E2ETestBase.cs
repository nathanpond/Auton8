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
    /// already at <c>/home</c> after the post-login redirect. The session also
    /// installs a <see cref="ConsoleErrorGuard"/> that fails the test if any
    /// non-allowlisted <c>console.error</c> or uncaught page error appears —
    /// catches the React/Mantine/DOM regressions that "h1 appears" assertions
    /// silently pass through.
    /// </summary>
    protected async Task<SignedInSession> NewSignedInAsAdminAsync()
    {
        var context = await Fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        // Install the guard BEFORE sign-in so a busted login or post-login
        // render still trips the gate. Sign-in itself navigates to / and then
        // /home; either page emitting an error fails the test on dispose.
        var guard = new ConsoleErrorGuard(page);
        await AutoNateE2EFixture.SignInAsAdminAsync(page);
        return new SignedInSession(context, page, guard);
    }

    /// <summary>
    /// A guarded session signed in as someone other than the seeded admin.
    /// </summary>
    /// <remarks>
    /// Specs that needed a limited user or an anonymous visitor used to build
    /// their own context and page, which silently opted them out of
    /// ConsoleErrorGuard — the guard is installed by NewSignedInAsAdminAsync,
    /// so anything that could not use that helper had no guard at all (#93).
    /// Permission-denial journeys are exactly where a silent client-side
    /// exception is easiest to miss, because the page is *expected* to look
    /// empty.
    /// </remarks>
    protected async Task<SignedInSession> NewSignedInAsAsync(string username, string password)
    {
        var context = await Fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        var guard = new ConsoleErrorGuard(page);
        await AutoNateE2EFixture.SignInAsync(page, username, password);
        return new SignedInSession(context, page, guard);
    }

    /// <summary>
    /// A guarded session with no sign-in — for specs that drive the login page
    /// itself or assert anonymous behaviour.
    /// </summary>
    protected async Task<SignedInSession> NewAnonymousSessionAsync()
    {
        var context = await Fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        var guard = new ConsoleErrorGuard(page);
        return new SignedInSession(context, page, guard);
    }
}

/// <summary>
/// Owned pairing of a browser context and a signed-in page. Disposes the
/// console-error guard first (so its assertion can fail the test before the
/// context is torn down), then the context (and therefore all of its pages).
/// Tests use the idiomatic <c>await using var session = …</c> pattern.
/// </summary>
public sealed class SignedInSession : IAsyncDisposable
{
    public IBrowserContext Context { get; }
    public IPage Page { get; }
    public ConsoleErrorGuard ConsoleErrors { get; }

    internal SignedInSession(IBrowserContext context, IPage page, ConsoleErrorGuard guard)
    {
        Context = context;
        Page = page;
        ConsoleErrors = guard;
    }

    public async ValueTask DisposeAsync()
    {
        // Guard first — it throws if it found errors, and the test should see
        // that as the failure. Wrap context disposal in try/finally so even a
        // throwing guard cleans up the browser context.
        try
        {
            await ConsoleErrors.DisposeAsync();
        }
        finally
        {
            await Context.DisposeAsync();
        }
    }
}
