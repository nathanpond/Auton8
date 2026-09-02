---
name: add-schema-change
description: Add or change a database table, column, index or seed in Auton8. Use when asked to "add a table", "add a column", "add an index", "write a migration", "change the schema", or when a feature needs new persistence. Covers where the batch goes, the ordering constraints that are not inferable from the code, and the two traps that have already bitten this codebase.
---

# Adding a schema change

All of Auton8's schema lives in
`src/AutoNate.Web/Persistence/DatabaseSchemaInitializer.cs` — around 4,500 lines
executed as ~70 ordered batches on startup, plus
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

The fixtures apply the same embedded `BaseSchema.sql` the application does, so a
schema change is exercised by the whole suite automatically. Beyond that:

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

Then the EF entity in `AutoNateDbContext.Records.cs`, and a store test.
