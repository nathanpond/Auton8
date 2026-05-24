# AQL integration

How a cache table becomes a first-class AQL entity.

## The contract

An AQL entity implements `IQueryEntity`:

```csharp
public interface IQueryEntity
{
    string Name { get; }                          // user-facing entity name
    IReadOnlyList<QueryColumn> StaticSchema { get; }
    IReadOnlyList<string> AllowedFunctions { get; }
    IReadOnlyList<string> RowFunctions { get; }   // scalar-per-row functions
    QueryDataType RowFunctionDataType(string functionName);
    Task<IPreparedQuery> PrepareAsync(AqlQuery query, CancellationToken cancellationToken);
}
```

`PrepareAsync` returns an `IPreparedQuery` that the AQL executor calls
later with the actor's `ClaimsPrincipal`. The prepared query owns the
auth check, the actual data fetch, and the WHERE/ORDER BY/LIMIT
evaluation.

## Minimal projection-backed entity

The shortest path from a cache table to AQL is to copy
`WorkflowExecutionsQueryEntity` and adapt three things:

1. `Name` and `StaticSchema` — what the user types and gets back.
2. The EF query inside `ExecuteAsync` — point at your cache `DbSet`.
3. The `EntityKinds.X` passed to `FilterQueryAsync` — your kind.

```csharp
public sealed class SupportTicketsQueryEntity : IQueryEntity
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly IAuthorizer _authorizer;

    public SupportTicketsQueryEntity(
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IAuthorizer authorizer)
    {
        _dbFactory = dbFactory;
        _authorizer = authorizer;
    }

    public string Name => "SupportTickets";

    public IReadOnlyList<QueryColumn> StaticSchema { get; } = new[]
    {
        new QueryColumn("Id",        QueryDataType.String, false, true),
        new QueryColumn("Subject",   QueryDataType.String, false, true),
        new QueryColumn("Status",    QueryDataType.String, false, true),
        new QueryColumn("CreatedAt", QueryDataType.Date,   true,  true),
    };

    public IReadOnlyList<string> AllowedFunctions { get; } = Array.Empty<string>();

    public Task<IPreparedQuery> PrepareAsync(AqlQuery query, CancellationToken cancellationToken)
    {
        IPreparedQuery prepared = new SupportTicketsPreparedQuery(
            this, query, StaticSchema, _dbFactory, _authorizer);
        return Task.FromResult(prepared);
    }
}
```

The `IPreparedQuery` implementation is mechanical — the WHERE / ORDER
BY / LIMIT evaluators are the same across cache-backed entities. Copy
from `WorkflowExecutionsPreparedQuery`.

## `QueryColumn` flags

```csharp
public sealed record QueryColumn(
    string Name,
    QueryDataType DataType,
    bool IsAggregable,   // can appear inside COUNT/MIN/MAX/AVG/MEDIAN?
    bool IsSystem);       // built-in, not user-defined?
```

For cache columns: `IsSystem = true`. `IsAggregable = true` for
numerics and dates; `false` for strings and JSON. The validator uses
this to gate aggregate calls in `COLUMNS()` and `ORDER BY`.

## Row functions vs aggregates

Row functions are scalar-per-row calls evaluated at projection time
(no GROUP required). Aggregates need a GROUP BY.

```csharp
// Declare row functions on the entity:
public IReadOnlyList<string> RowFunctions { get; } = new[]
{
    "NUMNODES", "NUMEXECUTIONS", "LASTEXECUTED"
};

public QueryDataType RowFunctionDataType(string fn) => fn.ToUpperInvariant() switch
{
    "LASTEXECUTED" => QueryDataType.Date,
    _ => QueryDataType.Number
};
```

The validator looks at `RowFunctions` before the standard aggregate
allowlist, so users can write `COLUMNS(ModelName, NUMEXECUTIONS())`
without a GROUP clause. Inside your `IPreparedQuery`, handle the
`item.IsAggregate` branch in `SelectItemToProjection` and dispatch to
a per-row evaluator — see `WorkflowModelsPreparedQuery.EvalRowFunction`
for the pattern.

If a user writes a true aggregate (`COUNT()`, `SUM(Foo)`) without
GROUP, the validator emits the error
`Aggregate '{fn}()' in COLUMNS() requires a GROUP(...) clause` before
your entity is called.

## Authorization

Three patterns, pick the one that fits:

### Pattern A: cache has its own permission tags

The cache row stores tags in a JSONB `auth_tags` column. Implement
`ISelectorCompiler<TCacheRow>` to translate selector grants into LINQ
predicates over that column or other indexed columns.

Inside `ExecuteAsync`:

```csharp
var baseQuery = db.SupportTicketCache.AsNoTracking().AsQueryable();
var authorized = await _authorizer.FilterQueryAsync(
    db, actor, EntityKinds.SupportTicket, Actions.View, baseQuery, cancellationToken);
var rows = await authorized.ToListAsync(cancellationToken);
```

The authorizer applies your compiler to every grant the actor has for
that kind/action, AND-combines the allows, and AND-NOT-OR's the denies.

You'll need to register the compiler in `Program.cs`:

```csharp
builder.Services.AddSingleton<ISelectorCompiler, SupportTicketSelectorCompiler>();
```

…and the kind has to exist in `EntityKinds.cs` + `CoreEntityTypes.cs`.
See the [`add-permission-gate`](../../.claude/skills/add-permission-gate/SKILL.md)
skill for that side.

### Pattern B: inherit auth from a parent entity

The cache row belongs to a parent that already has its own permissions.
Two-step query:

```csharp
// 1. Visible parent IDs.
var visibleInstances = await _authorizer.FilterQueryAsync(
    db, actor, EntityKinds.WorkflowExecution, Actions.View,
    db.WorkflowExecutionCache.AsNoTracking().AsQueryable(),
    cancellationToken);
var visibleIds = await visibleInstances.Select(c => c.FlowableInstanceId)
    .ToListAsync(cancellationToken);

// 2. Restrict child rows.
var rows = await db.WorkflowVariableCache.AsNoTracking()
    .Where(v => visibleIds.Contains(v.FlowableInstanceId))
    .ToListAsync(cancellationToken);
```

`WorkflowVariablesQueryEntity` and `WorkflowHistoryQueryEntity` both
use this pattern. No selector compiler needed for the child kind.

### Pattern C: kind-level only (no per-row gating)

Simplest case — if the actor has `View` on the kind at all, they see
everything in the cache. Use `_authorizer.AuthorizeAsync` for the
kind-level check, return empty if denied, otherwise fetch unrestricted
rows. Suitable only for tables where row-level visibility doesn't
matter (admin-facing rollups that don't expose PII, for example).

## Registration in `Program.cs`

The AQL executor builds its entity registry from DI. Each entity needs
two registrations — once as the concrete type (so other code can
resolve it directly), once as `IQueryEntity` (so the registry picks it
up).

```csharp
builder.Services.AddScoped<SupportTicketsQueryEntity>();
builder.Services.AddScoped<IQueryEntity>(sp =>
    sp.GetRequiredService<SupportTicketsQueryEntity>());
```

Forgetting the second line is a silent failure: the entity instantiates
fine, but the AQL executor never sees it and queries against it return
`Unknown entity 'SupportTickets'`.

## Testing the entity

The canonical test shape (from `ProjectionFrameworkTests`):

```csharp
[Fact]
public async Task SupportTickets_AQL_entity_returns_cached_rows()
{
    await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
    _ = factory.CreateClient();

    // Seed the cache through the projection.
    using (var seedScope = factory.Services.CreateScope())
    {
        var projection = seedScope.ServiceProvider.GetRequiredService<SupportTicketProjection>();
        var dbFactory = seedScope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        await projection.ApplyAsync(new[]
        {
            new ChangeEvent<SupportTicketSnapshot>(ChangeOp.Upsert, "tkt-1",
                new SupportTicketSnapshot(...), DateTimeOffset.UtcNow)
        }, db, CancellationToken.None);
    }

    // Run an AQL query.
    using var scope = factory.Services.CreateScope();
    var entity = scope.ServiceProvider.GetRequiredService<SupportTicketsQueryEntity>();
    var aql = new AqlQuery(
        Entity: "SupportTickets",
        Where: new AqlCompare("Status", "=", new AqlString("open")),
        OrderBy: Array.Empty<AqlOrderItem>(),
        Columns: null, Group: null, Limit: null);

    var prepared = await entity.PrepareAsync(aql, CancellationToken.None);
    var actor = new ClaimsPrincipal(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
    }, "test"));
    var result = await prepared.ExecuteAsync(actor, hardCap: 100, CancellationToken.None);

    Assert.Single(result.Rows);
    Assert.Equal("tkt-1", (string?)result.Rows[0]["Id"]);
}
```

The test factory disables the worker by default
(`Projections:WorkerEnabled = false`), so no background ticks interfere
with the seed/query timing. Calling `projection.ApplyAsync` directly
gives deterministic seeding.
