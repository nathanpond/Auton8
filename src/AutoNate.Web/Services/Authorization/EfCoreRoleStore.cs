using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Models.Authorization;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using RoleEntity = AutoNate.Web.Persistence.Scaffolded.Role;

namespace AutoNate.Web.Services.Authorization;

public sealed class EfCoreRoleStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    AuthCacheBumper cacheBumper,
    IAuthorizer authorizer) : IRoleStore
{
    public async Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Roles.AsNoTracking()
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<Role>> ListAuthorizedAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Roles.AsNoTracking().OrderBy(r => r.Name).AsQueryable();
        var visible = await authorizer.FilterQueryAsync(
            db, actor, EntityKinds.Role, Actions.View, query, cancellationToken);
        var rows = await visible.ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    public async Task<Role?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Roles.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        return entity is null ? null : ToModel(entity);
    }

    public async Task<Role> CreateAsync(CreateRoleInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new RoleValidationException("name is required.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Roles.AnyAsync(r => r.Name == name, cancellationToken))
        {
            throw new RoleValidationException($"Role '{name}' already exists.");
        }

        var now = DateTime.UtcNow;
        var entity = new RoleEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = input.Description?.Trim(),
            IsSystem = false,
            CreatedAtUtc = now,
            CreatedBy = actorId,
            UpdatedAtUtc = now,
            UpdatedBy = actorId
        };
        db.Roles.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        await cacheBumper.BumpAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task<Role> UpdateAsync(Guid id, UpdateRoleInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Roles.SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new RoleNotFoundException(id);

        if (entity.IsSystem)
        {
            throw new RoleValidationException("System roles cannot be modified.");
        }

        var changed = false;
        if (input.Name is { } newNameRaw)
        {
            var newName = newNameRaw.Trim();
            if (newName.Length == 0)
            {
                throw new RoleValidationException("name cannot be empty.");
            }

            if (!string.Equals(entity.Name, newName, StringComparison.Ordinal))
            {
                if (await db.Roles.AnyAsync(r => r.Name == newName && r.Id != id, cancellationToken))
                {
                    throw new RoleValidationException($"Role '{newName}' already exists.");
                }

                entity.Name = newName;
                changed = true;
            }
        }

        if (input.Description is { } newDescRaw)
        {
            var newDesc = string.IsNullOrWhiteSpace(newDescRaw) ? null : newDescRaw.Trim();
            if (!string.Equals(entity.Description, newDesc, StringComparison.Ordinal))
            {
                entity.Description = newDesc;
                changed = true;
            }
        }

        if (changed)
        {
            entity.UpdatedAtUtc = DateTime.UtcNow;
            entity.UpdatedBy = actorId;
            await db.SaveChangesAsync(cancellationToken);
            await cacheBumper.BumpAsync(cancellationToken);
        }

        return ToModel(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Roles.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        if (entity.IsSystem)
        {
            throw new RoleValidationException("System roles cannot be deleted.");
        }

        // Cascade-clean any direct grants attached to this role so the unified
        // permission_grants table doesn't carry orphans.
        var roleIdString = id.ToString();
        var staleGrants = db.PermissionGrants
            .Where(g => g.PrincipalKind == EntityKinds.Role && g.PrincipalId == roleIdString);
        db.PermissionGrants.RemoveRange(staleGrants);

        db.Roles.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        await cacheBumper.BumpAsync(cancellationToken);
        return true;
    }

    private static Role ToModel(RoleEntity e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Description = e.Description,
        IsSystem = e.IsSystem,
        CreatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(e.CreatedAtUtc),
        CreatedBy = e.CreatedBy,
        UpdatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(e.UpdatedAtUtc),
        UpdatedBy = e.UpdatedBy
    };
}
