using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Identity;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using ITfoxtec.Identity.Saml2.Schemas.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.Tokens.Saml2;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// SAML sign-in against a stub identity provider that really signs assertions.
/// </summary>
/// <remarks>
/// #93 puts it plainly: "a SAML implementation that accepts one of these is a
/// bypass, not a bug." A stub that returned a canned success would prove none of
/// that, so this one holds a real key pair and mints real signed SAML responses.
///
/// Every rejection test below produces an assertion that is **correct in every
/// respect except one**. That is what makes each of them a test of the specific
/// check rather than of the XML parser: if the audience test passed because the
/// document was malformed, it would tell us nothing about audience validation.
///
/// The stub deliberately keeps its <c>SubjectConfirmationData/@NotOnOrAfter</c>
/// genuinely in the future in every test, including the expiry ones. ITfoxtec
/// checks that attribute against wall-clock with no tolerance of its own, so
/// letting it lapse would make each lifetime test pass for the wrong reason —
/// the subject-confirmation deadline rather than the condition under test.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SamlSignInServiceTests
{
    private const string Slug = "corp";
    private const string Subject = "saml-subject-1";
    private const string IdpEntityId = "https://idp.example.com/saml";
    private const string AcsUri = "https://app.example.com/api/auth/saml/corp/acs";
    private const string SpEntityId = "https://app.example.com/api/auth/saml/corp/metadata";

    // ── The stub IdP ────────────────────────────────────────────────────────

    private sealed class StubIdp
    {
        public X509Certificate2 Certificate { get; }

        public StubIdp()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=stub-idp.example.com", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var ephemeral = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

            // Round-tripped through PKCS#12 so the private key is exportable.
            // A key bound to an ephemeral in-memory handle cannot be used by
            // SignedXml on every platform, and the resulting failure looks like
            // a signing bug rather than a key-storage one.
            const string Password = "stub";
            Certificate = X509CertificateLoader.LoadPkcs12(
                ephemeral.Export(X509ContentType.Pfx, Password),
                Password,
                X509KeyStorageFlags.Exportable);
        }

        /// <summary>Public half only — what an administrator pastes into Auton8.</summary>
        public string PublicCertificateBase64() =>
            Convert.ToBase64String(Certificate.Export(X509ContentType.Cert));

        /// <summary>
        /// Mints a SAML response, base64-encoded exactly as the IdP would post it.
        /// </summary>
        public string MintXml(
            string? subject = Subject,
            string email = "someone@example.com",
            string issuer = IdpEntityId,
            string audience = SpEntityId,
            string destination = AcsUri,
            Saml2StatusCodes status = Saml2StatusCodes.Success,
            DateTime? notBefore = null,
            DateTime? expires = null,
            bool sign = true,
            X509Certificate2? signWith = null)
        {
            var config = new Saml2Configuration
            {
                Issuer = issuer,
                SigningCertificate = sign ? signWith ?? Certificate : null,
                CertificateValidationMode = System.ServiceModel.Security.X509CertificateValidationMode.None,
                RevocationMode = X509RevocationMode.NoCheck,
            };

            var response = new Saml2AuthnResponse(config)
            {
                Status = status,
                Destination = new Uri(destination),
            };

            if (status == Saml2StatusCodes.Success)
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.Email, email),
                    new(ClaimTypes.Name, "someone"),
                    new(ClaimTypes.GivenName, "Some"),
                    new(ClaimTypes.Surname, "One"),
                };
                if (subject is not null)
                {
                    claims.Add(new Claim(ClaimTypes.NameIdentifier, subject));
                    response.NameId = new Saml2NameIdentifier(subject, NameIdentifierFormats.Persistent);
                }

                response.ClaimsIdentity = new ClaimsIdentity(claims);

                var now = DateTime.UtcNow;
                var descriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(claims),
                    Issuer = issuer,
                    Audience = audience,
                    NotBefore = notBefore ?? now.AddMinutes(-1),
                    Expires = expires ?? now.AddMinutes(30),
                };

                var authnStatement = new Saml2AuthenticationStatement(
                    new Saml2AuthenticationContext(AuthnContextClassTypes.PasswordProtectedTransport))
                {
                    SessionIndex = Guid.NewGuid().ToString("N"),
                };

                // Always comfortably in the future: see the class remarks.
                var confirmation = new Saml2SubjectConfirmation(
                    ITfoxtec.Identity.Saml2.Schemas.Saml2Constants.Saml2BearerToken,
                    new Saml2SubjectConfirmationData
                    {
                        Recipient = new Uri(destination),
                        NotOnOrAfter = DateTime.UtcNow.AddMinutes(30),
                    });

                response.CreateSecurityToken(descriptor, authnStatement, confirmation);
            }

            var binding = new Saml2PostBinding();
            binding.Bind(response);
            return binding.XmlDocument.OuterXml;
        }

        /// <summary>The IdP's own metadata document, as an administrator would paste it.</summary>
        public string MetadataXml(string ssoUrl = "https://idp.example.com/saml/sso")
        {
            var descriptor = new EntityDescriptor(
                new Saml2Configuration { Issuer = IdpEntityId }) { ValidUntil = 30 };
            descriptor.IdPSsoDescriptor = new IdPSsoDescriptor
            {
                SigningCertificates = [Certificate],
                SingleSignOnServices =
                [
                    new SingleSignOnService
                    {
                        Binding = ProtocolBindings.HttpRedirect,
                        Location = new Uri(ssoUrl),
                    },
                ],
                NameIDFormats = [NameIdentifierFormats.Persistent],
            };
            return descriptor.ToXmlDocument().OuterXml;
        }
    }

    private static string Encode(string xml) => Convert.ToBase64String(Encoding.UTF8.GetBytes(xml));

    // ── Harness ─────────────────────────────────────────────────────────────

    private static async Task<(SamlSignInService Service, AutoNateWebApplicationFactory App)> BuildAsync(
        StubIdp idp, bool enabled = true, bool withCertificate = true, string? idpEntityId = IdpEntityId,
        string? metadataXml = null)
    {
        var app = await AutoNateWebApplicationFactory.CreateAsync();
        _ = app.CreateClient();

        var store = app.Services.CreateScope().ServiceProvider
            .GetRequiredService<IIdentityProviderStore>();

        await store.CreateAsync(new CreateIdentityProviderRequest(
            Kind: IdentityProviderKinds.Saml,
            DisplayName: "Corporate SAML",
            Slug: Slug,
            IsEnabled: enabled,
            OidcAuthority: null, OidcClientId: null, OidcScopes: null,
            SamlEntityId: idpEntityId,
            SamlMetadataUrl: null,
            SamlMetadataXml: metadataXml,
            SamlSigningCertificate: withCertificate ? idp.PublicCertificateBase64() : null,
            Secret: null), Guid.NewGuid(), CancellationToken.None);

        var service = new SamlSignInService(
            store,
            app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>(),
            app.Services.GetRequiredService<ISamlMetadataCache>(),
            // One guard per test. A shared one would let a replay test's first
            // presentation poison an unrelated test's happy path.
            new SamlReplayGuard(TimeProvider.System),
            TimeProvider.System,
            NullLogger<SamlSignInService>.Instance);

        return (service, app);
    }

    private static Task<SamlSignInResult> CompleteAsync(SamlSignInService service, string samlResponse) =>
        service.CompleteAsync(Slug, samlResponse, AcsUri, SpEntityId, CancellationToken.None);

    // ── The happy path ──────────────────────────────────────────────────────

    [Fact]
    public async Task A_valid_assertion_creates_an_account_with_no_roles()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp);
        await using var _ = app;

        var result = await CompleteAsync(service, Encode(idp.MintXml()));

        Assert.True(result.Succeeded, result.Diagnostic);
        Assert.True(result.AccountCreated);
        Assert.NotNull(result.User);
        Assert.Equal($"{Slug}:{Subject}", result.User!.IdpKey);

        // The criterion the story exists to protect: a first federated sign-in
        // grants nothing. Asserted against the database, not inferred from the
        // absence of code that would have granted something.
        var dbFactory = app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var assignments = await db.RoleAssignments
            .CountAsync(a => a.PrincipalId == result.User.UserId.ToString());
        Assert.Equal(0, assignments);
    }

    [Fact]
    public async Task A_second_sign_in_reuses_the_same_account()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp);
        await using var _ = app;

        var first = await CompleteAsync(service, Encode(idp.MintXml()));
        var second = await CompleteAsync(service, Encode(idp.MintXml()));

        Assert.True(first.Succeeded, first.Diagnostic);
        Assert.True(second.Succeeded, second.Diagnostic);
        Assert.True(first.AccountCreated);
        Assert.False(second.AccountCreated);
        Assert.Equal(first.User!.UserId, second.User!.UserId);
    }

    [Fact]
    public async Task The_local_password_path_cannot_authenticate_a_federated_account()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp);
        await using var _ = app;

        var result = await CompleteAsync(service, Encode(idp.MintXml()));
        Assert.True(result.Succeeded, result.Diagnostic);

        var dbFactory = app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.LocalUsers.FirstAsync(u => u.UserId == result.User!.UserId);

        // Empty rather than random: there is no plaintext that hashes to an
        // empty string, so the local sign-in path cannot match this account even
        // by accident.
        Assert.Equal(string.Empty, row.PasswordHash);
        Assert.Equal(string.Empty, row.PasswordSalt);
    }

    // ── The rejections ──────────────────────────────────────────────────────

    [Fact]
    public async Task An_unsigned_assertion_is_refused()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp);
        await using var _ = app;

        var result = await CompleteAsync(service, Encode(idp.MintXml(sign: false)));

        Assert.False(result.Succeeded);
        Assert.Equal(SamlFailureReason.SignatureInvalid, result.Reason);
    }

    [Fact]
    public async Task An_assertion_signed_by_an_unknown_key_is_refused()
    {
        var idp = new StubIdp();
        var impostor = new StubIdp();
        var (service, app) = await BuildAsync(idp);
        await using var _ = app;

        // Signed, well formed, and correct in every other respect — by the wrong
        // key. This is the whole threat model of a browser-POST protocol.
        var result = await CompleteAsync(service, Encode(idp.MintXml(signWith: impostor.Certificate)));

        Assert.False(result.Succeeded);
        Assert.Equal(SamlFailureReason.SignatureInvalid, result.Reason);
    }

    [Fact]
    public async Task A_replayed_assertion_is_refused_the_second_time()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp);
        await using var _ = app;

        var assertion = Encode(idp.MintXml());

        var first = await CompleteAsync(service, assertion);
        var second = await CompleteAsync(service, assertion);

        Assert.True(first.Succeeded, first.Diagnostic);
        Assert.False(second.Succeeded);
        Assert.Equal(SamlFailureReason.Replayed, second.Reason);
    }

    [Fact]
    public async Task An_expired_assertion_is_refused()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp);
        await using var _ = app;

        var result = await CompleteAsync(service, Encode(idp.MintXml(
            notBefore: DateTime.UtcNow.AddMinutes(-30),
            expires: DateTime.UtcNow.AddMinutes(-10))));

        Assert.False(result.Succeeded);
        Assert.Equal(SamlFailureReason.Expired, result.Reason);
    }

    [Fact]
    public async Task An_assertion_from_the_future_is_refused()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp);
        await using var _ = app;

        var result = await CompleteAsync(service, Encode(idp.MintXml(
            notBefore: DateTime.UtcNow.AddMinutes(30),
            expires: DateTime.UtcNow.AddMinutes(60))));

        Assert.False(result.Succeeded);
        Assert.Equal(SamlFailureReason.NotYetValid, result.Reason);
    }

    [Fact]
    public async Task An_assertion_for_another_audience_is_refused()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp);
        await using var _ = app;

        // Correctly signed by the right IdP — for somebody else's service.
        var result = await CompleteAsync(service, Encode(idp.MintXml(
            audience: "https://other.example.com/api/auth/saml/corp/metadata")));

        Assert.False(result.Succeeded);
        Assert.Equal(SamlFailureReason.AudienceMismatch, result.Reason);
    }

    [Fact]
    public async Task An_assertion_addressed_elsewhere_is_refused()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp);
        await using var _ = app;

        var result = await CompleteAsync(service, Encode(idp.MintXml(
            destination: "https://app.example.com/api/auth/saml/other/acs")));

        Assert.False(result.Succeeded);
        Assert.Equal(SamlFailureReason.DestinationMismatch, result.Reason);
    }

    [Fact]
    public async Task An_assertion_with_no_destination_at_all_is_refused()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp);
        await using var _ = app;

        // Deleting the attribute must not be a way to skip the check. A signed
        // SAML response is required to carry one (SAML Core §3.2.2), and this
        // service refuses unsigned responses, so an absent Destination is a
        // defect and never a legitimate omission.
        var withoutDestination = System.Text.RegularExpressions.Regex.Replace(
            idp.MintXml(), " Destination=\"[^\"]*\"", string.Empty);

        var result = await CompleteAsync(service, Encode(withoutDestination));

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Reason,
            new[] { SamlFailureReason.DestinationMismatch, SamlFailureReason.SignatureInvalid });
    }

    [Fact]
    public async Task An_assertion_from_a_different_issuer_is_refused()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp);
        await using var _ = app;

        var result = await CompleteAsync(service, Encode(idp.MintXml(issuer: "https://elsewhere.example.com/saml")));

        Assert.False(result.Succeeded);
        Assert.Equal(SamlFailureReason.IssuerMismatch, result.Reason);
    }

    [Fact]
    public async Task An_assertion_with_no_subject_is_refused()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp);
        await using var _ = app;

        var result = await CompleteAsync(service, Encode(idp.MintXml(subject: null)));

        Assert.False(result.Succeeded);

        // Either reason is correct and both are safe: the library refuses an
        // assertion with no Subject element outright, and the service refuses one
        // whose NameID is absent. What must never happen is an account keyed on
        // nothing.
        Assert.Contains(
            result.Reason,
            new[] { SamlFailureReason.SubjectMissing, SamlFailureReason.MalformedResponse });
    }

    [Fact]
    public async Task A_non_success_status_is_refused()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp);
        await using var _ = app;

        var result = await CompleteAsync(service, Encode(idp.MintXml(status: Saml2StatusCodes.Responder)));

        Assert.False(result.Succeeded);
        Assert.Equal(SamlFailureReason.StatusNotSuccess, result.Reason);
    }

    [Fact]
    public async Task A_provider_with_no_signing_certificate_refuses_everything()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp, withCertificate: false);
        await using var _ = app;

        // Fails closed. The bypass this guards against is the natural-looking
        // implementation that reads "no key configured" as "nothing to check
        // against" and lets the assertion through.
        var result = await CompleteAsync(service, Encode(idp.MintXml()));

        Assert.False(result.Succeeded);
        Assert.Equal(SamlFailureReason.ProviderMisconfigured, result.Reason);
    }

    [Fact]
    public async Task A_disabled_provider_accepts_nothing()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp, enabled: false);
        await using var _ = app;

        var result = await CompleteAsync(service, Encode(idp.MintXml()));

        Assert.False(result.Succeeded);
        Assert.Equal(SamlFailureReason.ProviderNotFound, result.Reason);
    }

    [Fact]
    public async Task A_malformed_response_is_refused()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp);
        await using var _ = app;

        var result = await CompleteAsync(service, Convert.ToBase64String(
            Encoding.UTF8.GetBytes("<not-a-saml-response/>")));

        Assert.False(result.Succeeded);
        Assert.Equal(SamlFailureReason.MalformedResponse, result.Reason);
    }

    // ── Clock skew, at both edges ───────────────────────────────────────────

    [Fact]
    public async Task An_assertion_just_inside_the_skew_window_is_accepted()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp);
        await using var _ = app;

        // Expired two minutes ago, against a three-minute tolerance. This is the
        // ordinary case of an IdP whose clock runs slightly ahead, and refusing
        // it would produce intermittent sign-in failures nobody can reproduce.
        var justExpired = await CompleteAsync(service, Encode(idp.MintXml(
            notBefore: DateTime.UtcNow.AddMinutes(-30),
            expires: DateTime.UtcNow.AddMinutes(-2))));
        Assert.True(justExpired.Succeeded, justExpired.Diagnostic);

        // Valid from two minutes in the future, same tolerance.
        var justEarly = await CompleteAsync(service, Encode(idp.MintXml(
            notBefore: DateTime.UtcNow.AddMinutes(2),
            expires: DateTime.UtcNow.AddMinutes(30))));
        Assert.True(justEarly.Succeeded, justEarly.Diagnostic);
    }

    [Fact]
    public async Task An_assertion_just_outside_the_skew_window_is_refused()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp);
        await using var _ = app;

        // Four minutes either side of a three-minute tolerance. Deliberately
        // inside Microsoft's five-minute TokenValidationParameters default,
        // which ITfoxtec gives no way to change — so these two assertions pass
        // the library's own lifetime check and are refused by this service's
        // narrower one. That is what makes three minutes the number that
        // actually applies, rather than a comment claiming it does.
        var tooLate = await CompleteAsync(service, Encode(idp.MintXml(
            notBefore: DateTime.UtcNow.AddMinutes(-30),
            expires: DateTime.UtcNow.AddMinutes(-4))));
        Assert.False(tooLate.Succeeded);
        Assert.Equal(SamlFailureReason.Expired, tooLate.Reason);

        var tooEarly = await CompleteAsync(service, Encode(idp.MintXml(
            notBefore: DateTime.UtcNow.AddMinutes(4),
            expires: DateTime.UtcNow.AddMinutes(30))));
        Assert.False(tooEarly.Succeeded);
        Assert.Equal(SamlFailureReason.NotYetValid, tooEarly.Reason);
    }

    [Fact]
    public void The_stated_clock_skew_is_narrower_than_the_library_default()
    {
        // The comment in SamlSignInService explains that this service's window
        // binds because it is tighter than Microsoft's five-minute default. If
        // someone widens it past five minutes that reasoning silently inverts
        // and the library becomes the binding check, so the relationship is
        // pinned rather than described.
        Assert.True(
            SamlSignInService.ClockSkew < new TokenValidationParameters().ClockSkew,
            "SamlSignInService.ClockSkew must stay narrower than TokenValidationParameters.ClockSkew, "
            + "or the effective tolerance is the library's rather than the one Auton8 reports.");
    }

    [Fact]
    public async Task An_assertion_edited_after_signing_is_refused()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp);
        await using var _ = app;

        // The signature is genuine and the document is well formed — but the
        // subject was changed after the IdP signed it. This is the attack the
        // whole protocol turns on: if a signature that no longer covers the
        // assertion's contents were accepted, anyone holding one valid assertion
        // could sign in as anyone.
        var tampered = idp.MintXml().Replace(Subject, "somebody-else", StringComparison.Ordinal);

        var result = await CompleteAsync(service, Encode(tampered));

        Assert.False(result.Succeeded);
        Assert.Equal(SamlFailureReason.SignatureInvalid, result.Reason);
    }

    [Fact]
    public async Task A_changed_email_does_not_fork_the_account()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp);
        await using var _ = app;

        var first = await CompleteAsync(service, Encode(idp.MintXml()));

        // Same subject, different email — someone changed their address at the
        // IdP, and the IdP signs the new assertion perfectly well. Matching on
        // email rather than on the subject would silently give them a second,
        // role-less account and lose everything the first one had.
        var second = await CompleteAsync(
            service, Encode(idp.MintXml(email: "someone.else@example.com")));

        Assert.True(first.Succeeded, first.Diagnostic);
        Assert.True(second.Succeeded, second.Diagnostic);
        Assert.False(second.AccountCreated);
        Assert.Equal(first.User!.UserId, second.User!.UserId);
    }

    // ── Metadata as a configuration source ──────────────────────────────────

    [Fact]
    public async Task Metadata_and_hand_entered_values_produce_the_same_configuration()
    {
        var idp = new StubIdp();
        const string SsoUrl = "https://idp.example.com/saml/sso";

        // Configured two ways: an administrator pasting the IdP's metadata
        // document, and one transcribing the entity ID and certificate by hand.
        // The AC offers both, so the two have to agree — a metadata path that
        // quietly produced a laxer configuration would be the more attractive
        // one to use and the worse one to have.
        var (fromMetadata, appA) = await BuildAsync(
            idp, withCertificate: false, idpEntityId: null, metadataXml: idp.MetadataXml(SsoUrl));
        await using var _a = appA;

        var (byHand, appB) = await BuildAsync(idp);
        await using var _b = appB;

        var viaMetadata = await CompleteAsync(fromMetadata, Encode(idp.MintXml()));
        var viaFields = await CompleteAsync(byHand, Encode(idp.MintXml()));

        Assert.True(viaMetadata.Succeeded, viaMetadata.Diagnostic);
        Assert.True(viaFields.Succeeded, viaFields.Diagnostic);
        Assert.Equal(viaFields.User!.IdpKey, viaMetadata.User!.IdpKey);

        // And the same document supplies the sign-on destination, which the
        // hand-entered form has no field for at all.
        var challenge = await fromMetadata.BuildAuthnRequestUrlAsync(
            Slug, AcsUri, SpEntityId, "/home", CancellationToken.None);
        Assert.NotNull(challenge);
        Assert.StartsWith(SsoUrl, challenge, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Metadata_supplies_the_signing_certificate_without_transcription()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(
            idp, withCertificate: false, idpEntityId: null, metadataXml: idp.MetadataXml());
        await using var _ = app;

        // No certificate field filled in — only the pasted document. An
        // assertion signed by the key that document names is accepted, and one
        // signed by any other key is not.
        var genuine = await CompleteAsync(service, Encode(idp.MintXml()));
        Assert.True(genuine.Succeeded, genuine.Diagnostic);

        var impostor = new StubIdp();
        var forged = await CompleteAsync(service, Encode(idp.MintXml(signWith: impostor.Certificate)));
        Assert.False(forged.Succeeded);
        Assert.Equal(SamlFailureReason.SignatureInvalid, forged.Reason);
    }

    [Fact]
    public async Task A_provider_with_no_sign_on_destination_issues_no_challenge()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp);
        await using var _ = app;

        // Configured by hand, so nothing says where the IdP's sign-on endpoint
        // is. Better a challenge that does not start than a redirect to
        // somewhere arbitrary.
        Assert.Null(await service.BuildAuthnRequestUrlAsync(
            Slug, AcsUri, SpEntityId, "/home", CancellationToken.None));
    }

    [Fact]
    public async Task The_challenge_carries_the_return_path_as_relay_state()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(
            idp, withCertificate: false, idpEntityId: null, metadataXml: idp.MetadataXml());
        await using var _ = app;

        var challenge = await service.BuildAuthnRequestUrlAsync(
            Slug, AcsUri, SpEntityId, "/records/42", CancellationToken.None);

        // RelayState rather than a cookie: the assertion comes back by
        // cross-site POST, which no SameSite=Lax cookie survives.
        Assert.NotNull(challenge);
        Assert.Contains("RelayState=", challenge, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("/records/42"), challenge!, StringComparison.Ordinal);
    }

    // ── The session has to survive the request after the callback ───────────

    [Fact]
    public async Task A_federated_session_still_authenticates_on_the_next_request()
    {
        // The assertion this suite was missing, and the one that matters most:
        // not "did we call SignInAsync" but "is the user actually signed in".
        //
        // The gap between those two hid a defect for as long as federated
        // sign-in has existed. The Development auto-login middleware decided
        // which sessions to keep with an allow-list — `manual` and its own — and
        // signed out everything else. That was indistinguishable from correct
        // while those were the only two authentication sources; #90 and #93
        // added `oidc:{slug}` and `saml:{slug}`, and every federated session was
        // destroyed by the next GET. Account created, nothing logged, user
        // bounced back to the login page.
        //
        // So this drives the real ACS endpoint over HTTP, takes the cookie it
        // issues, and makes a SECOND request with it. A test that stops at the
        // sign-in call cannot see this class of bug at all.
        var idp = new StubIdp();
        var app = await AutoNateWebApplicationFactory.CreateAsync();
        await using var _ = app;

        var client = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing
            .WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        // The endpoint derives both from the request host, so the assertion has
        // to be minted for what the test client will actually send.
        var acs = $"http://localhost/api/auth/saml/{Slug}/acs";
        var audience = $"http://localhost/api/auth/saml/{Slug}/metadata";

        var store = app.Services.CreateScope().ServiceProvider
            .GetRequiredService<IIdentityProviderStore>();
        await store.CreateAsync(new CreateIdentityProviderRequest(
            Kind: IdentityProviderKinds.Saml,
            DisplayName: "Corporate SAML",
            Slug: Slug,
            IsEnabled: true,
            OidcAuthority: null, OidcClientId: null, OidcScopes: null,
            SamlEntityId: IdpEntityId,
            SamlMetadataUrl: null, SamlMetadataXml: null,
            SamlSigningCertificate: idp.PublicCertificateBase64(),
            Secret: null), Guid.NewGuid(), CancellationToken.None);

        var response = await client.PostAsync(acs, new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>(
                "SAMLResponse",
                Encode(idp.MintXml(destination: acs, audience: audience))),
        ]));

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain(
            "error", response.Headers.Location!.OriginalString, StringComparison.Ordinal);

        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie")
                .Where(c => c.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal)));

        var second = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        second.Headers.Add("Cookie", cookie.Split(';')[0]);
        var me = await client.SendAsync(second);

        me.EnsureSuccessStatusCode();
        var body = await me.Content.ReadAsStringAsync();

        // Asserting on WHO is signed in, not merely that somebody is.
        //
        // "authenticated: true" alone passes even with the defect present,
        // because Development auto-login immediately signs the request back in
        // as `admin` — the federated session is destroyed and replaced, and the
        // weaker assertion cannot tell the difference. That is precisely how a
        // regression test comes to pass against the bug it was written for.
        Assert.Contains($"\"authSource\":\"saml:{Slug}\"", body, StringComparison.Ordinal);
        Assert.Contains($"\"idpKey\":\"{Slug}:{Subject}\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"username\":\"admin\"", body, StringComparison.Ordinal);
    }

    // ── Metadata and the challenge ──────────────────────────────────────────

    [Fact]
    public async Task The_metadata_document_advertises_this_service_as_the_consumer()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp);
        await using var _ = app;

        var xml = await service.BuildMetadataAsync(Slug, AcsUri, SpEntityId, CancellationToken.None);

        Assert.NotNull(xml);
        Assert.Contains(SpEntityId, xml, StringComparison.Ordinal);
        Assert.Contains(AcsUri, xml, StringComparison.Ordinal);
        Assert.Contains("urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_disabled_provider_publishes_no_metadata()
    {
        var idp = new StubIdp();
        var (service, app) = await BuildAsync(idp, enabled: false);
        await using var _ = app;

        Assert.Null(await service.BuildMetadataAsync(Slug, AcsUri, SpEntityId, CancellationToken.None));
    }
}
