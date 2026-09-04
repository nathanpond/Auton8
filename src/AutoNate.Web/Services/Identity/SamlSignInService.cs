using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using ITfoxtec.Identity.Saml2.Schemas.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace AutoNate.Web.Services.Identity;

/// <summary>Why a SAML assertion was refused.</summary>
/// <remarks>
/// #93 is blunt about the stakes: "a SAML implementation that accepts one of
/// these is a bypass, not a bug." Each cause is separate so an administrator can
/// tell clock skew from a signature problem from a replay — three different
/// problems with three different fixes, and a single "authentication failed"
/// guarantees nobody can tell which they have.
/// </remarks>
public enum SamlFailureReason
{
    None = 0,
    ProviderNotFound,
    ProviderMisconfigured,
    MalformedResponse,
    IssuerMismatch,
    StatusNotSuccess,
    SignatureInvalid,
    Replayed,
    NotYetValid,
    Expired,
    AudienceMismatch,
    DestinationMismatch,
    SubjectMissing,
}

public sealed record SamlSignInResult(
    bool Succeeded,
    LocalUser? User,
    bool AccountCreated,
    IReadOnlyDictionary<string, string[]> Attributes,
    SamlFailureReason Reason,
    string? Diagnostic)
{
    /// <summary>The provider this sign-in went through, once one was found.</summary>
    /// <remarks>
    /// Carried so #92's reconciliation is scoped to the provider the user
    /// actually signed in through: two providers configured against one Auton8
    /// must not be able to revoke each other's grants.
    /// </remarks>
    public Guid ProviderId { get; init; }
}

public interface ISamlSignInService
{
    Task<string?> BuildAuthnRequestUrlAsync(
        string slug, string acsUri, string entityId, string relayState, CancellationToken ct);

    Task<string?> BuildMetadataAsync(string slug, string acsUri, string entityId, CancellationToken ct);

    Task<SamlSignInResult> CompleteAsync(
        string slug, string samlResponseBase64, string acsUri, string entityId, CancellationToken ct);
}

/// <summary>
/// SP-initiated SAML sign-in, driven from database-stored providers.
/// </summary>
/// <remarks>
/// The shape follows #86: ITfoxtec is endpoint-driven, so
/// <c>Saml2Configuration</c> is built per request from a provider row rather
/// than registered at startup — the same reason #95's answer for OIDC was to own
/// the flow.
///
/// SAML is the harder half because the assertion arrives as signed XML by
/// browser POST rather than over a back channel. There is no TLS-protected
/// exchange to lean on: the signature is the only thing between a login page and
/// anyone who can post a form.
///
/// Where the library already implements a check, it does it — signature
/// validation, audience restriction and replay detection all run inside
/// ITfoxtec's <c>Unbind</c> via <see cref="Saml2Configuration"/>, not in a
/// parallel path that could disagree about which assertions were accepted. What
/// this class adds is the checks the library leaves to the caller: destination,
/// an explicit clock-skew window, and the subject.
/// </remarks>
public sealed class SamlSignInService : ISamlSignInService
{
    /// <summary>Tolerance for clock difference between the IdP and this host.</summary>
    /// <remarks>
    /// Explicit and small. #93 names skew as the most common cause of false
    /// rejections in production and an unstated default as what makes it hard to
    /// diagnose, so the number is here, named, and quoted in the rejection
    /// message. Three minutes covers ordinary NTP drift; a wider window is a
    /// longer replay opportunity.
    /// </remarks>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(3);

    private readonly IIdentityProviderStore _providers;
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly ISamlMetadataCache _metadata;
    private readonly ITokenReplayCache _replayCache;
    private readonly TimeProvider _clock;
    private readonly ILogger<SamlSignInService> _log;

    public SamlSignInService(
        IIdentityProviderStore providers,
        IDbContextFactory<AutoNateDbContext> dbFactory,
        ISamlMetadataCache metadata,
        ITokenReplayCache replayCache,
        TimeProvider clock,
        ILogger<SamlSignInService> log)
    {
        _providers = providers;
        _dbFactory = dbFactory;
        _metadata = metadata;
        _replayCache = replayCache;
        _clock = clock;
        _log = log;
    }

    public async Task<string?> BuildAuthnRequestUrlAsync(
        string slug, string acsUri, string entityId, string relayState, CancellationToken ct)
    {
        var provider = await FindEnabledAsync(slug, ct);
        if (provider is null) return null;

        Saml2Configuration config;
        try
        {
            config = await BuildConfigurationAsync(provider, entityId, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "SAML challenge failed for {Slug}: configuration could not be built.", slug);
            return null;
        }

        if (config.SingleSignOnDestination is null)
        {
            _log.LogWarning(
                "SAML challenge failed for {Slug}: no single sign-on destination. Configure the IdP's "
                + "metadata (URL or pasted XML) so the destination can be read from it.", slug);
            return null;
        }

        var request = new Saml2AuthnRequest(config)
        {
            AssertionConsumerServiceUrl = new Uri(acsUri),
            NameIdPolicy = new NameIdPolicy { AllowCreate = true },
        };

        // Deliberately unsigned. Signing an AuthnRequest needs an SP key pair,
        // which #87 does not store — the same key management that puts encrypted
        // assertions out of this story's scope. An unsigned AuthnRequest is
        // ordinary and accepted by mainstream IdPs, and the security of this flow
        // rests on validating the *response*, which is where the leverage is.
        //
        // RelayState carries the post-sign-in destination out to the IdP and
        // back, because the ACS is reached by a cross-site POST that no
        // SameSite=Lax cookie survives. The IdP echoes it verbatim, so it is
        // caller-controlled by the time it returns and the endpoint re-filters
        // it there rather than trusting the round trip.
        var binding = new Saml2RedirectBinding { RelayState = relayState };
        binding.Bind(request);
        return binding.RedirectLocation.OriginalString;
    }

    public async Task<string?> BuildMetadataAsync(
        string slug, string acsUri, string entityId, CancellationToken ct)
    {
        var provider = await FindEnabledAsync(slug, ct);
        if (provider is null) return null;

        var config = new Saml2Configuration { Issuer = entityId };
        var metadata = new EntityDescriptor(config)
        {
            ValidUntil = 365,
            SPSsoDescriptor = new SPSsoDescriptor
            {
                // No SP certificate is published because none exists — see the
                // deferred encrypted-assertion slice. Publishing a placeholder
                // an IdP might encrypt to would be worse than publishing none.
                WantAssertionsSigned = true,
                AssertionConsumerServices =
                [
                    new AssertionConsumerService { Binding = ProtocolBindings.HttpPost, Location = new Uri(acsUri) },
                ],
                NameIDFormats = [NameIdentifierFormats.Persistent],
            },
        };

        return metadata.ToXmlDocument().OuterXml;
    }

    public async Task<SamlSignInResult> CompleteAsync(
        string slug, string samlResponseBase64, string acsUri, string entityId, CancellationToken ct)
    {
        var provider = await FindEnabledAsync(slug, ct);
        if (provider is null)
        {
            return Fail(SamlFailureReason.ProviderNotFound, $"No enabled SAML provider with slug '{slug}'.");
        }

        Saml2Configuration config;
        try
        {
            config = await BuildConfigurationAsync(provider, entityId, ct);
        }
        catch (Exception ex)
        {
            return Fail(SamlFailureReason.ProviderMisconfigured,
                $"The provider's SAML configuration could not be built: {ex.Message}");
        }

        if (config.SignatureValidationCertificates.Count == 0)
        {
            // Fail closed. An implementation that reads "no key configured" as
            // "skip the signature check" is the bypass this story exists to
            // prevent, and it is an easy one to write by accident.
            return Fail(SamlFailureReason.ProviderMisconfigured,
                "No signing certificate is configured for this provider, so no assertion can be validated. "
                + "Configure the IdP's certificate, or its metadata, before enabling it.");
        }

        var response = new Saml2AuthnResponse(config);
        var httpRequest = new ITfoxtec.Identity.Saml2.Http.HttpRequest
        {
            Method = "POST",
            Form = new System.Collections.Specialized.NameValueCollection
            {
                { "SAMLResponse", samlResponseBase64 },
            },
        };

        var binding = new Saml2PostBinding();
        try
        {
            // Parses without validating, so the Status can be read: a response
            // that reports a failure carries no assertion, and Unbind — which
            // insists on exactly one — would report it as a malformed document
            // instead of as the refusal it is.
            //
            // This stage does run the library's condition checks (lifetime and
            // audience), so those two can be reported from here rather than from
            // Unbind, and therefore before the signature has been checked. That
            // is why every failure goes through one mapper: the reason must be
            // the same whichever stage noticed it. Replay detection is off here
            // — ITfoxtec passes detectReplayedTokens: false — so nothing
            // unvalidated can reach the replay store.
            binding.ReadSamlResponse(httpRequest, response);
        }
        catch (Exception ex)
        {
            return Interpret(ex, slug, entityId, SamlFailureReason.MalformedResponse);
        }

        if (response.Status != Saml2StatusCodes.Success)
        {
            return Fail(SamlFailureReason.StatusNotSuccess,
                $"The identity provider returned status '{response.Status}' rather than Success"
                + (string.IsNullOrEmpty(response.StatusMessage) ? "." : $": {response.StatusMessage}"));
        }

        // Unbind repeats the parse with validation on: the XML signature against
        // the provider's certificate, the audience restriction, and replay
        // detection, all configured above.
        try
        {
            binding.Unbind(httpRequest, response);
        }
        catch (Exception ex)
        {
            return Interpret(ex, slug, entityId, SamlFailureReason.SignatureInvalid);
        }

        // Destination: an assertion minted for another service must not be usable
        // here even though its signature is perfectly valid — it was signed by
        // the same IdP, for a different SP.
        //
        // Required, not merely checked when present. SAML Core §3.2.2 says the
        // attribute MUST be present on a signed response, and this service
        // refuses unsigned ones — so treating an absent Destination as "nothing
        // to compare" would turn the check into one an attacker skips by
        // deleting an attribute.
        if (response.Destination is null)
        {
            return Fail(SamlFailureReason.DestinationMismatch,
                "The response carries no Destination. A signed SAML response must name the endpoint "
                + "it was minted for, and without it there is nothing to stop an assertion issued for "
                + "another service being presented here.");
        }

        if (!string.Equals(
                    response.Destination.GetLeftPart(UriPartial.Path),
                    new Uri(acsUri).GetLeftPart(UriPartial.Path),
                    StringComparison.OrdinalIgnoreCase))
        {
            return Fail(SamlFailureReason.DestinationMismatch,
                $"The assertion's Destination '{response.Destination}' is not this service's assertion "
                + $"consumer '{acsUri}' — it was minted for a different service.");
        }

        // The validity window again, at this service's own tolerance.
        //
        // Not redundant with the library's check: ITfoxtec builds its
        // TokenValidationParameters internally and exposes no way to set
        // ClockSkew, so the library runs at Microsoft's 5-minute default.
        // Auton8 states its own, narrower, three minutes — so the effective
        // tolerance is three, whichever check happens to fire first, and the
        // number a diagnosing administrator reads is the number that applied.
        var now = _clock.GetUtcNow().UtcDateTime;
        var validFrom = response.SecurityTokenValidFrom.UtcDateTime;
        var validTo = response.SecurityTokenValidTo.UtcDateTime;

        if (validFrom != default && now + ClockSkew < validFrom)
        {
            return Fail(SamlFailureReason.NotYetValid,
                NotYetValidMessage($"it is valid from {validFrom:O}, and it is now {now:O}."));
        }

        if (validTo != default && now - ClockSkew >= validTo)
        {
            return Fail(SamlFailureReason.Expired,
                ExpiredMessage($"it expired at {validTo:O}, and it is now {now:O}."));
        }

        var claims = response.ClaimsIdentity?.Claims?.ToList() ?? [];
        var subject = claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            return Fail(SamlFailureReason.SubjectMissing,
                "The assertion carried no NameID, so there is no stable identifier to key the account on.");
        }

        var attributes = claims
            .GroupBy(c => c.Type, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Value).ToArray(), StringComparer.Ordinal);

        var (user, created) = await MapToLocalUserAsync(provider, subject!, attributes, ct);
        return new SamlSignInResult(true, user, created, attributes, SamlFailureReason.None, null)
        {
            ProviderId = provider.Id,
        };
    }

    /// <summary>
    /// Turns a validation exception into the reason an administrator can act on.
    /// </summary>
    /// <remarks>
    /// By exception <em>type</em>, not by message: the type is what the
    /// libraries guarantee. Microsoft's Saml2SecurityTokenHandler throws the
    /// SecurityToken* family for lifetime, audience and replay; ITfoxtec throws
    /// its own for everything structural.
    ///
    /// Mapping matters more than it looks. If clock skew or a replay surfaced as
    /// "signature invalid", an administrator would go and check certificates —
    /// which are fine — and never find the real fault. #93 names precisely that
    /// as the failure mode to avoid, which is why <paramref name="fallback"/> is
    /// the last resort rather than the common case.
    /// </remarks>
    private SamlSignInResult Interpret(
        Exception ex, string slug, string entityId, SamlFailureReason fallback) => ex switch
    {
        SecurityTokenReplayDetectedException or SecurityTokenReplayAddFailedException =>
            Fail(SamlFailureReason.Replayed,
                "This assertion has already been consumed within its validity window. A second "
                + $"presentation of the same assertion is a replay, and is refused. ({ex.Message})"),

        SecurityTokenInvalidAudienceException =>
            Fail(SamlFailureReason.AudienceMismatch,
                $"The assertion's audience does not include this service provider '{entityId}'. "
                + $"({ex.Message})"),

        SecurityTokenExpiredException => Fail(SamlFailureReason.Expired, ExpiredMessage(ex.Message)),

        SecurityTokenNotYetValidException =>
            Fail(SamlFailureReason.NotYetValid, NotYetValidMessage(ex.Message)),

        // The subject-confirmation deadline, which the library checks against
        // wall-clock with no tolerance of its own. Still an expiry, and reported
        // as one.
        Saml2RequestException when ex.Message.Contains("expired", StringComparison.OrdinalIgnoreCase) =>
            Fail(SamlFailureReason.Expired, ExpiredMessage(ex.Message)),

        // Signed by someone, but not by the provider this slug names. Its own
        // reason, because "could not be parsed" would send an administrator
        // hunting a malformed document that is in fact well formed.
        Saml2RequestException when ex.Message.Contains("Invalid Issuer", StringComparison.OrdinalIgnoreCase) =>
            Fail(SamlFailureReason.IssuerMismatch,
                $"The response was issued by a different identity provider than '{slug}' expects. "
                + $"({ex.Message})"),

        // A missing Subject or SubjectConfirmation element: structurally
        // incomplete rather than cryptographically wrong.
        Saml2RequestException when ex.Message.Contains("Not Found", StringComparison.OrdinalIgnoreCase) =>
            Fail(SamlFailureReason.MalformedResponse,
                $"The assertion is missing a required element: {ex.Message}"),

        _ when fallback == SamlFailureReason.SignatureInvalid =>
            Fail(SamlFailureReason.SignatureInvalid,
                $"The assertion failed validation against the provider's signing certificate: {ex.Message}"),

        _ => Fail(fallback, $"The SAMLResponse could not be read: {ex.Message}"),
    };

    private static string ExpiredMessage(string detail) =>
        $"The assertion is no longer valid: {detail} Tolerance for clock difference is "
        + $"{ClockSkew.TotalMinutes:N0} minutes. If this recurs for users signing in promptly, the "
        + "identity provider's clock and this host's are out of step.";

    private static string NotYetValidMessage(string detail) =>
        $"The assertion is not valid yet: {detail} Tolerance for clock difference is "
        + $"{ClockSkew.TotalMinutes:N0} minutes. If this recurs, the identity provider's clock is ahead "
        + "of this host's.";

    /// <summary>
    /// Finds or provisions the local account, identically to the OIDC path.
    /// </summary>
    /// <remarks>
    /// The same rules as #90 rather than a parallel set: keyed on
    /// <c>{slug}:{subject}</c>, never email, and created with no role
    /// assignments. Two federation paths that provision differently is how one
    /// of them ends up being the lenient one.
    /// </remarks>
    private async Task<(LocalUser User, bool Created)> MapToLocalUserAsync(
        IdentityProviderDto provider, string subject,
        IReadOnlyDictionary<string, string[]> attributes, CancellationToken ct)
    {
        var idpKey = $"{provider.Slug}:{subject}";

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.LocalUsers.FirstOrDefaultAsync(u => u.IdpKey == idpKey, ct);
        if (existing is not null)
        {
            existing.LastLoginDate = _clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
            return (PersistenceModelMapper.ToModel(existing), false);
        }

        var email = First(attributes, ClaimTypes.Email) ?? $"{subject}@{provider.Slug}.invalid";
        var now = _clock.GetUtcNow().UtcDateTime;

        var row = new Persistence.Scaffolded.LocalUser
        {
            UserId = Guid.NewGuid(),
            Username = await UniqueUsernameAsync(db, First(attributes, ClaimTypes.Name) ?? email, ct),
            Email = email,
            FirstName = First(attributes, ClaimTypes.GivenName) ?? string.Empty,
            LastName = First(attributes, ClaimTypes.Surname) ?? string.Empty,
            IdpKey = idpKey,
            // Empty, not random: there is no plaintext that produces an empty
            // hash, so the local password path cannot authenticate this account
            // even by accident.
            PasswordHash = string.Empty,
            PasswordSalt = string.Empty,
            CreatedDate = now,
            LastLoginDate = now,
            FailedLoginAttempts = 0,
            IsLocked = false,
        };

        db.LocalUsers.Add(row);
        await db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Created a federated account for SAML provider {Slug} subject {Subject} with no role assignments.",
            provider.Slug, subject);

        return (PersistenceModelMapper.ToModel(row), true);
    }

    private static string? First(IReadOnlyDictionary<string, string[]> attributes, string key) =>
        attributes.TryGetValue(key, out var v) && v.Length > 0 && v[0].Length > 0 ? v[0] : null;

    private static async Task<string> UniqueUsernameAsync(AutoNateDbContext db, string desired, CancellationToken ct)
    {
        var candidate = desired;
        var suffix = 1;
        while (await db.LocalUsers.AnyAsync(u => u.Username == candidate, ct))
        {
            candidate = $"{desired}-{++suffix}";
        }
        return candidate;
    }

    private async Task<Saml2Configuration> BuildConfigurationAsync(
        IdentityProviderDto provider, string entityId, CancellationToken ct) =>
        BuildConfiguration(
            provider,
            await _providers.GetSamlMetadataXmlAsync(provider.Id, ct),
            provider.HasSamlMetadataXml
                ? null
                : await _metadata.GetAsync(provider.SamlMetadataUrl ?? string.Empty, ct),
            entityId,
            _replayCache);

    /// <summary>Builds the per-request SAML configuration from a provider row.</summary>
    /// <remarks>
    /// Separated from the fetching so it is a pure function of its inputs, which
    /// is what lets a test assert that a metadata document and hand-entered
    /// values produce the same configuration.
    /// </remarks>
    internal static Saml2Configuration BuildConfiguration(
        IdentityProviderDto provider, string? metadataXml, EntityDescriptor? fetched, string entityId,
        ITokenReplayCache? replayCache = null)
    {
        var config = new Saml2Configuration
        {
            Issuer = entityId,
            RevocationMode = X509RevocationMode.NoCheck,
            CertificateValidationMode = System.ServiceModel.Security.X509CertificateValidationMode.None,

            // Audience and replay are the library's job, configured here rather
            // than re-implemented alongside it.
            AudienceRestricted = true,
            DetectReplayedTokens = true,
            TokenReplayCache = replayCache,
        };
        config.AllowedAudienceUris.Add(entityId);

        // Metadata is preferred over hand-entered values, per the AC: an
        // administrator hands over a document rather than transcribing a
        // certificate. Pasted XML wins over a fetched URL — it is the more
        // deliberate act, and it is what an administrator reaches for when the
        // fetched document is wrong.
        EntityDescriptor? entity = null;
        if (!string.IsNullOrWhiteSpace(metadataXml))
        {
            entity = new EntityDescriptor();
            entity.ReadIdPSsoDescriptor(metadataXml);
        }
        else if (fetched is not null)
        {
            entity = fetched;
        }

        if (entity?.IdPSsoDescriptor is not null)
        {
            config.AllowedIssuer = entity.EntityId;
            var sso = entity.IdPSsoDescriptor.SingleSignOnServices?
                .FirstOrDefault(s => s.Binding == ProtocolBindings.HttpRedirect)
                ?? entity.IdPSsoDescriptor.SingleSignOnServices?.FirstOrDefault();
            if (sso is not null) config.SingleSignOnDestination = sso.Location;
            foreach (var cert in entity.IdPSsoDescriptor.SigningCertificates ?? [])
            {
                config.SignatureValidationCertificates.Add(cert);
            }
        }

        // A hand-entered certificate is additive rather than exclusive: an
        // administrator may supply one alongside metadata during a key rollover.
        if (!string.IsNullOrWhiteSpace(provider.SamlSigningCertificate))
        {
            config.SignatureValidationCertificates.Add(
                X509CertificateLoader.LoadCertificate(
                    Convert.FromBase64String(Normalise(provider.SamlSigningCertificate!))));
        }

        // An explicitly entered IdP entity ID wins over the one in the metadata
        // document: it is what the administrator typed, and the point of the
        // field is to pin the issuer when the document is ambiguous or shared.
        if (!string.IsNullOrWhiteSpace(provider.SamlEntityId))
        {
            config.AllowedIssuer = provider.SamlEntityId;
        }

        return config;
    }

    private static string Normalise(string pem) => pem
        .Replace("-----BEGIN CERTIFICATE-----", string.Empty, StringComparison.Ordinal)
        .Replace("-----END CERTIFICATE-----", string.Empty, StringComparison.Ordinal)
        .Replace("\r", string.Empty, StringComparison.Ordinal)
        .Replace("\n", string.Empty, StringComparison.Ordinal)
        .Trim();

    private async Task<IdentityProviderDto?> FindEnabledAsync(string slug, CancellationToken ct)
    {
        var all = await _providers.ListAsync(ct);
        return all.FirstOrDefault(p =>
            p.IsEnabled
            && p.Kind == IdentityProviderKinds.Saml
            && string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    private SamlSignInResult Fail(SamlFailureReason reason, string diagnostic)
    {
        _log.LogWarning("SAML sign-in rejected ({Reason}): {Diagnostic}", reason, diagnostic);
        return new SamlSignInResult(false, null, false, new Dictionary<string, string[]>(), reason, diagnostic);
    }
}
