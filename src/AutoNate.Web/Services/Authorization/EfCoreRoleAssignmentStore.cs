using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Authorization.Selectors;
using AutoNate.Web.Models.Authorization;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using RoleAssignmentEntity = AutoNate.Web.Persistence.Scaffolded.RoleAssignment;

namespace AutoNate.Web.Services.Authorization;

public sealed class EfCoreRoleAssignmentStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    AuthCacheBumper cacheBumper) : IRoleAssignmentStore
{
    private static readonly HashSet<string> AllowedPrincipalKinds =
        new(StringComparer.Ordinal) { EntityKinds.User, EntityKinds.Group };

    public async Task<IReadOnlyList<RoleAssignment>> ListByRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.RoleAssignments.AsNoTracking()
            .Where(a => a.RoleId == roleId)
            .OrderBy(a => a.PrincipalKind).ThenBy(a => a.PrincipalId)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<RoleAssignment>> ListForPrincipalAsync(
        string principalKind,
        string principalId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.RoleAssignments.AsNoTracking()
            .Where(a => a.PrincipalKind == principalKind && a.PrincipalId == principalId)
            .OrderBy(a => a.RoleId)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<RoleAssignment>> ListForPrincipalsAsync(
        string principalKind,
        IReadOnlyCollection<string> principalIds,
        CancellationToken cancellationToken = default)
    {
        if (principalIds.Count == 0) return Array.Empty<RoleAssignment>();

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        // Postgres translates Contains(...) to `principal_id = ANY(@p)` —
        // single round trip, server-side dedup is not needed because the
        // (principal_kind, principal_id, role_id) shape is already a unique
        // assignment row.
        var rows = await db.RoleAssignments.AsNoTracking()
            .Where(a => a.PrincipalKind == principalKind && principalIds.Contains(a.PrincipalId))
            .OrderBy(a => a.PrincipalId).ThenBy(a => a.RoleId)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    public async Task<RoleAssignment> AssignAsync(
        CreateRoleAssignmentInput input,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!AllowedPrincipalKinds.Contains(input.PrincipalKind))
        {
            throw new RoleAssignmentValidationException(
                $"principalKind must be '{EntityKinds.User}' or '{EntityKinds.Group}'.");
        }

        var principalId = (input.PrincipalId ?? string.Empty).Trim();
        if (principalId.Length == 0)
        {
            throw new RoleAssignmentValidationException("principalId is required.");
        }

        string? canonicalScope = null;
        string? scopeAstJson = null;
        if (!string.IsNullOrWhiteSpace(input.ScopeString))
        {
            SelectorAst ast;
            try
            {
                ast = SelectorParser.Parse(input.ScopeString);
            }
            catch (SelectorParseException ex)
            {
                throw new RoleAssignmentValidationException($"Invalid scope: {ex.Message}");
            }

            canonicalScope = SelectorPrinter.ToCanonicalString(ast);
            scopeAstJson = JsonSerializer.Serialize(ast);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var roleExists = await db.Roles.AnyAsync(r => r.Id == input.RoleId, cancellationToken);
        if (!roleExists)
        {
            throw new RoleAssignmentValidationException($"Role '{input.RoleId}' was not found.");
        }

        var existing = await db.RoleAssignments
            .SingleOrDefaultAsync(a =>
                a.RoleId == input.RoleId
                && a.PrincipalKind == input.PrincipalKind
                && a.PrincipalId == principalId,
                cancellationToken);
        if (existing is not null)
        {
            return ToModel(existing);
        }

        var entity = new RoleAssignmentEntity
        {
            Id = Guid.NewGuid(),
            RoleId = input.RoleId,
            PrincipalKind = input.PrincipalKind,
            PrincipalId = principalId,
            ScopeString = canonicalScope,
            ScopeAst = scopeAstJson,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actorId
        };
        db.RoleAssignments.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        await cacheBumper.BumpAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task<bool> RevokeAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.RoleAssignments.SingleOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        db.RoleAssignments.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        await cacheBumper.BumpAsync(cancellationToken);
        return true;
    }

    private static RoleAssignment ToModel(RoleAssignmentEntity e)
    {
        JsonElement? scopeAst = null;
        if (!string.IsNullOrWhiteSpace(e.ScopeAst))
        {
            using var doc = JsonDocument.Parse(e.ScopeAst);
            scopeAst = doc.RootElement.Clone();
        }

        return new RoleAssignment
        {
            Id = e.Id,
            RoleId = e.RoleId,
            PrincipalKind = e.PrincipalKind,
            PrincipalId = e.PrincipalId,
            ScopeString = e.ScopeString,
            ScopeAst = scopeAst,
            CreatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(e.CreatedAtUtc),
            CreatedBy = e.CreatedBy
        };
    }
}
