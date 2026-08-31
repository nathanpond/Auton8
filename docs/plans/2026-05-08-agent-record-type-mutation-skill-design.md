# Agent Record-Type Mutation Skill

## Goal

Give the chatbot agent the ability to author and edit record types — the
schema layer that lives one floor above records themselves. The user describes
a desired schema in natural language ("create a Cars type with model, year,
VIN as required, plus a color dropdown"), the agent proposes the change,
narrates it, and only commits after explicit user confirmation.

This is the second mutating skill, slotting in beside `ManageRecordsSkill`.
It deliberately mirrors that skill's shape: dry-run-first contract, structured
proposal envelopes, server-enforced `confirmed: bool` gate, and identical
audit/error patterns.

## Scope

- **In scope:** Create a record type, edit type metadata (name, description,
  icon, color), archive/restore types, add a field, edit a field
  (display name, config, required, sort order), archive/restore fields.
- **In scope:** Inline `fields[]` on `create_record_type` so a complete
  schema can be authored in one confirmed call (the chatbot use case is
  almost always "create a type with these N fields", not "create a bare
  type then add fields one at a time").
- **In scope:** Skill-level authorization checks via `IAuthorizer`, since
  `IRecordTypeStore` does **not** gate by authorizer (today the HTTP
  endpoints are the enforcement point — `RequirePermission(EntityKinds.RecordType, …)`).
- **In scope:** Refusing all mutations on `IsSystem = true` types, since
  no other layer guards system types today.
- **Out of scope:** Renaming a `field_key` (the storage layer treats
  `field_key` as immutable inside `records.values` JSONB). `display_name`
  changes are fine; `field_key` changes would require a data migration we
  are not building here.
- **Out of scope:** Bulk reordering of fields. The `update_record_type_field`
  tool can move one field at a time via `sortOrder`; no batch tool.
- **Out of scope:** Changing a field's `data_type`. Cross-type coercion of
  existing JSONB values is too easy to get wrong silently. The agent must
  archive the old field and add a new one.

## Architecture

| Layer | File(s) | Change |
|---|---|---|
| Skill | `src/AutoNate.Web/Services/Agent/Skills/ManageRecordTypesSkill.cs` (new) | Implements `IAgentSkill` with six tools. Resolves `IRecordTypeStore` and `IAuthorizer` at tool-invocation time. |
| DI registration | `src/AutoNate.Web/Program.cs` | `AddScoped<IAgentSkill, ManageRecordTypesSkill>()` next to `ManageRecordsSkill`. |
| Tests | `tests/AutoNate.Web.Tests/ManageRecordTypesSkillTests.cs` (new) | Mirrors `ManageRecordsSkillTests`: fakes for `IRecordTypeStore` and `IAuthorizer`, assert `confirmed=false` never calls the store, `confirmed=true` does, validation cases short-circuit, system-type guard fires, authorizer denial short-circuits. |
| Stores | none | `IRecordTypeStore` is unchanged. The skill is a translator on top of it. |
| Endpoints | none | The HTTP API for record types is unchanged. |

## Tools

All tools accept `confirmed: bool` (defaults to `false`). When `false`, the
skill builds and returns a `record_type_change_proposal` envelope; when
`true`, it executes the corresponding `IRecordTypeStore` call (after the
authorization and `IsSystem` guards) and returns a
`record_type_change_committed` envelope.

### 1. `create_record_type`

Create a new record type, optionally with a starter set of fields.

```json
{
  "type": "object",
  "properties": {
    "shortCode":   { "type": "string", "description": "2–8 chars, starts with a letter, then letters or digits." },
    "name":        { "type": "string" },
    "description": { "type": ["string", "null"] },
    "icon":        { "type": ["string", "null"] },
    "color":       { "type": ["string", "null"] },
    "fields": {
      "type": "array",
      "description": "Optional initial fields. Created in one transaction-equivalent batch (see Atomicity).",
      "items": {
        "type": "object",
        "properties": {
          "fieldKey":    { "type": "string", "description": "snake_case, 1–64 chars, starts with a letter." },
          "displayName": { "type": "string" },
          "dataType":    { "type": "string", "enum": ["text","number","date","phone","email","option","boolean"] },
          "config":      { "type": "object", "description": "Per-data-type config; e.g. option requires {choices:[{value,label}]}." },
          "isRequired":  { "type": "boolean" },
          "sortOrder":   { "type": "integer" }
        },
        "required": ["fieldKey","displayName","dataType"]
      }
    },
    "confirmed":   { "type": "boolean" }
  },
  "required": ["shortCode","name"],
  "additionalProperties": false
}
```

**Authorization:** `Actions.Create` on `EntityKinds.RecordType` (kind-level —
type doesn't exist yet). If `fields[]` is non-empty, also checks
`Actions.DefineFields`.

**Atomicity:** `IRecordTypeStore.CreateAsync` saves the type, then we loop
`CreateFieldAsync` for each entry. If any field validation fails partway
through, prior fields stay (the store auto-commits). To keep proposal/commit
honest, we **validate every field's config via `IFieldTypeRegistry.NormalizeConfig`
in the dry-run path** before the user confirms. If somehow a commit fails
midway anyway (e.g. duplicate key race), we surface the partial-state result
in the `record_type_change_failed` envelope so the agent can narrate exactly
what landed.

### 2. `update_record_type`

Edit metadata only.

```json
{
  "type": "object",
  "properties": {
    "typeShortCode": { "type": "string" },
    "name":          { "type": "string" },
    "description":   { "type": ["string", "null"] },
    "icon":          { "type": ["string", "null"] },
    "color":         { "type": ["string", "null"] },
    "confirmed":     { "type": "boolean" }
  },
  "required": ["typeShortCode"],
  "additionalProperties": false
}
```

Update semantics follow `update_record`: a missing property keeps the
current value, an explicit `null` clears (where the column is nullable),
a value sets. `name` cannot be cleared (server raises `RecordTypeValidationException`,
which the skill maps to `record_type_change_failed`).

**Authorization:** `Actions.Edit` on the resolved type instance.

### 3. `set_record_type_archived`

```json
{
  "type": "object",
  "properties": {
    "typeShortCode": { "type": "string" },
    "archived":      { "type": "boolean" },
    "confirmed":     { "type": "boolean" }
  },
  "required": ["typeShortCode","archived"],
  "additionalProperties": false
}
```

**Authorization:** `Actions.Delete` (archiving) or `Actions.Edit` (restoring),
matching `RecordTypeEndpoints` exactly.

### 4. `add_record_type_field`

```json
{
  "type": "object",
  "properties": {
    "typeShortCode": { "type": "string" },
    "fieldKey":      { "type": "string" },
    "displayName":   { "type": "string" },
    "dataType":      { "type": "string", "enum": ["text","number","date","phone","email","option","boolean"] },
    "config":        { "type": "object" },
    "isRequired":    { "type": "boolean" },
    "sortOrder":     { "type": "integer" },
    "confirmed":     { "type": "boolean" }
  },
  "required": ["typeShortCode","fieldKey","displayName","dataType"],
  "additionalProperties": false
}
```

If `sortOrder` is omitted, default to `max(existing.sortOrder) + 10`.

**Authorization:** `Actions.DefineFields` on the resolved type instance.

### 5. `update_record_type_field`

```json
{
  "type": "object",
  "properties": {
    "typeShortCode": { "type": "string" },
    "fieldKey":      { "type": "string" },
    "displayName":   { "type": "string" },
    "config":        { "type": "object" },
    "isRequired":    { "type": "boolean" },
    "sortOrder":     { "type": "integer" },
    "confirmed":     { "type": "boolean" }
  },
  "required": ["typeShortCode","fieldKey"],
  "additionalProperties": false
}
```

`fieldKey` is the lookup, not editable. `dataType` is **deliberately absent** —
see Scope. Any property the agent omits keeps the field's current value;
to mirror the store's `UpdateRecordTypeFieldInput` signature, the skill loads
the current field, layers the patch on top, and passes the merged record into
`UpdateFieldAsync`.

**Authorization:** `Actions.DefineFields` on the resolved type instance.

### 6. `set_record_type_field_archived`

```json
{
  "type": "object",
  "properties": {
    "typeShortCode": { "type": "string" },
    "fieldKey":      { "type": "string" },
    "archived":      { "type": "boolean" },
    "confirmed":     { "type": "boolean" }
  },
  "required": ["typeShortCode","fieldKey","archived"],
  "additionalProperties": false
}
```

**Authorization:** `Actions.DefineFields`. The system-prompt fragment (below)
warns the agent to narrate that archiving a field hides it from forms but
leaves the JSONB values on existing records intact.

## Envelopes

### `record_type_change_proposal` (dry-run)

```json
{
  "kind": "record_type_change_proposal",
  "source": "ManageRecordTypesSkill",
  "data": {
    "operation": "create_type" | "update_type" | "archive_type" | "restore_type"
                | "add_field" | "update_field" | "archive_field" | "restore_field",
    "summary": "Create CAR: 'Car' with 4 fields (model[text*], year[number], vin[text*], color[option])",
    "before": { /* current snapshot, omitted on create */ },
    "after":  { /* projected snapshot */ },
    "fieldChanges": [ /* update_field only — diff per attribute */ ],
    "validation": {
      "ok": true,
      "errors": [ { "code": "...", "message": "..." } ]
    }
  }
}
```

`fieldChanges` for `update_field` mirrors the diff array in `ManageRecordsSkill`:
each entry `{ key, displayName, before, after }`. For `create_record_type` and
`add_record_type_field` only `after` is populated. For `update_record_type`,
`set_record_type_archived`, and `set_record_type_field_archived`, both
`before` and `after` are full snapshots, modeled on the `SerializeTypeSnapshot`
shape in `EfCoreRecordTypeStore`.

### `record_type_change_committed` (commit success)

Always carries `kind`, `source`, `data.operation`. Per-operation payload:

| Operation | Extra fields |
|---|---|
| `create_type` | `id`, `shortCode`, `createdFieldCount` (0 if no inline fields) |
| `update_type` | `id`, `shortCode` |
| `archive_type` / `restore_type` | `id`, `shortCode`, `isArchived` |
| `add_field` | `typeId`, `shortCode`, `fieldId`, `fieldKey` |
| `update_field` | `typeId`, `shortCode`, `fieldId`, `fieldKey` |
| `archive_field` / `restore_field` | `typeId`, `shortCode`, `fieldId`, `fieldKey`, `isArchived` |

### `record_type_change_failed` (validation/commit failure)

Same shape as `record_change_failed` in `ManageRecordsSkill`: includes
`message` and `validation.errors[]`. Used for `RecordTypeValidationException`
and the partial-success case where some inline fields commit but a later one
fails.

### `error` (precondition failure — auth, missing type, system type, bad arg)

Same shape as everywhere else: `{ kind: "error", source, data: { message } }`.

## System-prompt fragment

```
You can author and edit record types via create_record_type / update_record_type /
add_record_type_field / update_record_type_field / set_record_type_archived /
set_record_type_field_archived. ALWAYS call them with confirmed=false first;
the tool returns a structured proposal envelope. Present the summary and any
validation errors to the user, then ASK for explicit confirmation. Only after
plain-language approval ('yes', 'go ahead') re-call with confirmed=true and the
SAME arguments. If you change ANY value between preview and commit, run
confirmed=false again first. Before proposing changes to an existing type, call
list_record_types and describe_record_type so you can show the user a clean diff.
Be aware: archiving a field hides it from forms but does NOT remove existing
records' values for that field — narrate this when archiving. Field data_type
cannot be changed once a field is created; archive the old field and add a new
one instead.
```

## Authorization details

`IRecordTypeStore` is unauthorized today. The HTTP endpoints enforce
permissions via `RequireKindPermission` / `RequirePermission`. The skill
mirrors those gates exactly:

| Tool | Action | Resource |
|---|---|---|
| `create_record_type` | `Create` | `EntityKinds.RecordType` (kind) |
| `create_record_type` (with fields) | `Create` + `DefineFields` | kind + (the new) instance |
| `update_record_type` | `Edit` | type instance |
| `set_record_type_archived` (archive) | `Delete` | type instance |
| `set_record_type_archived` (restore) | `Edit` | type instance |
| `add_record_type_field` | `DefineFields` | type instance |
| `update_record_type_field` | `DefineFields` | type instance |
| `set_record_type_field_archived` | `DefineFields` | type instance |

Authorization is checked **before** mutation in both the dry-run and commit
paths — a denied dry-run shouldn't even narrate "if you confirm, this will
fail". On denial we return an `error` envelope (not `record_type_change_failed`
— the action never started).

## System-type guard

Any tool that targets an existing type fetches it via
`GetByShortCodeAsync(shortCode)` first. If `IsSystem` is `true`, the skill
short-circuits with `error`: `"Record type 'X' is a system type and cannot be modified by the agent."`
This guard fires in both dry-run and commit paths so the agent can narrate
the refusal cleanly.

## Tests

`tests/AutoNate.Web.Tests/ManageRecordTypesSkillTests.cs`. Patterns mirror
`ManageRecordsSkillTests`. Per-tool coverage:

- Dry-run never calls the underlying mutator (`CreateAsync`, `UpdateAsync`,
  `CreateFieldAsync`, `UpdateFieldAsync`, `SetArchivedAsync`,
  `SetFieldArchivedAsync`).
- Commit calls the matching mutator exactly once with mapped input.
- Unknown `typeShortCode` / `fieldKey` returns `error`.
- `IsSystem = true` returns `error` for every mutating tool.
- Authorizer-denied returns `error` and never calls the store.
- `create_record_type` with `fields[]` validates each field's config via the
  field-type registry in dry-run; an invalid `option.choices` surfaces in
  `validation.errors`.
- A `RecordTypeValidationException` thrown from the store maps to
  `record_type_change_failed`, not to `error`.
- `set_record_type_archived` with `archived: true` requires `Actions.Delete`;
  with `archived: false` requires `Actions.Edit` — the fake authorizer is
  configured per-action and asserted.

The fake `IAuthorizer` records every `(kind, action, resourceId)` triple it
sees so tests can assert exact gating decisions.

## Files touched

- **New:** `src/AutoNate.Web/Services/Agent/Skills/ManageRecordTypesSkill.cs`
- **New:** `tests/AutoNate.Web.Tests/ManageRecordTypesSkillTests.cs`
- **Edit:** `src/AutoNate.Web/Program.cs` — one DI line.

No SPA changes: `ToolCallCard` in `AgentSidebar.tsx` renders every tool result
as JSON, and the user-facing narration is the agent's plain-language summary
of the proposal — no envelope-shape coupling on the front end.
