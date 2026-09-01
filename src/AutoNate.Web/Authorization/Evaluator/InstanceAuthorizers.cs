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
        if (string.IsNullOrEmpty(targetId)) return false;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // Endpoints route this kind on either the workflow_models GUID id
        // (publish/edit) or the process key (start/pause). Resolve both so
        // selectors with `[processkey=...]` can be evaluated against the
        // matching row regardless of which token the route exposes.
        var query = Guid.TryParse(targetId, out var id)
            ? db.WorkflowModels.AsNoTracking().Where(m => m.Id == id)
            : db.WorkflowModels.AsNoTracking().Where(m => m.ProcessKey == targetId);

        var visible = await authorizer.FilterQueryAsync(db, actor, Kind, action, query, cancellationToken);
        return await visible.AnyAsync(cancellationToken);
    }
}

public sealed class FormInstanceAuthorizer : IInstanceAuthorizer
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;

    public FormInstanceAuthorizer(IDbContextFactory<AutoNateDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public string Kind => EntityKinds.Form;

    public async Task<bool> ExistsAndAuthorizedAsync(
        IAuthorizer authorizer,
        ClaimsPrincipal actor,
        string action,
        string targetId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(targetId, out var id)) return false;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Forms.AsNoTracking().Where(f => f.Id == id);
        var visible = await authorizer.FilterQueryAsync(db, actor, Kind, action, query, cancellationToken);
        return await visible.AnyAsync(cancellationToken);
    }
}

// EntityKinds.User instance gating. The scaffolded LocalUser row has both a
// long surrogate `Id` and the Guid identity used everywhere else in the
// authorization layer (UserId column). Filter on UserId so the predicate
// matches the GUID coming off the route.
public sealed class UserInstanceAuthorizer : IInstanceAuthorizer
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;

    public UserInstanceAuthorizer(IDbContextFactory<AutoNateDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public string Kind => EntityKinds.User;

    public async Task<bool> ExistsAndAuthorizedAsync(
        IAuthorizer authorizer,
        ClaimsPrincipal actor,
        string action,
        string targetId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(targetId, out var id)) return false;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.LocalUsers.AsNoTracking().Where(u => u.UserId == id);
        var visible = await authorizer.FilterQueryAsync(db, actor, Kind, action, query, cancellationToken);
        return await visible.AnyAsync(cancellationToken);
    }
}

public sealed class ExternalConnectionInstanceAuthorizer : IInstanceAuthorizer
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;

    public ExternalConnectionInstanceAuthorizer(IDbContextFactory<AutoNateDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public string Kind => EntityKinds.ExternalConnection;

    public async Task<bool> ExistsAndAuthorizedAsync(
        IAuthorizer authorizer,
        ClaimsPrincipal actor,
        string action,
        string targetId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(targetId, out var id)) return false;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.ExternalConnections.AsNoTracking().Where(c => c.Id == id);
        var visible = await authorizer.FilterQueryAsync(db, actor, Kind, action, query, cancellationToken);
        return await visible.AnyAsync(cancellationToken);
    }
}

// The five kinds below had selector compilers registered — so list endpoints
// filtered correctly — but no IInstanceAuthorizer. Authorizer.ComputeDecisionAsync
// denies with "no instance handler for kind '<kind>'" when none is registered,
// so every RequirePermission endpoint for these kinds answered 403 to every
// non-super-admin, including the owner of the row being asked for. Datastore
// file upload/download/copy/table-preview, connector runs, dataset detail,
// saved-query detail and pipeline detail were all unreachable.
//
// Each one mirrors ExternalConnectionInstanceAuthorizer: ask FilterQueryAsync
// whether this specific row survives the caller's grants for this action, so
// instance access and list visibility are decided by exactly the same rule and
// cannot drift apart.

public sealed class DataStoreInstanceAuthorizer(
    IDbContextFactory<AutoNateDbContext> dbFactory) : IInstanceAuthorizer
{
    public string Kind => EntityKinds.DataStore;

    public async Task<bool> ExistsAndAuthorizedAsync(
        IAuthorizer authorizer,
        ClaimsPrincipal actor,
        string action,
        string targetId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(targetId, out var id)) return false;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.DataStores.AsNoTracking().Where(x => x.Id == id);
        var visible = await authorizer.FilterQueryAsync(db, actor, Kind, action, query, cancellationToken);
        return await visible.AnyAsync(cancellationToken);
    }
}

public sealed class DataConnectorInstanceAuthorizer(
    IDbContextFactory<AutoNateDbContext> dbFactory) : IInstanceAuthorizer
{
    public string Kind => EntityKinds.DataConnector;

    public async Task<bool> ExistsAndAuthorizedAsync(
        IAuthorizer authorizer,
        ClaimsPrincipal actor,
        string action,
        string targetId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(targetId, out var id)) return false;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.DataConnectors.AsNoTracking().Where(x => x.Id == id);
        var visible = await authorizer.FilterQueryAsync(db, actor, Kind, action, query, cancellationToken);
        return await visible.AnyAsync(cancellationToken);
    }
}

public sealed class DatasetInstanceAuthorizer(
    IDbContextFactory<AutoNateDbContext> dbFactory) : IInstanceAuthorizer
{
    public string Kind => EntityKinds.Dataset;

    public async Task<bool> ExistsAndAuthorizedAsync(
        IAuthorizer authorizer,
        ClaimsPrincipal actor,
        string action,
        string targetId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(targetId, out var id)) return false;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Datasets.AsNoTracking().Where(x => x.Id == id);
        var visible = await authorizer.FilterQueryAsync(db, actor, Kind, action, query, cancellationToken);
        return await visible.AnyAsync(cancellationToken);
    }
}

public sealed class SavedQueryInstanceAuthorizer(
    IDbContextFactory<AutoNateDbContext> dbFactory) : IInstanceAuthorizer
{
    public string Kind => EntityKinds.Query;

    public async Task<bool> ExistsAndAuthorizedAsync(
        IAuthorizer authorizer,
        ClaimsPrincipal actor,
        string action,
        string targetId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(targetId, out var id)) return false;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.SavedQueries.AsNoTracking().Where(x => x.Id == id);
        var visible = await authorizer.FilterQueryAsync(db, actor, Kind, action, query, cancellationToken);
        return await visible.AnyAsync(cancellationToken);
    }
}

public sealed class PipelineInstanceAuthorizer(
    IDbContextFactory<AutoNateDbContext> dbFactory) : IInstanceAuthorizer
{
    public string Kind => EntityKinds.Pipeline;

    public async Task<bool> ExistsAndAuthorizedAsync(
        IAuthorizer authorizer,
        ClaimsPrincipal actor,
        string action,
        string targetId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(targetId, out var id)) return false;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Pipelines.AsNoTracking().Where(x => x.Id == id);
        var visible = await authorizer.FilterQueryAsync(db, actor, Kind, action, query, cancellationToken);
        return await visible.AnyAsync(cancellationToken);
    }
}
