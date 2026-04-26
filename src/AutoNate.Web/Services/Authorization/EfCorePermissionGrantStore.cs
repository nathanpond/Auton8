using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Authorization.Selectors;
using AutoNate.Web.Models.Authorization;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using PermissionGrantEntity = AutoNate.Web.Persistence.Scaffolded.PermissionGrant;

namespace AutoNate.Web.Services.Authorization;

public sealed class EfCorePermissionGrantStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    AuthCacheBumper cacheBumper) : IPermissionGrantStore
{
    private static readonly HashSet<string> AllowedPrincipalKinds =
        new(StringComparer.Ordinal) { EntityKinds.User, EntityKinds.Group, EntityKinds.Role };

    public async Task<IReadOnlyList<PermissionGrant>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.PermissionGrants.AsNoTracking()
            .OrderBy(g => g.PrincipalKind).ThenBy(g => g.PrincipalId).ThenBy(g => g.Action)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<PermissionGrant>> ListForPrincipalAsync(
        string principalKind,
        string principalId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.PermissionGrants.AsNoTracking()
            .Where(g => g.PrincipalKind == principalKind && g.PrincipalId == principalId)
            .OrderBy(g => g.Action).ThenBy(g => g.SelectorString)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    public async Task<PermissionGrant> CreateAsync(
        CreatePermissionGrantInput input,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!AllowedPrincipalKinds.Contains(input.PrincipalKind))
        {
            throw new PermissionGrantValidationException(
                $"principalKind must be '{EntityKinds.User}' or '{EntityKinds.Group}'.");
        }

        var principalId = (input.PrincipalId ?? string.Empty).Trim();
        if (principalId.Length == 0)
        {
            throw new PermissionGrantValidationException("principalId is required.");
        }

        var action = (input.Action ?? string.Empty).Trim();
        if (action.Length == 0)
        {
            throw new PermissionGrantValidationException("action is required.");
        }

        var selectorString = (input.SelectorString ?? string.Empty).Trim();
        if (selectorString.Length == 0)
        {
            throw new PermissionGrantValidationException("selectorString is required.");
        }

        SelectorAst ast;
        try
        {
            ast = SelectorParser.Parse(selectorString);
        }
        catch (SelectorParseException ex)
        {
            throw new PermissionGrantValidationException($"Invalid selector: {ex.Message}");
        }

        var effect = (input.Effect ?? string.Empty).ToLowerInvariant();
        if (effect != "allow" && effect != "deny")
        {
            throw new PermissionGrantValidationException("effect must be 'allow' or 'deny'.");
        }

        var canonical = SelectorPrinter.ToCanonicalString(ast);
        var astJson = JsonSerializer.Serialize(ast);
        var now = DateTime.UtcNow;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = new PermissionGrantEntity
        {
            Id = Guid.NewGuid(),
            PrincipalKind = input.PrincipalKind,
            PrincipalId = principalId,
            Action = action,
            SelectorString = canonical,
            SelectorAst = astJson,
            Effect = effect,
            Priority = input.Priority,
            CreatedAtUtc = now,
            CreatedBy = actorId,
            UpdatedAtUtc = now,
            UpdatedBy = actorId
        };
        db.PermissionGrants.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        await cacheBumper.BumpAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.PermissionGrants.SingleOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (entity is null) return false;

        db.PermissionGrants.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        await cacheBumper.BumpAsync(cancellationToken);
        return true;
    }

    private static PermissionGrant ToModel(PermissionGrantEntity e)
    {
        using var doc = JsonDocument.Parse(e.SelectorAst);
        return new PermissionGrant
        {
            Id = e.Id,
            PrincipalKind = e.PrincipalKind,
            PrincipalId = e.PrincipalId,
            Action = e.Action,
            SelectorString = e.SelectorString,
            SelectorAst = doc.RootElement.Clone(),
            Effect = e.Effect,
            Priority = e.Priority,
            CreatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(e.CreatedAtUtc),
            CreatedBy = e.CreatedBy,
            UpdatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(e.UpdatedAtUtc),
            UpdatedBy = e.UpdatedBy
        };
    }
}
