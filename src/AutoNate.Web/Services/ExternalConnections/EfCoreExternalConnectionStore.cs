using System.Text.Json;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.ExternalConnections;

public sealed class EfCoreExternalConnectionStore : IExternalConnectionStore
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbContextFactory;
    private readonly IConnectionSecretProtector _secretProtector;
    private readonly IAuditEventPublisher _auditPublisher;

    public EfCoreExternalConnectionStore(
        IDbContextFactory<AutoNateDbContext> dbContextFactory,
        IConnectionSecretProtector secretProtector,
        IAuditEventPublisher auditPublisher)
    {
        _dbContextFactory = dbContextFactory;
        _secretProtector = secretProtector;
        _auditPublisher = auditPublisher;
    }

    public async Task<IReadOnlyList<ExternalConnectionRow>> ListAsync(
        string? kind,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = dbContext.ExternalConnections.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(kind))
        {
            query = query.Where(c => c.Kind == kind);
        }

        var rows = await query
            .OrderByDescending(c => c.IsDefault)
            .ThenBy(c => c.Kind)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        await _auditPublisher.PublishAsync(
            ExternalConnectionEventTopic.TopicName,
            ExternalConnectionEventTypes.ListViewed,
            ExternalConnectionEventTopic.ResourceKind,
            resource: null,
            details: new { kind, count = rows.Count },
            cancellationToken);

        return rows.Select(ToRow).ToList();
    }

    public async Task<ExternalConnectionRow?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.ExternalConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (entity is null) return null;

        await _auditPublisher.PublishAsync(
            ExternalConnectionEventTopic.TopicName,
            ExternalConnectionEventTypes.Viewed,
            ExternalConnectionEventTopic.ResourceKind,
            resource: ResourceFor(entity),
            details: null,
            cancellationToken);

        return ToRow(entity);
    }

    public async Task<ExternalConnectionRow> CreateAsync(
        CreateExternalConnectionInput input,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Kind)) throw new InvalidOperationException("Kind is required.");
        if (string.IsNullOrWhiteSpace(input.Name)) throw new InvalidOperationException("Name is required.");

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var entity = new ExternalConnection
        {
            Id = Guid.NewGuid(),
            Kind = input.Kind.Trim(),
            Name = input.Name.Trim(),
            Description = input.Description,
            IsEnabled = input.IsEnabled,
            IsDefault = false,
            MetadataJson = SerializeMetadata(input.Metadata),
            CreatedAtUtc = now,
            CreatedBy = actorId,
            UpdatedAtUtc = now,
            UpdatedBy = actorId
        };

        if (!string.IsNullOrEmpty(input.Secret))
        {
            entity.SecretCiphertext = _secretProtector.Protect(input.Secret);
            entity.SecretFingerprint = _secretProtector.Fingerprint(input.Secret);
        }

        dbContext.ExternalConnections.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await _auditPublisher.PublishAsync(
            ExternalConnectionEventTopic.TopicName,
            ExternalConnectionEventTypes.Created,
            ExternalConnectionEventTopic.ResourceKind,
            resource: ResourceFor(entity),
            details: new { hasSecret = entity.SecretCiphertext is not null },
            cancellationToken);

        return ToRow(entity);
    }

    public async Task<ExternalConnectionRow?> UpdateAsync(
        Guid id,
        UpdateExternalConnectionInput input,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.ExternalConnections.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (entity is null) return null;

        if (input.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(input.Name)) throw new InvalidOperationException("Name cannot be empty.");
            entity.Name = input.Name.Trim();
        }
        if (input.Description is not null) entity.Description = input.Description;
        if (input.IsEnabled is bool enabled) entity.IsEnabled = enabled;
        if (input.Metadata is JsonElement metadata) entity.MetadataJson = SerializeMetadata(metadata);

        var secretChanged = input.Secret is not null;
        if (input.Secret is { Length: 0 })
        {
            entity.SecretCiphertext = null;
            entity.SecretFingerprint = null;
        }
        else if (input.Secret is { Length: > 0 } secret)
        {
            entity.SecretCiphertext = _secretProtector.Protect(secret);
            entity.SecretFingerprint = _secretProtector.Fingerprint(secret);
        }

        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = actorId;

        await dbContext.SaveChangesAsync(cancellationToken);

        await _auditPublisher.PublishAsync(
            ExternalConnectionEventTopic.TopicName,
            ExternalConnectionEventTypes.Updated,
            ExternalConnectionEventTopic.ResourceKind,
            resource: ResourceFor(entity),
            details: new { secretChanged },
            cancellationToken);

        return ToRow(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.ExternalConnections.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (entity is null) return false;

        var resource = ResourceFor(entity);

        dbContext.ExternalConnections.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await _auditPublisher.PublishAsync(
            ExternalConnectionEventTopic.TopicName,
            ExternalConnectionEventTypes.Deleted,
            ExternalConnectionEventTopic.ResourceKind,
            resource: resource,
            details: new { actorId },
            cancellationToken);

        return true;
    }

    public async Task<ExternalConnectionRow?> SetDefaultAsync(
        Guid id,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.ExternalConnections.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (entity is null) return null;

        // Atomic swap inside a transaction so the partial unique index never
        // sees two defaults for the same kind concurrently.
        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.ExternalConnections
            .Where(c => c.Kind == entity.Kind && c.IsDefault && c.Id != entity.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDefault, false), cancellationToken);

        entity.IsDefault = true;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = actorId;
        await dbContext.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        await _auditPublisher.PublishAsync(
            ExternalConnectionEventTopic.TopicName,
            ExternalConnectionEventTypes.SetDefault,
            ExternalConnectionEventTopic.ResourceKind,
            resource: ResourceFor(entity),
            details: null,
            cancellationToken);

        return ToRow(entity);
    }

    public async Task<RevealedConnection?> RevealForResolverAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.ExternalConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (entity is null || entity.SecretCiphertext is null) return null;

        var secret = _secretProtector.Reveal(entity.SecretCiphertext);
        return new RevealedConnection(entity.Id, entity.Kind, ParseMetadata(entity.MetadataJson), secret);
    }

    private static ExternalConnectionRow ToRow(ExternalConnection entity) => new(
        Id: entity.Id,
        Kind: entity.Kind,
        Name: entity.Name,
        Description: entity.Description,
        IsEnabled: entity.IsEnabled,
        IsDefault: entity.IsDefault,
        Metadata: ParseMetadata(entity.MetadataJson),
        SecretFingerprint: entity.SecretFingerprint,
        CreatedAtUtc: entity.CreatedAtUtc,
        CreatedBy: entity.CreatedBy,
        UpdatedAtUtc: entity.UpdatedAtUtc,
        UpdatedBy: entity.UpdatedBy);

    private static object ResourceFor(ExternalConnection entity) => new
    {
        id = entity.Id,
        kind = entity.Kind,
        name = entity.Name,
        secretFingerprint = entity.SecretFingerprint
    };

    private static string SerializeMetadata(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Undefined || element.ValueKind == JsonValueKind.Null)
        {
            return "{}";
        }
        return JsonSerializer.Serialize(element);
    }

    private static JsonElement ParseMetadata(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return doc.RootElement.Clone();
    }
}
