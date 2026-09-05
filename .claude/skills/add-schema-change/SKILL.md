---
name: add-schema-change
description: Add or change a database table, column, index or seed in Auton8. Use when asked to "add a table", "add a column", "add an index", "write a migration", "change the schema", or when a feature needs new persistence. Covers where the batch goes, the ordering constraints that are not inferable from the code, and the four traps that have already bitten this codebase.
---

# Adding a schema change

All of Auton8's schema lives in
`src/AutoNate.Web/Persistence/DatabaseSchemaInitializer.cs` — around 4,800 lines
executed as ~78 ordered batches on startup, plus
`src/AutoNate.Web/Persistence/Sql/BaseSchema.sql`, an embedded resource applied
first.

There are no EF migrations. That was a deliberate decision (issue #24): the
batches are hand-written and idempotent, and moving to EF would mean porting all
of it plus adopting existing installs into a migration history. `dotnet-ef` stays
pinned in `dotnet-tools.json`; nothing here forecloses the move.

**Treat every path, line number and symbol below as a claim that may have
rotted.** Verify before following, and if code has moved, fix this skill in the
same commit as the change.

## Which kind of change are you making?

This is the first decision and getting it backwards is the defect class behind
several existing `*_v1` keys.

**A schema batch** creates or alters structure. It is written to be safe to run
forever — `CREATE TABLE IF NOT EXISTS`, `ADD COLUMN IF NOT EXISTS`,
`CREATE INDEX IF NOT EXISTS` — and it is recorded in `schema_versions`, so after
the first successful boot it is skipped.

**A one-shot data migration** changes rows: a backfill, a rename, retiring a
seeded row. It must never run twice, and it gates itself:

```sql
IF NOT EXISTS (SELECT 1 FROM auth_seed_state WHERE key = 'your_thing_v1') THEN
    -- ... do the work ...
    INSERT INTO auth_seed_state (key, applied_at_utc)
    VALUES ('your_thing_v1', NOW())
    ON CONFLICT (key) DO NOTHING;
END IF;
```

**The ledger deliberately does not skip a batch whose SQL mentions
`auth_seed_state`.** That batch owns its own re-run semantics, and clearing its
marker is how an operator (and `RebrandMigrationTests`) re-enables it. If the
ledger skipped it too, clearing the marker would silently do nothing.

## Adding a batch

1. Declare the SQL as a `private const string XxxSql` beside its neighbours.
2. Call it from `RunSchemaBatchesAsync`, in the right place (see ordering below):

```csharp
await ApplyStepAsync(dbContext, applied, nameof(XxxSql), XxxSql, cancellationToken);
```

`nameof` gives the ledger a stable, greppable step name. Do not pass a literal.

3. Add the EF entity to the matching `AutoNateDbContext` partial under
   `src/AutoNate.Web/Persistence/`, if the table is queried through EF.
4. Extend `BaseSchema.sql` **only** for foundational tables that everything else
   assumes. Anything layered on top belongs in a batch.

## Ordering constraints that are not inferable

`RunSchemaBatchesAsync` is ordered, and three positions are load-bearing:

- **`EnsureBootstrapAdminAsync` runs mid-sequence, not at the end.** Two later
  seeds (`DocumentsMenuItemSeedSql`, `ContentSampleProjectSeedSql`) attribute
  their rows to the oldest `local_users` row and `RETURN` silently when there is
  none. With the admin created after them, a fresh install came up with no
  Documents nav item and no sample project, and nothing failed to say so. It must
  run after `AuthorizationSchemaSql` and before the first seed that needs an
  actor.
- **`PluginReaderLockdownSql` runs last.** Every table has to exist before the
  credential tables can be taken back off `plg_readers`.
- **The base schema runs first**, before everything. `WorkflowVersioningSql`
  opens with `ALTER TABLE workflow_models`, which fails on a database where
  nothing created it.

## Two traps that have already cost time

### Braces, and which execution path you are on

EF's `ExecuteSqlRawAsync` runs SQL through `string.Format` first. That means:

- **Inline batches are written FOR the format pass.** At the time of writing there are 36 occurrences of
  `'{{}}'::jsonb` — doubled so the format pass collapses them to `{}`. Write
  single braces in an inline batch and it fails with
  `22P02: invalid input syntax for type json`.
- **`BaseSchema.sql` is the opposite.** It is an external `.sql` file that must
  stay valid SQL on its own, so it has single braces and is executed with
  `bypassFormatting: true`. Put it through the format pass and it fails with
  `Failure to parse near offset NNNN. Expected an ASCII digit.`

Both directions were observed as real failures. If your batch contains a JSONB
default, a regex, or a `format()` call, this applies to you.

### Cluster-wide objects are not covered by the advisory lock

`EnsureAsync` holds a Postgres advisory lock, so two hosts initialising one
database is serialised. **Roles are not protected by it.** `pg_advisory` keys are
scoped to a database and `pg_roles` is a cluster-wide shared catalog, so two
hosts owning *different* databases take different locks, both succeed
immediately, and race each other. Anything touching a role needs its own
exception handler:

```sql
DO $$
BEGIN
    CREATE ROLE plg_readers NOLOGIN;
EXCEPTION WHEN duplicate_object OR unique_violation THEN
    NULL;
END $$;
```

This reproduced on CI in one test out of 1,666 after passing locally.
`RoleCreationRaceTests` pins it.

## Testing it

The fixtures apply **only** `BaseSchema.sql` — `PostgresTestDatabase`'s bootstrap
list has exactly one entry. So a change to *that file* is exercised by every test,
but a **batch** runs only in tests that boot the host through
`DatabaseSchemaInitializer.EnsureAsync`. A test using `PostgresTestDatabase` directly
sees the base schema and nothing else, and hits `relation "x" does not exist` for
anything a batch created. The fixture that *does* run batches is
`AutoNateWebApplicationFactory`, which creates that same database and then boots
`Program` → `PrimaryDatabaseInitializer` → `EnsureAsync`; `CreateOn(database)` gives
you a second host over the same database, which is the "restart" primitive
`RebrandMigrationTests` uses. Beyond that:

- A new table or column that a feature reads gets its own endpoint or store test.
- A one-shot migration gets a test that **rewinds and re-runs it** —
  `RebrandMigrationTests` is the model: set the old values, clear the
  `auth_seed_state` key, restart, assert the migration ran. And assert the
  opposite direction too: that a value an administrator chose is left alone.
- Run the full backend suite. It needs the compose services:

```bash
cd infra && docker compose -p infra up -d postgres nats nats-init redis
cd .. && AUTONATE_POSTGRES_PASSWORD='Your_password123!' dotnet test tests/AutoNate.Web.Tests
```

**Use the `trx` logger when something fails.** `console;verbosity=minimal` prints
a count and a stack trace and hides the actual message — with ~70 batches, a
single bad one can fail hundreds of tests and the console tells you nothing about
which:

```bash
dotnet test tests/AutoNate.Web.Tests --logger "trx;LogFileName=/tmp/r.trx"
```

## Worked example

Adding `record_pins`, a table with a JSONB column:

```csharp
private const string RecordPinsSchemaSql =
    """
    CREATE TABLE IF NOT EXISTS record_pins (
        id UUID PRIMARY KEY,
        record_id UUID NOT NULL REFERENCES records (id) ON DELETE CASCADE,
        user_id UUID NOT NULL,
        options JSONB NOT NULL DEFAULT '{{}}'::jsonb,   -- doubled: format pass
        pinned_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
    );

    CREATE UNIQUE INDEX IF NOT EXISTS ux_record_pins_record_user
        ON record_pins (record_id, user_id);
    """;
```

Called after `RecordsSchemaSql` (it references `records`):

```csharp
await ApplyStepAsync(dbContext, applied, nameof(RecordPinsSchemaSql), RecordPinsSchemaSql, cancellationToken);
```

Then the EF entity — which is **three** edits, not one: the POCO in `src/AutoNate.Web/Persistence/Scaffolded/<Name>.cs`, a `public virtual DbSet<T>` in `AutoNateDbContext.cs`, and an explicit `modelBuilder.Entity<T>(…)` block in `OnModelCreating` with `.ToTable("snake_case")`, `.HasKey(…).HasName("x_pkey")` and a `HasColumnName` for **every** property. Skip the third and EF silently looks for PascalCase tables and columns. (There is no `AutoNateDbContext.Records.cs`; the partials are `AutoNateDbContext.cs`, `AutoNateDbContext.DataStores.cs` and `AutoNateDbContext.ProjectionCaches.cs`, and the latter two are for their own subsystems.) <!-- verify-ignore: AutoNateDbContext.Records.cs -->

For a **column on an existing table** the DbSet is already there, so it is three
different edits: the POCO property, the `HasColumnName` inside the existing
`modelBuilder.Entity<T>` block, and — the one that is easy to miss —
`PersistenceModelMapper`'s `ToModel` **and** `Apply`. Skip the mapper and the column
persists and reads back from the database but never reaches an API response, with no
compile error and no test failure unless you wrote the round-trip test.

Then a store test.

## Trap 3: BaseSchema.sql re-runs on every boot

`ApplyStepAsync` exempts any step whose SQL contains `auth_seed_state` from the
ledger skip. `BaseSchema.sql` declares `auth_seed_state`, so **the whole file matches
and re-runs on every single start, forever.** The file says so itself:

> *"NOTE: this whole file RE-RUNS ON EVERY BOOT … so every statement in it must be
> safe against a database that predates it. That is why the columns live inside
> CREATE TABLE IF NOT EXISTS … and why the index on them does NOT live here: a
> standalone CREATE INDEX naming `source` fails at parse time on an old database, on
> every start, forever."*

The real constraint is narrower than "put columns in the CREATE TABLE". A bare
`ALTER TABLE … ADD COLUMN IF NOT EXISTS` names no pre-existing column, so it is
trivially re-run safe — and the file already contains **13** of them, including
`workflow_models.default_variables`, which exists *only* as a trailing ALTER.

What cannot live here is a statement that **references** a column: an index, a
`CHECK`, an `UPDATE`. Those fail at parse time on a database that predates the column,
on every boot, forever. That is why the `group_members` provenance columns sit inside
their `CREATE TABLE IF NOT EXISTS` — because of the `CHECK` beside them — and why the
index on them lives in a batch.

So: a plain new column may be a trailing ALTER here; anything referencing it goes in a
batch.

## Trap 4: PostgreSQL parses a whole command before executing any of it

An `ALTER TABLE … ADD COLUMN source` and a `CREATE INDEX … (source)` **cannot share
one command**. Postgres parses every statement first, so the index fails at parse
time with `column "source" does not exist` — **on an existing database only**. A
fresh database bootstraps from `BaseSchema.sql`, which already declares the column.

**Caveat, measured 2026-09-05: this does not reproduce on the current stack.** The
combination above was run against PostgreSQL 16.15 through `ExecuteSqlRawAsync` (EF
Core 9 / Npgsql 9.0.3) with the column absent beforehand, and succeeded — as does the
same shape via `psql`. With no parameters the command goes over the simple query
protocol, where each statement is analysed immediately before its own execution. Taken
literally the rule would also condemn this skill's own worked example and
`WorkflowVersioningSql`, both of which create a table and an index on it in one const
and ship fine.

Keep the two-constant split as convention — it costs nothing and the incident that
motivated it was real. But do not expect the failure, and if you ever do see
`column "x" does not exist` from a batch that plainly adds it, this is the first thing
to suspect.

**And it is testable**, contrary to what this skill said before: drop the column from
a fixture database, run `EnsureAsync`, and assert both the column and its index exist.
`BaseSchemaSingleSourceTests` already does the harder half.

The fix is two constants applied back to back — see
`GroupMemberProvenanceColumnsSql` and `GroupMemberProvenanceConstraintsSql`, whose
separation exists for exactly this reason and carries the comment
*"Separate command, so the columns above already exist when this is parsed."*

## If your table holds secrets, add it to the lockdown

`plg_readers` is granted `SELECT ON ALL TABLES IN SCHEMA public` **plus**
`ALTER DEFAULT PRIVILEGES … GRANT SELECT ON TABLES`, so **every table you add is
readable by every installed plugin by default.** `PluginReaderLockdownSql` revokes
only four named tables.

If your table holds credentials, hashes, tokens or encrypted secrets, add it to that
array. (Note the lockdown currently only runs on a database's first boot — see
GHSA-fxx3-gpxv-32qq — so on an upgraded install adding it there is necessary but not
yet sufficient. Check whether that advisory has been remediated.)

