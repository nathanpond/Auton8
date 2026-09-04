using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.ExternalConnections;
using AutoNate.Web.Services.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// Tests for identity-provider configuration (#87).
/// </summary>
/// <remarks>
/// The security-critical half of this story is negative: a secret that goes in
/// must never come back out, and the new DataProtection purpose must actually
/// be a different key rather than a differently-spelled label. Both are tested
/// by trying to get the secret back rather than by inspecting the code.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class IdentityProviderEndpointsTests
{
    private const string Secret = "super-secret-oidc-client-secret-4f2b";

    [Fact]
    public async Task Nothing_is_seeded_a_fresh_database_has_no_identity_providers()
    {
        // Project invariant 1: configuring nothing creates nothing. An install
        // with no provider must behave exactly as it does today.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/admin/identity-providers");
        response.EnsureSuccessStatusCode();

        var rows = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, rows.GetArrayLength());
    }

    // There is deliberately no "unauthenticated caller is refused" test here.
    // AutoNateWebApplicationFactory enables the Development auto-login
    // middleware so authenticated endpoints are reachable, which makes an
    // unauthenticated request unrepresentable through this factory. That
    // property is covered where it can be checked honestly:
    // AuthorizationGatePresenceTests asserts every route carries an explicit
    // authorization decision, and KindGateEnforcementTests asserts each is
    // wired to the right (kind, action) pair — project invariant 3.

    [Fact]
    public async Task The_secret_never_round_trips_through_any_read_endpoint()
    {
        // The regression this guards is a DTO gaining the field later, so it
        // checks the raw response text rather than a typed property: a new
        // property called anything at all would still be caught.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var id = await CreateProviderAsync(client, "oidc-secret-probe");

        foreach (var route in new[]
                 {
                     "/api/admin/identity-providers",
                     $"/api/admin/identity-providers/{id}",
                 })
        {
            var body = await (await client.GetAsync(route)).Content.ReadAsStringAsync();
            Assert.DoesNotContain(Secret, body, StringComparison.Ordinal);
        }

        // And the create response itself.
        var detail = await (await client.GetAsync($"/api/admin/identity-providers/{id}"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(detail.GetProperty("hasSecret").GetBoolean(), "The admin screen needs to know one is set.");
        var fingerprint = detail.GetProperty("secretFingerprint").GetString();
        Assert.False(string.IsNullOrWhiteSpace(fingerprint));
        Assert.DoesNotContain(Secret, fingerprint!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_secret_is_encrypted_at_rest_and_round_trips_through_the_protector()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        var id = await CreateProviderAsync(client, "oidc-at-rest");

        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.IdentityProviders.SingleAsync(p => p.Id == id);

        Assert.NotNull(row.SecretCiphertext);
        // The stored bytes must not be the plaintext.
        var stored = System.Text.Encoding.UTF8.GetString(row.SecretCiphertext!);
        Assert.DoesNotContain(Secret, stored, StringComparison.Ordinal);

        var protector = factory.Services.GetRequiredService<IIdentityProviderSecretProtector>();
        Assert.Equal(Secret, protector.Reveal(row.SecretCiphertext!));
    }

    [Fact]
    public void The_identity_purpose_cannot_decrypt_an_external_connections_payload_or_the_reverse()
    {
        // The point of a separate purpose, and the thing that would silently
        // NOT hold if the purpose string had been copied. A DataProtection
        // purpose is part of key derivation, so two protectors with different
        // purposes derive different keys and neither can read the other.
        var provider = DataProtectionProvider.Create(nameof(IdentityProviderEndpointsTests));

        var identity = new DataProtectionIdentityProviderSecretProtector(provider);
        var external = new DataProtectionConnectionSecretProtector(provider);

        var identityBlob = identity.Protect(Secret);
        var externalBlob = external.Protect(Secret);

        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => external.Reveal(identityBlob));
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => identity.Reveal(externalBlob));

        // Each still reads its own.
        Assert.Equal(Secret, identity.Reveal(identityBlob));
        Assert.Equal(Secret, external.Reveal(externalBlob));
    }

    [Fact]
    public async Task A_patch_that_omits_the_secret_leaves_it_alone()
    {
        // Distinguishing "not supplied" from "cleared" — a PATCH that edits the
        // display name must not silently wipe the secret.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        var id = await CreateProviderAsync(client, "oidc-patch-probe");

        var patch = await client.PatchAsJsonAsync(
            $"/api/admin/identity-providers/{id}", new { displayName = "Renamed" });
        patch.EnsureSuccessStatusCode();

        var detail = await (await client.GetAsync($"/api/admin/identity-providers/{id}"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Renamed", detail.GetProperty("displayName").GetString());
        Assert.True(detail.GetProperty("hasSecret").GetBoolean(), "Omitting the secret must not clear it.");
    }

    [Fact]
    public async Task Enable_and_disable_are_distinct_audited_actions()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        var id = await CreateProviderAsync(client, "oidc-enable-probe");
        factory.RecordedAuditEvents.Clear();

        (await client.PostAsync($"/api/admin/identity-providers/{id}/enable", null)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/admin/identity-providers/{id}/disable", null)).EnsureSuccessStatusCode();

        var types = factory.RecordedAuditEvents.Events.Select(e => e.EventType).ToList();
        Assert.Contains(IdentityProviderEventTypes.Enabled, types);
        Assert.Contains(IdentityProviderEventTypes.Disabled, types);
    }

    [Fact]
    public async Task A_duplicate_slug_is_refused_with_a_reason()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await CreateProviderAsync(client, "duplicate-slug");

        var second = await client.PostAsJsonAsync("/api/admin/identity-providers", new
        {
            kind = "oidc",
            displayName = "Another",
            slug = "duplicate-slug",
            oidcAuthority = "https://idp.example.com",
            oidcClientId = "client",
        });

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        // Slugs appear in callback paths — the message should say so rather
        // than just "conflict".
        Assert.Contains("slug", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_unknown_kind_is_refused()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var response = await client.PostAsJsonAsync("/api/admin/identity-providers", new
        {
            kind = "ldap",
            displayName = "Nope",
            slug = "nope",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Establishes the session cookie.
    /// </summary>
    /// <remarks>
    /// The factory's Development auto-login middleware only activates on GET,
    /// so a POST from a fresh client has no actor and the handler refuses it.
    /// One GET first puts the cookie in the client's container, which every
    /// later request carries. The neighbouring ExternalConnection suite does
    /// the same thing for the same reason.
    /// </remarks>
    private static async Task PrimeAuthAsync(HttpClient client)
    {
        (await client.GetAsync("/api/admin/identity-providers")).EnsureSuccessStatusCode();
    }

    private static async Task<Guid> CreateProviderAsync(HttpClient client, string slug)
    {
        await PrimeAuthAsync(client);
        var response = await client.PostAsJsonAsync("/api/admin/identity-providers", new
        {
            kind = "oidc",
            displayName = "Corporate SSO",
            slug,
            oidcAuthority = "https://idp.example.com",
            oidcClientId = "auton8",
            secret = Secret,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("id").GetGuid();
    }
}
