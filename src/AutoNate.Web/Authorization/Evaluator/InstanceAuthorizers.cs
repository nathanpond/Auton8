using System.Security.Claims;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Authorization.Evaluator;

public sealed class RecordInstanceAuthorizer : IInstanceAuthorizer
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;

    public RecordInstanceAuthorizer(IDbContextFactory<AutoNateDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public string Kind => EntityKinds.Record;

    public async Task<bool> ExistsAndAuthorizedAsync(
        IAuthorizer authorizer,
        ClaimsPrincipal actor,
        string action,
        string targetId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(targetId, out var id)) return false;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Records.AsNoTracking().Where(r => r.Id == id);
        var visible = await authorizer.FilterQueryAsync(db, actor, Kind, action, query, cancellationToken);
        return await visible.AnyAsync(cancellationToken);
    }
}

public sealed class RoleInstanceAuthorizer : IInstanceAuthorizer
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;

    public RoleInstanceAuthorizer(IDbContextFactory<AutoNateDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public string Kind => EntityKinds.Role;

    public async Task<bool> ExistsAndAuthorizedAsync(
        IAuthorizer authorizer,
        ClaimsPrincipal actor,
        string action,
        string targetId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(targetId, out var id)) return false;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Roles.AsNoTracking().Where(r => r.Id == id);
        var visible = await authorizer.FilterQueryAsync(db, actor, Kind, action, query, cancellationToken);
        return await visible.AnyAsync(cancellationToken);
    }
}

public sealed class GroupInstanceAuthorizer : IInstanceAuthorizer
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;

    public GroupInstanceAuthorizer(IDbContextFactory<AutoNateDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public string Kind => EntityKinds.Group;

    public async Task<bool> ExistsAndAuthorizedAsync(
        IAuthorizer authorizer,
        ClaimsPrincipal actor,
        string action,
        string targetId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(targetId, out var id)) return false;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Groups.AsNoTracking().Where(g => g.Id == id);
        var visible = await authorizer.FilterQueryAsync(db, actor, Kind, action, query, cancellationToken);
        return await visible.AnyAsync(cancellationToken);
    }
}

public sealed class RecordTypeInstanceAuthorizer : IInstanceAuthorizer
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;

    public RecordTypeInstanceAuthorizer(IDbContextFactory<AutoNateDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public string Kind => EntityKinds.RecordType;

    public async Task<bool> ExistsAndAuthorizedAsync(
        IAuthorizer authorizer,
        ClaimsPrincipal actor,
        string action,
        string targetId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(targetId, out var id)) return false;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.RecordTypes.AsNoTracking().Where(t => t.Id == id);
        var visible = await authorizer.FilterQueryAsync(db, actor, Kind, action, query, cancellationToken);
        return await visible.AnyAsync(cancellationToken);
    }
}

public sealed class WorkflowModelInstanceAuthorizer : IInstanceAuthorizer
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;

    public WorkflowModelInstanceAuthorizer(IDbContextFactory<AutoNateDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public string Kind => EntityKinds.WorkflowModel;

    public async Task<bool> ExistsAndAuthorizedAsync(
        IAuthorizer authorizer,
        ClaimsPrincipal actor,
        string action,
        string targetId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(targetId, out var id)) return false;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.WorkflowModels.AsNoTracking().Where(m => m.Id == id);
        var visible = await authorizer.FilterQueryAsync(db, actor, Kind, action, query, cancellationToken);
        return await visible.AnyAsync(cancellationToken);
    }
}
