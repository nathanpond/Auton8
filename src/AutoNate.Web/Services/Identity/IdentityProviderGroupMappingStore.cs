using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Identity;

public sealed record IdentityProviderGroupMappingDto(
    Guid Id,
    Guid ProviderId,
    string ClaimType,
    string ClaimValue,
    Guid GroupId,
    string GroupName,
    bool GroupIsArchived,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record UpsertGroupMappingRequest(string? ClaimType, string? ClaimValue, Guid? GroupId);

public interface IIdentityProviderGroupMappingStore
{
    Task<IReadOnlyList<IdentityProviderGroupMappingDto>> ListAsync(Guid providerId, CancellationToken ct);

    Task<IdentityProviderGroupMappingDto> CreateAsync(
        Guid providerId, UpsertGroupMappingRequest request, Guid actorId, CancellationToken ct);

    Task<IdentityProviderGroupMappingDto?> UpdateAsync(
        Guid providerId, Guid id, UpsertGroupMappingRequest request, Guid actorId, CancellationToken ct);

    Task<bool> DeleteAsync(Guid providerId, Guid id, CancellationToken ct);
}

public sealed class EfCoreIdentityProviderGroupMappingStore : IIdentityProviderGroupMappingStore
{
    private readonly IDbContextFactory<AutoNateDbContext> _factory;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _clock;

    public EfCoreIdentityProviderGroupMappingStore(
        IDbContextFactory<AutoNateDbContext> factory,
        IAuditEventPublisher audit,
        TimeProvider clock)
    {
        _factory = factory;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IReadOnlyList<IdentityProviderGroupMappingDto>> ListAsync(
        Guid providerId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await Query(db, m => m.ProviderId == providerId).ToListAsync(ct);
    }

    public async Task<IdentityProviderGroupMappingDto> CreateAsync(
        Guid providerId, UpsertGroupMappingRequest request, Guid actorId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var db = await _factory.CreateDbContextAsync(ct);
        var (claimType, claimValue, groupId) = await ValidateAsync(db, providerId, request, ct);

        var duplicate = await db.IdentityProviderGroupMappings.AnyAsync(
            m => m.ProviderId == providerId
                 && m.ClaimType == claimType
                 && m.ClaimValue == claimValue
                 && m.GroupId == groupId,
            ct);
        if (duplicate)
        {
            throw new IdentityProviderValidationException(
                "That claim already grants that group. The same claim may grant several groups and "
                + "several claims may grant one group, but the same edge twice would mean nothing.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var row = new IdentityProviderGroupMappingModel
        {
            Id = Guid.NewGuid(),
            ProviderId = providerId,
            ClaimType = claimType,
            ClaimValue = claimValue,
            GroupId = groupId,
            CreatedAtUtc = now,
            CreatedBy = actorId,
            UpdatedAtUtc = now,
            UpdatedBy = actorId,
        };

        db.IdentityProviderGroupMappings.Add(row);
        await db.SaveChangesAsync(ct);

        await PublishAsync(IdentityEventTypes.GroupMappingCreated, row, ct);
        return await SingleAsync(db, row.Id, ct);
    }

    public async Task<IdentityProviderGroupMappingDto?> UpdateAsync(
        Guid providerId, Guid id, UpsertGroupMappingRequest request, Guid actorId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.IdentityProviderGroupMappings
            .FirstOrDefaultAsync(m => m.Id == id && m.ProviderId == providerId, ct);
        if (row is null) return null;

        var (claimType, claimValue, groupId) = await ValidateAsync(db, providerId, request, ct);

        row.ClaimType = claimType;
        row.ClaimValue = claimValue;
        row.GroupId = groupId;
        row.UpdatedAtUtc = _clock.GetUtcNow().UtcDateTime;
        row.UpdatedBy = actorId;
        await db.SaveChangesAsync(ct);

        await PublishAsync(IdentityEventTypes.GroupMappingUpdated, row, ct);
        return await SingleAsync(db, row.Id, ct);
    }

    public async Task<bool> DeleteAsync(Guid providerId, Guid id, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.IdentityProviderGroupMappings
            .FirstOrDefaultAsync(m => m.Id == id && m.ProviderId == providerId, ct);
        if (row is null) return false;

        db.IdentityProviderGroupMappings.Remove(row);
        await db.SaveChangesAsync(ct);

        // Deleting a mapping does not revoke anything on its own. The membership
        // it granted disappears at the affected users' next sign-in, when
        // reconciliation finds no mapping backing it — which is the same path
        // every other revocation takes, rather than a second one that would have
        // to be kept in step with it.
        await PublishAsync(IdentityEventTypes.GroupMappingDeleted, row, ct);
        return true;
    }

    private async Task<(string ClaimType, string ClaimValue, Guid GroupId)> ValidateAsync(
        AutoNateDbContext db, Guid providerId, UpsertGroupMappingRequest request, CancellationToken ct)
    {
        var claimType = request.ClaimType?.Trim();
        var claimValue = request.ClaimValue?.Trim();

        if (string.IsNullOrWhiteSpace(claimType))
        {
            throw new IdentityProviderValidationException(
                "A claim type is required — for OIDC usually 'groups', for SAML the attribute name.");
        }

        if (string.IsNullOrWhiteSpace(claimValue))
        {
            throw new IdentityProviderValidationException(
                "A claim value is required. Mapping a claim type with no value would grant the group to "
                + "everyone carrying that claim at all, whatever its contents.");
        }

        if (request.GroupId is not { } groupId || groupId == Guid.Empty)
        {
            throw new IdentityProviderValidationException("A group is required.");
        }

        if (!await db.IdentityProviders.AnyAsync(p => p.Id == providerId, ct))
        {
            throw new IdentityProviderValidationException($"No identity provider '{providerId}'.");
        }

        if (!await db.Groups.AnyAsync(g => g.Id == groupId, ct))
        {
            throw new IdentityProviderValidationException($"No group '{groupId}'.");
        }

        return (claimType, claimValue, groupId);
    }

    private Task PublishAsync(string eventType, IdentityProviderGroupMappingModel row, CancellationToken ct) =>
        _audit.PublishAsync(
            IamEventTopic.TopicName,
            eventType,
            IamResourceKinds.GroupMember,
            resource: new { mappingId = row.Id, providerId = row.ProviderId, groupId = row.GroupId },
            // The claim itself is in the event. A mapping is an access-control
            // rule, and an auditor asking "why did this person have that group"
            // needs to see which claim was believed to grant it.
            details: new { claimType = row.ClaimType, claimValue = row.ClaimValue },
            ct);

    /// <summary>
    /// Projects mappings with their group's name attached.
    /// </summary>
    /// <remarks>
    /// Filtering and ordering both happen on the entity, before the projection
    /// rather than on the projected record afterwards. A positional record's
    /// property is not something EF can translate back to a column, and the
    /// failure is a runtime 500 rather than a compile error — so the shape that
    /// cannot express the mistake is the one to keep.
    /// </remarks>
    private static IQueryable<IdentityProviderGroupMappingDto> Query(
        AutoNateDbContext db,
        // Named `predicate`, not `where`: inside query syntax the latter is a
        // contextual keyword and the parser gives up on the whole expression.
        System.Linq.Expressions.Expression<Func<IdentityProviderGroupMappingModel, bool>> predicate) =>
        from m in db.IdentityProviderGroupMappings.AsNoTracking().Where(predicate)
        join g in db.Groups.AsNoTracking() on m.GroupId equals g.Id
        orderby m.ClaimType, m.ClaimValue, g.Name
        select new IdentityProviderGroupMappingDto(
            m.Id, m.ProviderId, m.ClaimType, m.ClaimValue,
            m.GroupId, g.Name, g.IsArchived, m.CreatedAtUtc, m.UpdatedAtUtc);

    private static async Task<IdentityProviderGroupMappingDto> SingleAsync(
        AutoNateDbContext db, Guid id, CancellationToken ct) =>
        await Query(db, m => m.Id == id).FirstAsync(ct);
}
