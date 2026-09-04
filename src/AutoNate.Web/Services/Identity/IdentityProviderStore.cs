using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Identity;

public interface IIdentityProviderStore
{
    Task<IReadOnlyList<IdentityProviderDto>> ListAsync(CancellationToken ct);

    Task<IdentityProviderDto?> GetAsync(Guid id, CancellationToken ct);

    Task<IdentityProviderDto> CreateAsync(CreateIdentityProviderRequest request, Guid actorId, CancellationToken ct);

    Task<IdentityProviderDto?> UpdateAsync(Guid id, UpdateIdentityProviderRequest request, Guid actorId, CancellationToken ct);

    Task<IdentityProviderDto?> SetEnabledAsync(Guid id, bool enabled, Guid actorId, CancellationToken ct);

    Task<bool> DeleteAsync(Guid id, Guid actorId, CancellationToken ct);

    /// <summary>Reveals a provider's secret for the sign-in flows. Never for an endpoint.</summary>
    Task<string?> RevealSecretAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Reads a SAML provider's stored IdP metadata document.
    /// </summary>
    /// <remarks>
    /// Not on the DTO. The document is public — it is what an IdP publishes —
    /// but it is a multi-kilobyte XML blob, and putting it on the record every
    /// list call materialises would make the admin list pay for it. The sign-in
    /// flow is the only caller that needs the text, so the text is fetched only
    /// there; the DTO carries <c>HasSamlMetadataXml</c> for the admin UI.
    /// </remarks>
    Task<string?> GetSamlMetadataXmlAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Marks that someone has just signed in successfully through this provider.
    /// </summary>
    /// <remarks>
    /// #94's lockout guard reads this: local sign-in cannot be switched off
    /// until a federated provider has actually worked. Recorded here rather
    /// than derived from audit events, which are retained on their own schedule
    /// — a guard that silently weakens when old events age out would still
    /// answer, and eventually answer wrongly.
    /// </remarks>
    Task RecordSuccessfulSignInAsync(Guid id, DateTime whenUtc, CancellationToken ct);
}

/// <summary>
/// Validation failure that endpoints turn into a 400 rather than a 500.
/// </summary>
public sealed class IdentityProviderValidationException(string message) : Exception(message);

public sealed class EfCoreIdentityProviderStore : IIdentityProviderStore
{
    private readonly IDbContextFactory<AutoNateDbContext> _factory;
    private readonly IIdentityProviderSecretProtector _protector;
    private readonly IAuditEventPublisher _audit;

    public EfCoreIdentityProviderStore(
        IDbContextFactory<AutoNateDbContext> factory,
        IIdentityProviderSecretProtector protector,
        IAuditEventPublisher audit)
    {
        _factory = factory;
        _protector = protector;
        _audit = audit;
    }

    public async Task<IReadOnlyList<IdentityProviderDto>> ListAsync(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.IdentityProviders
            .OrderBy(p => p.DisplayName)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<IdentityProviderDto?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.IdentityProviders.FirstOrDefaultAsync(p => p.Id == id, ct);
        return row is null ? null : ToDto(row);
    }

    public async Task<IdentityProviderDto> CreateAsync(
        CreateIdentityProviderRequest request, Guid actorId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kind = Normalise(request.Kind);
        if (!IdentityProviderKinds.IsKnown(kind))
        {
            throw new IdentityProviderValidationException(
                $"Unknown identity provider kind '{request.Kind}'. Expected 'oidc' or 'saml'.");
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new IdentityProviderValidationException("A display name is required — it is what appears on the login page.");
        }

        var slug = Slugify(request.Slug ?? request.DisplayName);
        if (slug.Length == 0)
        {
            throw new IdentityProviderValidationException(
                "A slug is required and must contain at least one letter or digit; it appears in the provider's callback path.");
        }

        await using var db = await _factory.CreateDbContextAsync(ct);

        if (await db.IdentityProviders.AnyAsync(p => p.Slug.ToLower() == slug, ct))
        {
            throw new IdentityProviderValidationException(
                $"An identity provider with the slug '{slug}' already exists. Slugs appear in callback paths, so they must be unique.");
        }

        var now = DateTime.UtcNow;
        var row = new IdentityProviderModel
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            DisplayName = request.DisplayName.Trim(),
            Slug = slug,
            // A new provider starts disabled unless asked otherwise. Creating a
            // route into the system should be two deliberate steps, not one.
            IsEnabled = request.IsEnabled ?? false,
            OidcAuthority = Trim(request.OidcAuthority),
            OidcClientId = Trim(request.OidcClientId),
            OidcScopes = Trim(request.OidcScopes),
            SamlEntityId = Trim(request.SamlEntityId),
            SamlMetadataUrl = Trim(request.SamlMetadataUrl),
            SamlMetadataXml = Trim(request.SamlMetadataXml),
            SamlSigningCertificate = Trim(request.SamlSigningCertificate),
            CreatedAtUtc = now,
            CreatedBy = actorId,
            UpdatedAtUtc = now,
            UpdatedBy = actorId,
        };

        ApplySecret(row, request.Secret);

        db.IdentityProviders.Add(row);
        await db.SaveChangesAsync(ct);

        await PublishAsync(IdentityProviderEventTypes.Created, row,
            new { hasSecret = row.SecretCiphertext is not null }, ct);

        return ToDto(row);
    }

    public async Task<IdentityProviderDto?> UpdateAsync(
        Guid id, UpdateIdentityProviderRequest request, Guid actorId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.IdentityProviders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return null;

        if (request.DisplayName is not null)
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                throw new IdentityProviderValidationException("A display name is required.");
            }
            row.DisplayName = request.DisplayName.Trim();
        }

        if (request.OidcAuthority is not null) row.OidcAuthority = Trim(request.OidcAuthority);
        if (request.OidcClientId is not null) row.OidcClientId = Trim(request.OidcClientId);
        if (request.OidcScopes is not null) row.OidcScopes = Trim(request.OidcScopes);
        if (request.SamlEntityId is not null) row.SamlEntityId = Trim(request.SamlEntityId);
        if (request.SamlMetadataUrl is not null) row.SamlMetadataUrl = Trim(request.SamlMetadataUrl);
        if (request.SamlMetadataXml is not null) row.SamlMetadataXml = Trim(request.SamlMetadataXml);
        if (request.SamlSigningCertificate is not null) row.SamlSigningCertificate = Trim(request.SamlSigningCertificate);

        // Null means "not supplied" and leaves the stored secret alone. A PATCH
        // that omits the field must not wipe it.
        var secretChanged = request.Secret is not null;
        if (secretChanged) ApplySecret(row, request.Secret);

        row.UpdatedAtUtc = DateTime.UtcNow;
        row.UpdatedBy = actorId;
        await db.SaveChangesAsync(ct);

        await PublishAsync(IdentityProviderEventTypes.Updated, row, new { secretChanged }, ct);
        return ToDto(row);
    }

    public async Task<IdentityProviderDto?> SetEnabledAsync(Guid id, bool enabled, Guid actorId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.IdentityProviders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return null;

        row.IsEnabled = enabled;
        row.UpdatedAtUtc = DateTime.UtcNow;
        row.UpdatedBy = actorId;
        await db.SaveChangesAsync(ct);

        // Its own event type rather than an Updated with a diff: enabling a
        // provider changes who can get into the system, and that should be
        // greppable in an audit log without reading payloads.
        await PublishAsync(
            enabled ? IdentityProviderEventTypes.Enabled : IdentityProviderEventTypes.Disabled,
            row, null, ct);

        return ToDto(row);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.IdentityProviders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return false;

        db.IdentityProviders.Remove(row);
        await db.SaveChangesAsync(ct);

        // Captured pre-delete so a consumer can identify what was removed.
        await PublishAsync(IdentityProviderEventTypes.Deleted, row, new { actorId }, ct);
        return true;
    }

    public async Task<string?> RevealSecretAsync(Guid id, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.IdentityProviders.FirstOrDefaultAsync(p => p.Id == id, ct);
        return row?.SecretCiphertext is null ? null : _protector.Reveal(row.SecretCiphertext);
    }

    public async Task RecordSuccessfulSignInAsync(Guid id, DateTime whenUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.IdentityProviders
            .Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastSuccessfulSignInAtUtc, whenUtc), ct);
    }

    public async Task<string?> GetSamlMetadataXmlAsync(Guid id, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.IdentityProviders
            .Where(p => p.Id == id)
            .Select(p => p.SamlMetadataXml)
            .FirstOrDefaultAsync(ct);
    }

    private void ApplySecret(IdentityProviderModel row, string? secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            // An explicitly empty secret clears it.
            row.SecretCiphertext = null;
            row.SecretFingerprint = null;
            return;
        }

        row.SecretCiphertext = _protector.Protect(secret);
        row.SecretFingerprint = _protector.Fingerprint(secret);
    }

    private Task PublishAsync(string eventType, IdentityProviderModel row, object? details, CancellationToken ct) =>
        _audit.PublishAsync(
            IdentityProviderEventTopic.TopicName,
            eventType,
            IdentityProviderEventTopic.ResourceKind,
            // Fingerprint, never the secret.
            resource: new { id = row.Id, kind = row.Kind, displayName = row.DisplayName, slug = row.Slug, secretFingerprint = row.SecretFingerprint },
            details: details,
            ct);

    private static IdentityProviderDto ToDto(IdentityProviderModel r) => new(
        r.Id, r.Kind, r.DisplayName, r.Slug, r.IsEnabled,
        r.OidcAuthority, r.OidcClientId, r.OidcScopes,
        r.SamlEntityId, r.SamlMetadataUrl,
        HasSamlMetadataXml: !string.IsNullOrWhiteSpace(r.SamlMetadataXml),
        r.SamlSigningCertificate,
        HasSecret: r.SecretCiphertext is not null,
        r.SecretFingerprint,
        r.LastSuccessfulSignInAtUtc,
        r.CreatedAtUtc, r.UpdatedAtUtc);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Normalise(string? kind) => (kind ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Lowercase, alphanumerics and single hyphens — it goes in a URL path.</summary>
    private static string Slugify(string input)
    {
        var chars = input.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return slug.Trim('-');
    }
}
