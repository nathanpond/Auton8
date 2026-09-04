using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Auth;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// The cached user directory (#9): still correct, still not leaking.
/// </summary>
/// <remarks>
/// Caching an endpoint changes two things that can go wrong independently.
/// It can go **stale** — a user created in the admin screen missing from an
/// assignee picker, which reads as the create having failed. And it can go
/// **leaky** — the admin-only fields are blanked on the way out today, and a
/// cache that stored full rows would serve every user's email to any
/// authenticated account the first time a caller forgot.
///
/// The leak is the one that would not be noticed, so it is asserted first and
/// against the raw response text rather than a typed property: a field of any
/// name carrying an address would fail.
///
/// Every store call here uses named arguments. The first draft did not, put the
/// email into <c>lastName</c> by positional accident, and the leak assertion
/// caught it — which is the assertion working, but a positional call in a test
/// about not leaking emails is a poor thing to leave lying around.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class UserDirectorySnapshotTests
{
    [Fact]
    public async Task The_directory_never_carries_admin_only_fields()
    {
        await using var app = await AutoNateWebApplicationFactory.CreateAsync();
        var client = app.CreateClient();

        var scope = app.Services.CreateScope().ServiceProvider;
        var store = scope.GetRequiredService<ILocalUserStore>();
        await store.CreateAsync(
            username: "leaky", firstName: "Leaky", lastName: "User",
            password: "secret-password", email: "leaky@private.example",
            cancellationToken: CancellationToken.None);

        var body = await client.GetStringAsync("/api/users/directory");

        // Raw text, not a typed read: the regression this guards is a future
        // shape change that reintroduces the field under any name.
        Assert.DoesNotContain("private.example", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-password", body, StringComparison.Ordinal);
        Assert.Contains("leaky", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_newly_created_user_appears_immediately()
    {
        // The staleness failure mode. A 30-second TTL without invalidation
        // would pass a test that waited, and fail the person who just pressed
        // Create and cannot find their user.
        await using var app = await AutoNateWebApplicationFactory.CreateAsync();
        var client = app.CreateClient();

        var before = await client.GetFromJsonAsync<List<LocalUser>>("/api/users/directory");

        var scope = app.Services.CreateScope().ServiceProvider;
        var store = scope.GetRequiredService<ILocalUserStore>();
        var username = "fresh-" + Guid.NewGuid().ToString("N")[..8];
        await store.CreateAsync(
            username: username, firstName: "Fresh", lastName: "User",
            password: "pw", email: $"{username}@example.com",
            cancellationToken: CancellationToken.None);

        var after = await client.GetFromJsonAsync<List<LocalUser>>("/api/users/directory");

        Assert.DoesNotContain(before!, u => u.Username == username);
        Assert.Contains(after!, u => u.Username == username);
    }

    [Fact]
    public async Task A_deleted_user_disappears_immediately()
    {
        await using var app = await AutoNateWebApplicationFactory.CreateAsync();
        var client = app.CreateClient();

        var scope = app.Services.CreateScope().ServiceProvider;
        var store = scope.GetRequiredService<ILocalUserStore>();
        var username = "doomed-" + Guid.NewGuid().ToString("N")[..8];
        var created = await store.CreateAsync(
            username: username, firstName: "Doomed", lastName: "User",
            password: "pw", email: $"{username}@example.com",
            cancellationToken: CancellationToken.None);

        Assert.Contains(
            (await client.GetFromJsonAsync<List<LocalUser>>("/api/users/directory"))!,
            u => u.Username == username);

        await store.DeleteAsync(created.Id, CancellationToken.None);

        // A stale cache here is worse than a stale create: a removed account
        // would still be offered as an assignee.
        Assert.DoesNotContain(
            (await client.GetFromJsonAsync<List<LocalUser>>("/api/users/directory"))!,
            u => u.Username == username);
    }

    [Fact]
    public async Task An_edited_user_shows_its_new_name()
    {
        await using var app = await AutoNateWebApplicationFactory.CreateAsync();
        var client = app.CreateClient();

        var scope = app.Services.CreateScope().ServiceProvider;
        var store = scope.GetRequiredService<ILocalUserStore>();
        var username = "renamed-" + Guid.NewGuid().ToString("N")[..8];
        var created = await store.CreateAsync(
            username: username, firstName: "Before", lastName: "User",
            password: "pw", email: $"{username}@example.com",
            cancellationToken: CancellationToken.None);

        _ = await client.GetStringAsync("/api/users/directory");
        await store.UpdateAsync(
            created.Id, username: username, firstName: "After", lastName: "User",
            email: $"{username}@example.com", cancellationToken: CancellationToken.None);

        var after = await client.GetFromJsonAsync<List<LocalUser>>("/api/users/directory");
        Assert.Equal("After", after!.Single(u => u.Username == username).FirstName);
    }

    [Fact]
    public async Task The_snapshot_serves_repeat_reads_without_going_back_to_the_database()
    {
        // The point of the change. Asserted through the cache directly, because
        // the endpoint cannot show whether a query ran — and "it looks cached"
        // is how the original N+1 survived review.
        await using var app = await AutoNateWebApplicationFactory.CreateAsync();
        _ = app.CreateClient();

        var cache = app.Services.GetRequiredService<UserDirectorySnapshotCache>();

        var first = await cache.GetAsync(CancellationToken.None);
        var second = await cache.GetAsync(CancellationToken.None);

        // Same instance, so the second call did no work at all.
        Assert.Same(first, second);

        cache.Invalidate();
        var third = await cache.GetAsync(CancellationToken.None);
        Assert.NotSame(first, third);
    }

    [Fact]
    public async Task The_fields_consumers_actually_read_are_all_present()
    {
        // #9 suggested projecting to (id, username, displayName). The SPA reads
        // userId, username, firstName and lastName across 16 call sites, so that
        // projection would have broken assignee pickers and comment authorship
        // while looking like a pure optimisation.
        await using var app = await AutoNateWebApplicationFactory.CreateAsync();
        var client = app.CreateClient();

        var body = await client.GetStringAsync("/api/users/directory");
        var first = JsonDocument.Parse(body).RootElement.EnumerateArray().First();

        foreach (var field in new[] { "userId", "username", "firstName", "lastName" })
        {
            Assert.True(first.TryGetProperty(field, out _), $"'{field}' is read by the SPA and must survive.");
        }
    }
}
