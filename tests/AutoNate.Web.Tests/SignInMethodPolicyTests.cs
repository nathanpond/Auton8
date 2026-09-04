using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// Which sign-in methods are available, and the guards against having none (#94).
/// </summary>
/// <remarks>
/// Turning local sign-in off is the first configuration in this product that can
/// lock everybody out, so most of what is asserted here is a refusal. The two
/// tests that matter most are the ones nobody writes by accident: that a
/// disabled method's endpoint actually *refuses* rather than merely being
/// hidden, and that the first-administrator bootstrap still works on a fresh
/// install whose configuration says local sign-in is off — which is the exact
/// shape of a guard producing the lockout it exists to prevent.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SignInMethodPolicyTests
{
    private static async Task<(AutoNateWebApplicationFactory App, HttpClient Client)> BuildAsync(
        IReadOnlyDictionary<string, string?>? extraConfig = null)
    {
        var app = await AutoNateWebApplicationFactory.CreateAsync(extraConfig);
        var client = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing
            .WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // The Development auto-login middleware only activates on GET, so a POST
        // from a fresh client has no actor.
        (await client.GetAsync("/api/admin/identity-providers")).EnsureSuccessStatusCode();
        return (app, client);
    }

    private static async Task<Guid> ProviderAsync(
        AutoNateWebApplicationFactory app, string slug = "corp", bool enabled = true,
        string kind = IdentityProviderKinds.Oidc)
    {
        var store = app.Services.CreateScope().ServiceProvider
            .GetRequiredService<IIdentityProviderStore>();
        var provider = await store.CreateAsync(new CreateIdentityProviderRequest(
            Kind: kind,
            DisplayName: $"Provider {slug}",
            Slug: slug,
            IsEnabled: enabled,
            OidcAuthority: "https://idp.example.com",
            OidcClientId: "auton8",
            OidcScopes: null,
            SamlEntityId: "https://idp.example.com/saml",
            SamlMetadataUrl: null, SamlMetadataXml: null, SamlSigningCertificate: null,
            Secret: null), Guid.NewGuid(), CancellationToken.None);
        return provider.Id;
    }

    private static async Task ProveAsync(AutoNateWebApplicationFactory app, Guid providerId)
    {
        var store = app.Services.CreateScope().ServiceProvider
            .GetRequiredService<IIdentityProviderStore>();
        await store.RecordSuccessfulSignInAsync(providerId, DateTime.UtcNow, CancellationToken.None);
    }

    private static async Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client, string username, string password)
    {
        var tokenResponse = await client.GetAsync("/api/auth/antiforgery");
        tokenResponse.EnsureSuccessStatusCode();
        var tokens = await tokenResponse.Content.ReadFromJsonAsync<AntiforgeryTokenDto>()
            ?? throw new InvalidOperationException("Antiforgery token response was empty.");

        return await client.PostAsync("/account/login", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                [tokens.FormFieldName] = tokens.Token,
                ["username"] = username,
                ["password"] = password,
            }));
    }

    private sealed record AntiforgeryTokenDto(string Token, string FormFieldName, string HeaderName);

    private static Task<HttpResponseMessage> SetMethodsAsync(
        HttpClient client, bool local, bool oidc, bool saml) =>
        client.PutAsJsonAsync("/api/admin/sign-in-methods", new { local, oidc, saml });

    // ── Every combination is reachable, and reflected ───────────────────────

    [Fact]
    public async Task All_three_methods_are_enabled_on_a_fresh_install()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;

        // Absent settings mean enabled. An upgrade must not silently switch a
        // method off, and every install predating this story had all three.
        var methods = await client.GetFromJsonAsync<JsonElement>("/api/auth/methods");
        Assert.True(methods.GetProperty("local").GetBoolean());
        Assert.True(methods.GetProperty("oidc").GetBoolean());
        Assert.True(methods.GetProperty("saml").GetBoolean());
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task Any_combination_that_leaves_a_way_in_is_accepted(bool local, bool oidc, bool saml)
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;

        (await SetMethodsAsync(client, local, oidc, saml)).EnsureSuccessStatusCode();

        var methods = await client.GetFromJsonAsync<JsonElement>("/api/auth/methods");
        Assert.Equal(local, methods.GetProperty("local").GetBoolean());
        Assert.Equal(oidc, methods.GetProperty("oidc").GetBoolean());
        Assert.Equal(saml, methods.GetProperty("saml").GetBoolean());
    }

    [Fact]
    public async Task A_provider_whose_protocol_is_disabled_is_not_offered()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;
        var provider = await ProviderAsync(app, "corp");
        await ProveAsync(app, provider);

        var withOidc = await client.GetFromJsonAsync<JsonElement>("/api/auth/providers");
        Assert.Equal(1, withOidc.GetArrayLength());

        (await SetMethodsAsync(client, local: true, oidc: false, saml: true)).EnsureSuccessStatusCode();

        // Not merely a method flag flipped: the provider disappears from the
        // list, or the login page draws a button whose endpoint will refuse it.
        var withoutOidc = await client.GetFromJsonAsync<JsonElement>("/api/auth/providers");
        Assert.Equal(0, withoutOidc.GetArrayLength());
    }

    // ── The unreachable configurations ──────────────────────────────────────

    [Fact]
    public async Task Disabling_every_method_is_refused()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;

        var response = await SetMethodsAsync(client, false, false, false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("every way of signing in", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Disabling_local_with_no_federated_provider_is_refused()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;

        var response = await SetMethodsAsync(client, local: false, oidc: true, saml: true);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("no federated provider is enabled", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Disabling_local_with_an_unproven_provider_is_refused_and_names_it()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;
        await ProviderAsync(app, "corp");

        // Configured is not working. This gap is exactly where an install locks
        // itself out: an administrator who sets up a provider and disables local
        // in one sitting has no way to know the provider does not work yet.
        var response = await SetMethodsAsync(client, local: false, oidc: true, saml: true);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("has completed a sign-in", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Provider corp", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disabling_local_is_allowed_once_a_provider_has_worked()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;
        var provider = await ProviderAsync(app, "corp");

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await SetMethodsAsync(client, local: false, oidc: true, saml: true)).StatusCode);

        await ProveAsync(app, provider);

        // Both directions of the guard in one test: the same request refused
        // and then accepted, with only the proof changing.
        (await SetMethodsAsync(client, local: false, oidc: true, saml: true)).EnsureSuccessStatusCode();
        var methods = await client.GetFromJsonAsync<JsonElement>("/api/auth/methods");
        Assert.False(methods.GetProperty("local").GetBoolean());
    }

    [Fact]
    public async Task A_provider_proven_under_a_protocol_being_disabled_does_not_count()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;
        var provider = await ProviderAsync(app, "corp", kind: IdentityProviderKinds.Oidc);
        await ProveAsync(app, provider);

        // The only working provider is OIDC, and the same request turns OIDC
        // off. Counting it would leave SAML as the only enabled method with
        // nothing configured behind it — a lockout arrived at by counting the
        // wrong thing.
        var response = await SetMethodsAsync(client, local: false, oidc: false, saml: true);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_disabled_provider_does_not_count_as_proof()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;
        var provider = await ProviderAsync(app, "corp", enabled: false);
        await ProveAsync(app, provider);

        // It worked once, and then somebody switched it off. History is not
        // availability.
        var response = await SetMethodsAsync(client, local: false, oidc: true, saml: true);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Enforcement, not decoration ─────────────────────────────────────────

    [Fact]
    public async Task A_disabled_local_method_refuses_a_direct_post()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;
        var provider = await ProviderAsync(app, "corp");
        await ProveAsync(app, provider);
        (await SetMethodsAsync(client, local: false, oidc: true, saml: true)).EnsureSuccessStatusCode();

        // The assertion that separates a disabled method from a hidden button.
        // Without server-side enforcement, an administrator who switched local
        // sign-in off would still have every password in the database working
        // against a direct POST.
        // With a valid antiforgery token, so this proves the *method* refusal
        // rather than the CSRF middleware refusing first — a test that passed
        // because it never reached the handler would prove nothing about #94.
        var response = await PostLoginAsync(client, "admin", "admin");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("method_disabled", response.Headers.Location!.OriginalString, StringComparison.Ordinal);

        // And no session came back with it.
        Assert.DoesNotContain(
            response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            c => c.Contains(".AspNetCore.Cookies", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_disabled_federated_method_refuses_its_challenge()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;
        var provider = await ProviderAsync(app, "corp");
        await ProveAsync(app, provider);
        (await SetMethodsAsync(client, local: true, oidc: false, saml: false)).EnsureSuccessStatusCode();

        var oidc = await client.GetAsync("/api/auth/oidc/corp/challenge");
        var saml = await client.GetAsync("/api/auth/saml/corp/challenge");

        Assert.Equal("/?error=provider_unavailable", oidc.Headers.Location?.OriginalString);
        Assert.Equal("/?error=provider_unavailable", saml.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task A_disabled_federated_method_refuses_its_callback_too()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;
        var provider = await ProviderAsync(app, "corp");
        await ProveAsync(app, provider);
        (await SetMethodsAsync(client, local: true, oidc: false, saml: false)).EnsureSuccessStatusCode();

        // A tab that started a sign-in before the method was switched off must
        // not be able to finish one after. The window is small; "small" is not a
        // property anybody should have to rely on.
        var oidc = await client.GetAsync("/api/auth/oidc/corp/callback?code=x&state=y");
        var saml = await client.PostAsync("/api/auth/saml/corp/acs", new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("SAMLResponse", "irrelevant")]));

        Assert.Equal("/?error=provider_unavailable", oidc.Headers.Location?.OriginalString);
        Assert.Equal("/?error=provider_unavailable", saml.Headers.Location?.OriginalString);
    }

    // ── Round-trip, and what disabling must not do ──────────────────────────

    [Fact]
    public async Task Disabling_local_does_not_touch_local_accounts()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;
        var provider = await ProviderAsync(app, "corp");
        await ProveAsync(app, provider);

        var dbFactory = app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        List<(Guid Id, string Hash)> Snapshot()
        {
            using var db = dbFactory.CreateDbContext();
            return db.LocalUsers.AsNoTracking()
                .Select(u => new { u.UserId, u.PasswordHash })
                .ToList()
                .Select(u => (u.UserId, u.PasswordHash))
                .OrderBy(u => u.UserId)
                .ToList();
        }

        var before = Snapshot();
        Assert.NotEmpty(before);

        (await SetMethodsAsync(client, local: false, oidc: true, saml: true)).EnsureSuccessStatusCode();
        (await SetMethodsAsync(client, local: true, oidc: true, saml: true)).EnsureSuccessStatusCode();

        // Disabling a sign-in *method* is not a statement about accounts. Off
        // and on again has to leave them exactly as they were, hashes included.
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task The_methods_endpoint_leaks_no_configuration()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;
        await ProviderAsync(app, "corp");

        var providers = await client.GetStringAsync("/api/auth/providers");
        var methods = await client.GetStringAsync("/api/auth/methods");

        // A signed-out visitor gets display name, slug and kind — enough to draw
        // a button — and three booleans. Not the authority, not the client id,
        // and not whether the break-glass override is what is keeping local
        // available, which would tell them the install is currently in trouble.
        Assert.Contains("Provider corp", providers, StringComparison.Ordinal);
        Assert.DoesNotContain("idp.example.com", providers, StringComparison.Ordinal);
        Assert.DoesNotContain("auton8", providers, StringComparison.Ordinal);
        Assert.DoesNotContain("override", methods, StringComparison.OrdinalIgnoreCase);
    }

    // ── Reachability holds even when stored state is wrong ──────────────────

    [Fact]
    public async Task Local_comes_back_when_the_provider_that_justified_disabling_it_is_switched_off()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;
        var provider = await ProviderAsync(app, "corp");
        await ProveAsync(app, provider);
        (await SetMethodsAsync(client, local: false, oidc: true, saml: true)).EnsureSuccessStatusCode();

        Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/auth/methods"))
            .GetProperty("local").GetBoolean());

        // The write-time guard cannot see this coming: switching the provider
        // off is a different action, on a different screen, and it is what
        // turns a valid configuration into an install nobody can enter.
        var store = app.Services.CreateScope().ServiceProvider
            .GetRequiredService<IIdentityProviderStore>();
        await store.SetEnabledAsync(provider, false, Guid.NewGuid(), CancellationToken.None);

        Assert.True((await client.GetFromJsonAsync<JsonElement>("/api/auth/methods"))
            .GetProperty("local").GetBoolean());
    }

    [Fact]
    public async Task Local_comes_back_when_the_provider_is_deleted_outright()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;
        var provider = await ProviderAsync(app, "corp");
        await ProveAsync(app, provider);
        (await SetMethodsAsync(client, local: false, oidc: true, saml: true)).EnsureSuccessStatusCode();

        (await client.DeleteAsync($"/api/admin/identity-providers/{provider}")).EnsureSuccessStatusCode();

        // "There is always a way in" is true by construction rather than only
        // at the moment somebody pressed save.
        Assert.True((await client.GetFromJsonAsync<JsonElement>("/api/auth/methods"))
            .GetProperty("local").GetBoolean());
    }

    [Fact]
    public async Task The_stored_configuration_is_not_rewritten_when_local_is_kept_for_reachability()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;
        var provider = await ProviderAsync(app, "corp");
        await ProveAsync(app, provider);
        (await SetMethodsAsync(client, local: false, oidc: true, saml: true)).EnsureSuccessStatusCode();

        var store = app.Services.CreateScope().ServiceProvider
            .GetRequiredService<IIdentityProviderStore>();
        await store.SetEnabledAsync(provider, false, Guid.NewGuid(), CancellationToken.None);

        // Local is available, and the administrator's intent is still on record.
        // Rewriting it would mean an operator who fixes their provider finds
        // their SSO-only configuration silently reverted.
        Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/admin/sign-in-methods"))
            .GetProperty("local").GetBoolean());

        await store.SetEnabledAsync(provider, true, Guid.NewGuid(), CancellationToken.None);
        Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/auth/methods"))
            .GetProperty("local").GetBoolean());
    }

    [Fact]
    public async Task The_first_administrator_can_sign_in_even_if_settings_say_local_is_off()
    {
        // The interaction most likely to be missed, and the one where a guard
        // produces the lockout it exists to prevent: a database whose settings
        // disable local sign-in but which has no working federation and no
        // users. The bootstrap creates an administrator; that administrator has
        // to be able to get in.
        var (seed, seedClient) = await BuildAsync();
        await using var _ = seed;
        var provider = await ProviderAsync(seed, "corp");
        await ProveAsync(seed, provider);
        (await SetMethodsAsync(seedClient, local: false, oidc: true, saml: true)).EnsureSuccessStatusCode();

        // Remove what justified it, leaving the stored "local off" behind — the
        // shape of a settings restore without the matching providers.
        (await seedClient.DeleteAsync($"/api/admin/identity-providers/{provider}")).EnsureSuccessStatusCode();

        await using var restarted = AutoNateWebApplicationFactory.CreateOn(seed.Database);
        var client = restarted.CreateClient(new Microsoft.AspNetCore.Mvc.Testing
            .WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await PostLoginAsync(client, "admin", "admin");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain(
            "method_disabled", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "error", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    // ── Break glass ─────────────────────────────────────────────────────────

    [Fact]
    public async Task The_override_forces_local_on_over_stored_configuration()
    {
        var (app, client) = await BuildAsync();
        // Disposed last: this factory owns the database, and dropping it out
        // from under the second host is not a restart, it is a deletion.
        await using var _ = app;

        var provider = await ProviderAsync(app, "corp");
        await ProveAsync(app, provider);
        (await SetMethodsAsync(client, local: false, oidc: true, saml: true)).EnsureSuccessStatusCode();

        // A second host over the same database — the shape of a restart with the
        // escape hatch set.
        await using var restarted = AutoNateWebApplicationFactory.CreateOn(
            app.Database,
            new Dictionary<string, string?> { [SignInMethodPolicy.OverrideVariable] = "true" });
        var restartedClient = restarted.CreateClient();

        var methods = await restartedClient.GetFromJsonAsync<JsonElement>("/api/auth/methods");
        Assert.True(methods.GetProperty("local").GetBoolean());

        // And the stored configuration is untouched — the override overrules it
        // for this process, it does not rewrite it. An operator who forgets to
        // unset the variable should find their SSO-only configuration intact,
        // not silently reverted.
        var stored = await restartedClient.GetFromJsonAsync<JsonElement>("/api/admin/sign-in-methods");
        Assert.False(stored.GetProperty("local").GetBoolean());
        Assert.True(stored.GetProperty("overrideActive").GetBoolean());
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("please")]
    public void Anything_but_a_clear_negative_activates_the_override(string value)
    {
        // Deliberately lenient. An operator setting this during an incident has
        // typed something meaning "yes", and a strict parse that rejected their
        // spelling would leave them locked out believing they had fixed it. The
        // failure mode of reading it too eagerly is a login form that should
        // have been hidden; of reading it too strictly, an install nobody can
        // enter.
        Assert.True(SignInMethodPolicy.IsOverrideSet(Config(value)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("no")]
    [InlineData("off")]
    [InlineData("")]
    [InlineData(null)]
    public void A_clear_negative_leaves_the_override_off(string? value)
    {
        Assert.False(SignInMethodPolicy.IsOverrideSet(Config(value)));
    }

    private static IConfiguration Config(string? value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SignInMethodPolicy.OverrideVariable] = value,
            })
            .Build();
}
