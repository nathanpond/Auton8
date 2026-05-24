# Recipe: add a projection (host code)

End-to-end walkthrough for adding a new projection inside the main
`AutoNate.Web` project. For plugin-contributed projections, see the
[plugin recipe](recipe-plugin-projection.md) instead.

We'll use a running example: a `support_ticket_cache` populated from an
external `Zendesk` HTTP client. Substitute your own source.

## What you'll touch

1. **Schema** — a new Postgres table for the cache.
2. **Scaffolded entity** — EF-mapped CLR type for the table.
3. **DbContext partial** — `DbSet` + `OnModelCreating` registration.
4. **Source type** — a record describing one source row.
5. **Projection** — translates source → cache row.
6. **Change feed** — pulls rows from the external system on a schedule (or
   subscribes to events).
7. **AQL entity** — exposes the cache to AQL.
8. **Selector compiler** *(optional)* — if the cache has its own permission
   tags. If parent auth is enough, skip this and inherit from the parent.
9. **DI wiring** in `Program.cs`.
10. **Configuration** in `appsettings.json`.
11. **Tests**.

The whole thing is usually 7–10 small files plus a migration. Plan for
half a day for the first one; subsequent projections take an hour or two.

## 1. Schema

Add to `src/AutoNate.Web/Persistence/DatabaseSchemaInitializer.cs`:

```csharp
private const string SupportTicketCacheSchemaSql =
    """
    CREATE TABLE IF NOT EXISTS support_ticket_cache (
        zendesk_id TEXT PRIMARY KEY,
        subject TEXT NOT NULL,
        requester_email TEXT NULL,
        priority TEXT NULL,
        status TEXT NOT NULL,
        created_at TIMESTAMPTZ NOT NULL,
        updated_at TIMESTAMPTZ NOT NULL,
        auth_tags JSONB NOT NULL DEFAULT '{{}}'::jsonb,
        projection_version INT NOT NULL DEFAULT 1,
        last_sync_at TIMESTAMPTZ NOT NULL
    );

    CREATE INDEX IF NOT EXISTS ix_support_ticket_cache_status
        ON support_ticket_cache (status);
    CREATE INDEX IF NOT EXISTS ix_support_ticket_cache_auth_tags
        ON support_ticket_cache USING GIN (auth_tags jsonb_path_ops);
    """;
```

**Watch out**: `ExecuteSqlRawAsync` uses `string.Format` placeholder
syntax, so any literal `{` in your SQL must be doubled to `{{`. The
`'{{}}'::jsonb` default above is the canonical "empty JSONB" idiom.

Then add it to `EnsureAsync`:

```csharp
await dbContext.Database.ExecuteSqlRawAsync(SupportTicketCacheSchemaSql, cancellationToken);
```

**Indexing strategy**: most cache queries fall into one of three shapes —
point lookup by primary key, predicate by indexed scalar column, or
predicate against the `auth_tags` JSONB. Cover the second with regular
btree indexes, the third with `GIN (auth_tags jsonb_path_ops)`. For
time-based scans on high-volume tables, add a `BRIN` index on the
timestamp column.

## 2. Scaffolded entity

`src/AutoNate.Web/Persistence/Scaffolded/SupportTicketCache.cs`:

```csharp
namespace AutoNate.Web.Persistence.Scaffolded;

public partial class SupportTicketCache
{
    public string ZendeskId { get; set; } = null!;
    public string Subject { get; set; } = null!;
    public string? RequesterEmail { get; set; }
    public string? Priority { get; set; }
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string AuthTagsJson { get; set; } = "{}";
    public int ProjectionVersion { get; set; }
    public DateTime LastSyncAtUtc { get; set; }
}
```

## 3. DbContext partial

Extend `src/AutoNate.Web/Persistence/AutoNateDbContext.ProjectionCaches.cs`:

```csharp
public virtual DbSet<SupportTicketCache> SupportTicketCache { get; set; } = null!;

// inside OnModelCreatingPartial:
modelBuilder.Entity<SupportTicketCache>(entity =>
{
    entity.HasKey(e => e.ZendeskId).HasName("support_ticket_cache_pkey");
    entity.ToTable("support_ticket_cache");

    entity.Property(e => e.ZendeskId).HasColumnName("zendesk_id");
    entity.Property(e => e.Subject).HasColumnName("subject");
    entity.Property(e => e.RequesterEmail).HasColumnName("requester_email");
    entity.Property(e => e.Priority).HasColumnName("priority");
    entity.Property(e => e.Status).HasColumnName("status");
    entity.Property(e => e.CreatedAt).HasColumnName("created_at");
    entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
    entity.Property(e => e.AuthTagsJson).HasColumnName("auth_tags").HasColumnType("jsonb");
    entity.Property(e => e.ProjectionVersion).HasColumnName("projection_version");
    entity.Property(e => e.LastSyncAtUtc).HasColumnName("last_sync_at");
});
```

## 4. Source type

The shape your feed emits and your projection consumes:

```csharp
namespace AutoNate.Web.Services.Support;

public sealed record class SupportTicketSnapshot(
    string ZendeskId,
    string Subject,
    string? RequesterEmail,
    string? Priority,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
```

This is the *source* shape, not the cache row shape. It's what the
external system gives you. The projection's job is to translate it.

## 5. Projection

```csharp
using System.Text.Json;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Projections;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Support;

public sealed class SupportTicketProjection : IProjection<SupportTicketSnapshot>
{
    public string Name => "support.support_ticket_cache";
    public int Version => 1;
    public Type SourceType => typeof(SupportTicketSnapshot);

    public async Task ApplyAsync(
        IReadOnlyList<ChangeEvent<SupportTicketSnapshot>> batch,
        AutoNateDbContext db,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0) return;

        // Collapse same-id repeats within the batch to "latest wins".
        var latest = new Dictionary<string, ChangeEvent<SupportTicketSnapshot>>(StringComparer.Ordinal);
        var deletes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in batch)
        {
            if (change.Op == ChangeOp.Delete)
            {
                latest.Remove(change.SourceId);
                deletes.Add(change.SourceId);
            }
            else
            {
                deletes.Remove(change.SourceId);
                latest[change.SourceId] = change;
            }
        }

        var now = DateTime.UtcNow;
        foreach (var change in latest.Values)
        {
            var src = change.Source!;
            var authTags = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["status"] = src.Status,
                ["priority"] = src.Priority,
                ["requester"] = src.RequesterEmail,
            };
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO support_ticket_cache (
                    zendesk_id, subject, requester_email, priority, status,
                    created_at, updated_at,
                    auth_tags, projection_version, last_sync_at)
                VALUES (
                    {src.ZendeskId}, {src.Subject}, {src.RequesterEmail}, {src.Priority}, {src.Status},
                    {src.CreatedAt.UtcDateTime}, {src.UpdatedAt.UtcDateTime},
                    {JsonSerializer.Serialize(authTags)}::jsonb, {Version}, {now})
                ON CONFLICT (zendesk_id) DO UPDATE SET
                    subject            = EXCLUDED.subject,
                    requester_email    = EXCLUDED.requester_email,
                    priority           = EXCLUDED.priority,
                    status             = EXCLUDED.status,
                    created_at         = EXCLUDED.created_at,
                    updated_at         = EXCLUDED.updated_at,
                    auth_tags          = EXCLUDED.auth_tags,
                    projection_version = EXCLUDED.projection_version,
                    last_sync_at       = EXCLUDED.last_sync_at
                """, cancellationToken);
        }

        foreach (var id in deletes)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM support_ticket_cache WHERE zendesk_id = {id}",
                cancellationToken);
        }
    }
}
```

**Idempotency**: `ON CONFLICT ... DO UPDATE` makes replays cheap. The
explicit dedup loop ahead of the SQL keeps batches small when many
events for the same ticket arrive together (a common pattern when
backfilling from a busy day).

## 6. Change feed

For poll-based sources, extend `PeriodicPollingFeed<TSource>`:

```csharp
using AutoNate.Web.Services.Projections;
using AutoNate.Web.Services.Projections.Feeds;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Support;

public sealed class SupportTicketPollingFeed : PeriodicPollingFeed<SupportTicketSnapshot>
{
    private readonly IZendeskClient _zendesk;

    public SupportTicketPollingFeed(
        IZendeskClient zendesk,
        IOptions<SupportTicketCacheOptions> options,
        ILogger<SupportTicketPollingFeed> logger)
        : base("support.tickets.poll", options.Value.PollInterval, logger)
    {
        _zendesk = zendesk;
    }

    protected override async Task TickAsync(CancellationToken cancellationToken)
    {
        var tickets = await _zendesk.ListRecentTicketsAsync(cancellationToken);
        foreach (var t in tickets)
        {
            await EmitAsync(
                new ChangeEvent<SupportTicketSnapshot>(
                    ChangeOp.Upsert, t.ZendeskId, t, DateTimeOffset.UtcNow),
                cancellationToken);
        }
    }
}
```

**Watermarks**: if you want incremental polling (only fetch what changed
since last tick), inject `IProjectionWatermarkStore`, read with
`GetAsync(FeedName, ct)` before the fetch, and call `SetAsync` after.
See `FlowableHistoryPollingFeed` for the canonical pattern.

For event-driven sources (NATS, webhook, Dapr pub/sub), implement
`IChangeFeed<TSource>` directly and emit `ChangeEvent`s from the
subscription handler.

## 7. AQL entity

`src/AutoNate.Web/Services/Query/Entities/SupportTicketsQueryEntity.cs`:

The cleanest model is to copy `WorkflowExecutionsQueryEntity` as a
template — same shape, just different columns and a different selector
kind. The skeleton:

```csharp
public sealed class SupportTicketsQueryEntity : IQueryEntity
{
    public string Name => "SupportTickets";

    public IReadOnlyList<QueryColumn> StaticSchema { get; } = new[]
    {
        new QueryColumn("Id",       QueryDataType.String, false, true),
        new QueryColumn("Subject",  QueryDataType.String, false, true),
        new QueryColumn("Priority", QueryDataType.String, false, true),
        new QueryColumn("Status",   QueryDataType.String, false, true),
        new QueryColumn("CreatedAt",QueryDataType.Date,   true,  true),
        // ...
    };

    public IReadOnlyList<string> AllowedFunctions { get; } = Array.Empty<string>();

    public Task<IPreparedQuery> PrepareAsync(AqlQuery query, CancellationToken ct)
    {
        IPreparedQuery prepared = new SupportTicketsPreparedQuery(
            this, query, StaticSchema, _dbFactory, _authorizer);
        return Task.FromResult(prepared);
    }
}
```

Inside `ExecuteAsync`, call `_authorizer.FilterQueryAsync<SupportTicketCache>(...)`
before materializing rows. The authorizer reads grants for
`EntityKinds.SupportTicket` and applies your selector compiler (next
step) to translate them into a SQL `WHERE` clause.

## 8. Selector compiler (optional)

Only needed if the cache has its own permission tags. If support tickets
inherit visibility from a parent entity (e.g. tickets belong to a
project), follow the parent-auth pattern from
`WorkflowVariablesQueryEntity`: filter the parent kind, then restrict the
child query to `WHERE parent_id IN visible_ids`.

If the cache has its own tags (e.g. `[priority=high]`,
`[requester=$me]`), implement `ISelectorCompiler<SupportTicketCache>`:

```csharp
public sealed class SupportTicketSelectorCompiler : ISelectorCompiler<SupportTicketCache>
{
    public string Kind => EntityKinds.SupportTicket;

    public Expression<Func<SupportTicketCache, bool>> Compile(
        SelectorAst ast, CompilationContext context)
    {
        var predicate = ExpressionUtilities.AlwaysTrue<SupportTicketCache>();
        if (ast.Predicate is { } pred)
        {
            foreach (var expr in pred.Expressions)
            {
                predicate = ExpressionUtilities.AndAlso(predicate, CompileExpr(expr, context));
            }
        }
        return predicate;
    }

    // ... CompileExpr handles individual tag predicates by mapping tag name
    // to the appropriate column comparison (see WorkflowExecutionCacheSelectorCompiler).
}
```

Don't forget to add the `EntityKinds.SupportTicket` constant and
register the kind in `CoreEntityTypes.cs` with its allowed `tags`.
The [`add-permission-gate`](../../.claude/skills/add-permission-gate/SKILL.md)
skill walks through that side.

## 9. Wire it up in `Program.cs`

```csharp
builder.Services.Configure<SupportTicketCacheOptions>(
    builder.Configuration.GetSection(SupportTicketCacheOptions.SectionName));

AutoNate.Web.Services.Projections.ProjectionServiceCollectionExtensions
    .AddProjection<SupportTicketSnapshot, SupportTicketProjection>(builder.Services);
AutoNate.Web.Services.Projections.ProjectionServiceCollectionExtensions
    .AddChangeFeed<SupportTicketSnapshot, SupportTicketPollingFeed>(builder.Services);

// Selector compiler (if you wrote one)
builder.Services.AddSingleton<ISelectorCompiler, SupportTicketSelectorCompiler>();

// AQL entity registration mirrors the existing pattern
builder.Services.AddScoped<SupportTicketsQueryEntity>();
builder.Services.AddScoped<IQueryEntity>(sp =>
    sp.GetRequiredService<SupportTicketsQueryEntity>());
```

The first two lines are enough for the projection to start populating on
app start. The selector and AQL entity registrations make it queryable.

## 10. Configuration

`appsettings.json`:

```json
"SupportTicketCache": {
  "PollInterval": "00:05:00",
  "CurrentProjectionVersion": 1
}
```

## 11. Tests

A minimal projection-roundtrip test:

```csharp
[Fact]
public async Task Support_ticket_projection_writes_and_updates()
{
    await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
    _ = factory.CreateClient();

    using var scope = factory.Services.CreateScope();
    var projection = scope.ServiceProvider.GetRequiredService<SupportTicketProjection>();
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();

    var snap = new SupportTicketSnapshot(
        "tkt-1", "Order shipped late", "alice@example.com", "high", "open",
        DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow);
    await projection.ApplyAsync(new[]
    {
        new ChangeEvent<SupportTicketSnapshot>(
            ChangeOp.Upsert, snap.ZendeskId, snap, DateTimeOffset.UtcNow)
    }, db, CancellationToken.None);

    var row = await db.SupportTicketCache.AsNoTracking()
        .FirstOrDefaultAsync(t => t.ZendeskId == "tkt-1");
    Assert.NotNull(row);
    Assert.Equal("open", row!.Status);
}
```

For the AQL entity side, copy the pattern in `ProjectionFrameworkTests`:
seed via the projection, run an `AqlQuery` through the entity's
`PrepareAsync` + `ExecuteAsync`, assert on rows.

## Common pitfalls

- **`'{}'::jsonb` in raw SQL inside `ExecuteSqlRawAsync`** crashes with a
  format-parsing error. Use `'{{}}'::jsonb` (doubled braces).
- **Forgetting to register the entity twice** — once as the concrete type,
  once as `IQueryEntity`. Both are needed for the AQL executor's registry
  scan to pick it up.
- **DateTime kind mismatches**: Postgres `timestamptz` columns only
  accept `Kind = Utc` parameters. Always `DateTime.SpecifyKind(..., Utc)`
  before passing to a parameter, and after reading back from any
  EF query whose return type is `DateTime` (especially `SqlQueryRaw`).
- **The polling feed runs at startup AND on the interval.** Don't add
  side-effects in the feed that assume one tick per real-world event.
- **Multiple `IQueryEntity` registrations colliding on `Name`.** The
  registry throws on construction. If you see `Duplicate entity
  registration for name 'X'` in the logs, you double-registered.
