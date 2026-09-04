using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// OIDC and SAML against real Keycloak (#99).
/// </summary>
/// <remarks>
/// #90 and #93 test against stubs I wrote, which proves the implementation
/// matches my reading of the specifications — and agrees with the code by
/// construction, because the same reading produced both. Signature
/// canonicalization, NameID formats, attribute encodings and metadata quirks are
/// where that agreement stops being worth anything, and they are exactly where
/// SAML integrations fail in production.
///
/// The stubs are not replaced. They own the rejection matrix — minting an
/// unsigned assertion or a replay is easy against a stub and impractical against
/// real software. These specs own the opposite claim: that it works against
/// software somebody else wrote.
///
/// Traited <c>RequiresService=Keycloak</c> at class level, so a spec added here
/// inherits CI's exclusion rather than relying on someone remembering it.
/// </remarks>
[Collection(AutoNateE2ECollection.Name)]
[Trait("RequiresService", "Keycloak")]
public sealed class IdentityProviderInteropTests : E2ETestBase
{
    private const string Slug = "keycloak-e2e";

    public IdentityProviderInteropTests(AutoNateE2EFixture fixture) : base(fixture)
    {
    }

    // ── The two journeys ────────────────────────────────────────────────────

    [Fact]
    public async Task Oidc_sign_in_against_real_keycloak_lands_with_a_working_session()
    {
        await using var realm = await RequireKeycloakAsync();
        await realm.ConfigureOidcClientAsync(Fixture.BaseUrl);
        await ConfigureProviderAsync(realm, oidc: true);

        await using var session = await NewAnonymousSessionAsync();
        var page = session.Page;

        await SignInThroughKeycloakAsync(page, "oidc");

        // The session, not the landing page. #98's demo showed a landing URL can
        // lie: the account existed, the redirect looked right, and there was no
        // session behind it.
        var me = await ReadMeAsync(page);
        Assert.True(me.GetProperty("authenticated").GetBoolean());
        Assert.Equal($"oidc:{Slug}", me.GetProperty("authSource").GetString());
        Assert.StartsWith($"{Slug}:", me.GetProperty("idpKey").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saml_sign_in_against_real_keycloak_lands_with_a_working_session()
    {
        await using var realm = await RequireKeycloakAsync();
        await realm.CreateSamlClientAsync(Fixture.BaseUrl, Slug);
        await ConfigureProviderAsync(realm, oidc: false);

        await using var session = await NewAnonymousSessionAsync();
        var page = session.Page;

        await SignInThroughKeycloakAsync(page, "saml");

        // The half where interop actually breaks: a real IdP's signature
        // canonicalization, NameID format and attribute encoding, none of which
        // a stub I wrote could have disagreed with me about.
        var me = await ReadMeAsync(page);
        Assert.True(me.GetProperty("authenticated").GetBoolean());
        Assert.Equal($"saml:{Slug}", me.GetProperty("authSource").GetString());
    }

    // ── Claim mapping, through both protocols ───────────────────────────────

    [Theory]
    [InlineData("oidc")]
    [InlineData("saml")]
    public async Task A_mapped_group_is_granted_and_revoked_through_either_protocol(string protocol)
    {
        await using var realm = await RequireKeycloakAsync();
        await ConfigureClientAsync(realm, protocol);
        var providerId = await ConfigureProviderAsync(realm, oidc: protocol == "oidc");

        // A group of Auton8's own, mapped to the claim Keycloak actually sends.
        // #92 is asserted through both protocols rather than assumed to behave
        // the same: the group arrives as an OIDC claim one way and a SAML
        // attribute the other, and only one of those was ever exercised against
        // real software.
        var groupName = "Interop " + Guid.NewGuid().ToString("N")[..6];
        var groupId = await CreateGroupAndMappingAsync(providerId, groupName);

        var alice = await realm.UserIdAsync(KeycloakRealm.AliceUsername);
        var engineering = await realm.GroupIdAsync(KeycloakRealm.EngineeringGroup);

        await using (var session = await NewAnonymousSessionAsync())
        {
            await SignInThroughKeycloakAsync(session.Page, protocol);
            var me = await ReadMeAsync(session.Page);
            Assert.Contains(
                groupName,
                me.GetProperty("groups").EnumerateArray()
                    .Select(g => g.GetProperty("name").GetString()),
                StringComparer.Ordinal);
        }

        // The direction a first implementation silently never does. Removing
        // someone from a group at the identity provider has to remove their
        // access here, or federation is a one-way ratchet on the day it matters.
        await realm.RemoveFromGroupAsync(alice, engineering);
        try
        {
            await using var session = await NewAnonymousSessionAsync();
            await SignInThroughKeycloakAsync(session.Page, protocol);
            var me = await ReadMeAsync(session.Page);
            Assert.DoesNotContain(
                groupName,
                me.GetProperty("groups").EnumerateArray()
                    .Select(g => g.GetProperty("name").GetString()),
                StringComparer.Ordinal);
        }
        finally
        {
            // Put the realm back even if the assertion failed — a spec that
            // leaves shared state changed is how the next one passes wrongly.
            await realm.AddToGroupAsync(alice, engineering);
        }
    }

    // ── Account matching, against real tokens ───────────────────────────────

    [Fact]
    public async Task A_changed_email_at_the_idp_still_resolves_to_one_account()
    {
        await using var realm = await RequireKeycloakAsync();
        await realm.ConfigureOidcClientAsync(Fixture.BaseUrl);
        await ConfigureProviderAsync(realm, oidc: true);

        var alice = await realm.UserIdAsync(KeycloakRealm.AliceUsername);
        var original = $"alice@auton8.local";

        string firstUserId;
        await using (var session = await NewAnonymousSessionAsync())
        {
            await SignInThroughKeycloakAsync(session.Page, "oidc");
            firstUserId = (await ReadMeAsync(session.Page)).GetProperty("userId").GetString()!;
        }

        await realm.SetEmailAsync(alice, "alice.renamed@auton8.local");
        try
        {
            await using var session = await NewAnonymousSessionAsync();
            await SignInThroughKeycloakAsync(session.Page, "oidc");
            var second = await ReadMeAsync(session.Page);

            // Matching on the subject, not the email. Keying on email would give
            // somebody who changed their address a second, role-less account and
            // silently strand everything the first one had.
            Assert.Equal(firstUserId, second.GetProperty("userId").GetString());
        }
        finally
        {
            await realm.SetEmailAsync(alice, original);
        }
    }

    // ── Plumbing ────────────────────────────────────────────────────────────

    private static async Task<KeycloakRealm> RequireKeycloakAsync()
    {
        var realm = await KeycloakRealm.ConnectAsync();

        // Fail rather than silently pass when Keycloak is absent. A spec that
        // green-ticks itself because the thing it tests was unreachable is worse
        // than no spec — CI excludes these by trait, so reaching this line at
        // all means someone ran them locally without the profile up.
        Assert.True(realm is not null,
            "Keycloak is not reachable. Run `make keycloak-up`. These specs carry "
            + "RequiresService=Keycloak, so CI skips them by filter and never reaches this.");
        return realm!;
    }

    private async Task ConfigureClientAsync(KeycloakRealm realm, string protocol)
    {
        if (protocol == "oidc")
        {
            await realm.ConfigureOidcClientAsync(Fixture.BaseUrl);
        }
        else
        {
            await realm.CreateSamlClientAsync(Fixture.BaseUrl, Slug);
        }
    }

    /// <summary>
    /// Configures the Auton8 side through the admin API.
    /// </summary>
    /// <remarks>
    /// Through the API rather than seeded directly, so the specs exercise #87's
    /// real validation. A row inserted straight into the table could be one the
    /// API would have refused, and the specs would prove interop for a
    /// configuration nobody can actually create.
    /// </remarks>
    private async Task<string> ConfigureProviderAsync(KeycloakRealm realm, bool oidc)
    {
        await using var admin = await NewSignedInAsAdminAsync();

        var existing = await admin.Page.APIRequest.GetAsync("/api/admin/identity-providers");
        var rows = JsonDocument.Parse(await existing.TextAsync()).RootElement;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.GetProperty("slug").GetString() == Slug)
            {
                await admin.Page.APIRequest.DeleteAsync(
                    $"/api/admin/identity-providers/{row.GetProperty("id").GetString()}");
            }
        }

        var response = await admin.Page.APIRequest.PostAsync(
            "/api/admin/identity-providers",
            new APIRequestContextOptions
            {
                DataObject = oidc
                    ? new Dictionary<string, object?>
                    {
                        ["kind"] = "oidc",
                        ["displayName"] = "Keycloak",
                        ["slug"] = Slug,
                        ["isEnabled"] = true,
                        ["oidcAuthority"] = realm.Issuer,
                        ["oidcClientId"] = KeycloakRealm.OidcClientId,
                    }
                    : new Dictionary<string, object?>
                    {
                        ["kind"] = "saml",
                        ["displayName"] = "Keycloak",
                        ["slug"] = Slug,
                        ["isEnabled"] = true,
                        ["samlEntityId"] = realm.Issuer,
                        // Pasted, not fetched — see KeycloakRealm.SamlDescriptorXmlAsync
                        // and #137. Same document, same real Keycloak.
                        ["samlMetadataXml"] = await realm.SamlDescriptorXmlAsync(),
                    },
            });

        Assert.True(response.Ok, $"Creating the provider failed: {await response.TextAsync()}");
        return JsonDocument.Parse(await response.TextAsync()).RootElement
            .GetProperty("id").GetString()!;
    }

    private async Task<string> CreateGroupAndMappingAsync(string providerId, string groupName)
    {
        await using var admin = await NewSignedInAsAdminAsync();

        var group = await admin.Page.APIRequest.PostAsync("/api/admin/groups",
            new APIRequestContextOptions { DataObject = new { name = groupName, description = (string?)null } });
        Assert.True(group.Ok, await group.TextAsync());
        var groupId = JsonDocument.Parse(await group.TextAsync()).RootElement
            .GetProperty("id").GetString()!;

        var mapping = await admin.Page.APIRequest.PostAsync(
            $"/api/admin/identity-providers/{providerId}/group-mappings",
            new APIRequestContextOptions
            {
                DataObject = new
                {
                    claimType = "groups",
                    claimValue = KeycloakRealm.EngineeringGroup,
                    groupId,
                },
            });
        Assert.True(mapping.Ok, await mapping.TextAsync());

        return groupId;
    }

    /// <summary>Drives the login page through Keycloak and back.</summary>
    private static async Task SignInThroughKeycloakAsync(IPage page, string protocol)
    {
        await page.GotoAsync($"/api/auth/{protocol}/{Slug}/challenge?returnUrl=%2Fhome");

        // Report where we actually landed rather than timing out on a selector.
        // A challenge that refuses returns a redirect to /?error=..., and
        // "waiting for Username or email" says nothing about which of the half
        // dozen reasons it was.
        if (!page.Url.Contains("/realms/", StringComparison.Ordinal))
        {
            Assert.Fail(
                $"The {protocol} challenge did not reach Keycloak. Landed on {page.Url}. "
                + "A '?error=provider_unavailable' here means the provider could not build a "
                + "configuration — for SAML that usually means the IdP metadata could not be "
                + "fetched or parsed, so no single sign-on destination was found.");
        }

        await page.GetByRole(AriaRole.Textbox, new() { Name = "Username or email" })
            .FillAsync(KeycloakRealm.AliceUsername);
        await page.Locator("input[type='password']").FillAsync(KeycloakRealm.AlicePassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign In" }).ClickAsync();

        await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"/(home|\?error=)"));
        Assert.DoesNotContain("error=", page.Url, StringComparison.Ordinal);
    }

    private static async Task<JsonElement> ReadMeAsync(IPage page)
    {
        var response = await page.APIRequest.GetAsync("/api/auth/me");
        Assert.True(response.Ok);
        return JsonDocument.Parse(await response.TextAsync()).RootElement;
    }
}
