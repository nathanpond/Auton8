# Signal Start Event — Record Type Filter

**Status:** Implemented
**Date:** 2026-05-04
**Owner:** npond

## Implementation Notes

Implemented across phases between 2026-05-04 and 2026-05-05. Notable nuances surfaced during execution:

- **Public validation API.** `WorkflowBpmnXml.Validate(...)` referenced in the spec is actually `WorkflowBpmnXml.ValidateProcess(...)` returning `WorkflowBpmnValidationResult` with `.Errors` and `.Warnings`. The publish-time DB-aware warning lives one level up in the `/api/workflows/prepare` endpoint handler.
- **EventCatalog topic vs. category.** Read-side audit events (`record.viewed`, `record.list.viewed`, `record.searched`, `record.history.viewed`) share the `record.events` topic but use a different envelope (audit context, not `RecordEventEnvelope`). They intentionally stay unflagged. The `CarriesRecordType` flag is set per-entry on the six lifecycle events in the "Record" category, not by topic.
- **Idempotency `businessKey`.** The dispatcher does NOT yet set a deterministic business key from `BusWatcherMessage.Headers`. At-least-once delivery may produce duplicate starts on Dapr redelivery. Acceptable for v1; deferred until a concrete duplication is observed.
- **Audit-event invalidation of `RecordTypeShortCodeCache`.** Deferred: the cache refreshes only on app start. `IActionHub.AuditEventPublished` is a viable in-process hook for `record-type.created/updated/archived/restored` events when needed; documented as TODO inside the cache class.
- **Resolver guard in dispatcher.** `IRecordTypeShortCodeResolver.TryGetShortCode` is called inside a `try`/`catch` so a future resolver implementation that throws cannot tear down dispatch.
- **`BroadcastSignalAsync`.** Removed from the dispatch path entirely. Still exists on `IFlowableClient` for completeness; can be deleted in a follow-up sweep.
- **Filter set comparison is `StringComparer.Ordinal`.** Match the rest of the AutoNate codebase. Filter set on each registration is a `FrozenSet<string>` to make the immutability explicit.
- **Tests:** 738 passing post-implementation (was ~720 pre). Includes one end-to-end integration test (`SignalStartRecordTypeFilterIntegrationTests`) exercising publish → registry refresh → dispatcher → start.

## Summary

Today every BPMN signal start event in AutoNate fires for every payload that
matches its signal name (e.g. `record.created`). When two record types both
publish the same event, the workflow can't tell them apart without a script
task gate inside the process. This design adds an optional **record-type
filter** to signal start events: pick one or more record types in the studio,
and the dispatcher only starts the workflow when the inbound payload's
`recordTypeId` matches.

The filter is **purely additive**. Empty filter = current behavior (match all
records). Non-record signals are not affected.

## Design Decisions

| Question | Decision | Rationale |
| --- | --- | --- |
| Which signals can be filtered? | Any signal whose payload carries `recordTypeId` (Q1=C). | Generic, future-proof; topic-agnostic. |
| Single record type or multiple per event? | Multi-select (Q2=B). | Same UI cost as single; avoids workflow duplication. |
| Identifier in BPMN XML | `ShortCode` (Q3=B). | Portable across environments; matches existing `userFormShortCode` pattern. |
| Dispatch architecture | Replace broadcast with per-key dispatch (Q4=A). | Only correct way to fire a subset of workflows on the same signal name. |
| Intermediate catches in scope? | No, start events only this iteration (Q5=A). | Narrows scope; rework leaves room for later expansion. |
| Editor visibility | Hidden unless the chosen Event Type is known to carry `recordTypeId`. | Keeps non-record workflows uncluttered; catalog flag drives the rule. |

## BPMN XML Representation

A new Flowable-namespaced attribute on the **`<signalEventDefinition>`**
element (per-event, *not* on the shared `<signal>` root, so two start events
with the same signal name can have different filters):

```xml
<bpmn:startEvent id="StartEvent_1">
  <bpmn:signalEventDefinition signalRef="Signal_recordCreated"
                              flowable:recordTypeShortCodes="asset,vehicle"/>
</bpmn:startEvent>
```

Comma-separated shortcodes. Attribute omitted entirely when no filter is
configured (existing workflows are byte-identical post-migration). Round-trips
through `bpmn-moddle` via `$attrs`, the same mechanism the existing
`flowable:topic` attribute uses on the `<signal>` root.

The `<signal>` root element keeps its current shape (`name`, `flowable:topic`)
— only the per-event reference changes.

## Data Model

### Backend: `WorkflowSignalRegistration`

Today (`src/AutoNate.Web/Services/Workflow/WorkflowElementSnapshot.cs:27`):

```csharp
public sealed record class WorkflowSignalRegistration(string SignalName, string Topic);
```

After:

```csharp
public sealed record class WorkflowSignalRegistration(
    string SignalName,
    string Topic,
    string ProcessDefinitionKey,                 // NEW — needed for per-key start
    IReadOnlySet<string> RecordTypeShortCodes);  // NEW — empty = match all
```

The dedup key in `WorkflowBpmnXml.ExtractSignalRegistrations`
(`src/AutoNate.Web/Services/Workflow/WorkflowBpmnXml.cs:639`) becomes
`(name, topic, processKey)` — same signal name on different process keys
produces separate registrations. (Today the dedup collapses them; it didn't
matter under broadcast.)

### Backend: registry interface

`IWorkflowSignalRegistry` gains:

```csharp
IReadOnlyList<WorkflowSignalRegistration> GetRegistrationsForTopic(string topic);
```

The existing `GetSignalNamesForTopic` stays for callers that only need the
topic-subscription list (e.g. `DaprStreamingSubscriber`).

### Frontend: `WorkflowElementSnapshot`

`src/AutoNate.Spa/src/api/workflows.ts` mirror gains:

```ts
recordTypeShortCodes?: string[] | null;
```

`SignalStartEventEditor` in `WorkflowStudio.tsx` gains:

```ts
recordTypeShortCodes: string[];
```

## Editor UI

**Location:** `src/AutoNate.Spa/src/pages/workflow/WorkflowStudio.tsx`,
`SignalStartEventModal` (line 2094).

**Field placement:** below "Event Type", labelled **"Record types (optional)"**.

**Visibility rule:** the field renders only when the currently typed
`(topic, eventType)` combo resolves to an `EventCatalogEntry` whose new
`carriesRecordType` flag is `true`.

```
Event Name (optional)     [____________________]
Topic                     [record.events       ▼]
Event Type                [record.created      ▼]
─── shows only when (topic, eventType) carries recordTypeId ───
Record types (optional)   [☑ Asset  ☐ Person  ☐ Vehicle  …]
                          "Empty = all record types match. When set,
                           only payloads whose recordTypeId matches one
                           of these will start this workflow."
```

**Widget:** chip-style multi-select matching the candidate-users/groups
picker pattern already used in the studio. Stores `string[]` of shortcodes.

**Data source:** `useRecordTypes()` (already exists). Excludes archived types
from the picker but keeps them resolvable when an existing workflow already
references one (suffixed `(archived)` in the list).

**Mid-edit safety:** when the user has a filter set on a record-carrying
event type and changes the eventType to one that doesn't carry a record
type, the existing selection is **preserved on the editor** but **stripped
on Apply**. A single-line warning shows next to the Event Type field while
the mismatch is in flight:

> *This event type doesn't carry a record type — the configured filter will be cleared when you apply.*

**Freeform / unknown event types:** field stays hidden. The dispatcher still
honors any `recordTypeShortCodes` attribute it finds in the BPMN (so
hand-edited XML or imports work), but the studio is conservative about
exposing the picker.

**Studio plumbing changes (`src/AutoNate.Spa/src/lib/bpmn/workflow.js`):**

- `describeSignalStartEvent` (line 1219) reads `flowable:recordTypeShortCodes`
  off the signalEventDefinition's `$attrs`, splits on `,`, returns `string[]`
  (or `null` when absent).
- `updateSignalStartEventProperties` (line ~1410) writes via
  `writeFlowableAttribute(signalEventDefinition, "recordTypeShortCodes",
  joined)` with the value passed as `null` when the array is empty (omits
  the attribute).

## Dispatch Architecture

`WorkflowSignalDispatcher` (`src/AutoNate.Web/Services/Signals/WorkflowSignalDispatcher.cs`)
is rewritten:

```
HandleAsync(message):
    registrations = registry.GetRegistrationsForTopic(message.Topic)
    matching = registrations.where(r => r.SignalName == payload.eventType)
    if matching is empty: return

    payloadRecordTypeId = TryReadGuid(payload, "recordTypeId")  // null if absent
    resolvedShortCode = (payloadRecordTypeId is null)
        ? null
        : recordTypeStore.GetShortCodeById(payloadRecordTypeId)  // cached

    for each registration in matching:
        if registration.RecordTypeShortCodes.Count == 0:
            start  // unfiltered — preserves today's behavior
        elif resolvedShortCode is not null and
             registration.RecordTypeShortCodes.Contains(resolvedShortCode):  // Ordinal compare
            start
        else:
            skip

        start := flowableClient.StartProcessInstanceAsync(
                    registration.ProcessDefinitionKey,
                    businessKey: businessKeyOrNull,
                    variables: { eventData: rawPayloadJson })
```

### Key properties

- **`BroadcastSignalAsync` is removed from the dispatch path entirely.**
  Verified the dispatcher is the sole live caller (other references are in
  `FlowableClient` itself, the test stub, and `FlowableClientTests`). The
  method stays declared on `IFlowableClient` for now — its removal can be a
  follow-up sweep after the rebuild stabilises.
- **Filtered + payload missing `recordTypeId` → skip.** Configuring a filter
  expresses intent to handle record events; non-record payloads cannot
  satisfy the filter and are excluded.
- **Per-process error isolation.** A Flowable failure on one start does not
  abort sibling registrations; each is wrapped in its own try/catch with
  structured error logging.
- **Idempotency via best-effort `businessKey`.** `BusWatcherMessage.Headers`
  is inspected for a stable message identifier (CloudEvents `id`, falling
  back to Dapr's `traceparent` segment). When present, the dispatcher sets
  `businessKey = $"signal:{stableId}:{processKey}"` so a redelivered message
  collapses to one start per process. When the headers carry no stable id,
  `businessKey` is omitted — at-least-once delivery applies and a duplicate
  message could double-start that workflow. Acceptable for v1; tighter
  dedup is a follow-up if it becomes a problem in practice.
- **Record-type lookup cache.** A small in-memory `Guid → ShortCode` cache
  populated from `RecordType` rows. Invalidated on
  `record-type.created/updated/archived/restored` audit events (already
  published).

### Intermediate catch path (new, no filter)

A new `IFlowableClient.SignalExecutionAsync(executionId, variables)` calls
`PUT /runtime/executions/{id}` with `{action: "signalEventReceived",
variables: {…}}`. The dispatcher queries
`/runtime/executions?signalEventSubscriptionName={name}` and signals each
returned execution. No filter applied (Q5=A — out of scope this iteration).

## Validation

Server-side `WorkflowBpmnXml.Validate`:

- **Warning** (publish proceeds): `flowable:recordTypeShortCodes` references
  a shortcode that doesn't resolve to a current `RecordType`. Surfaced via
  the existing validation envelope so the studio shows it next to Publish.
  Permits cross-environment exports/imports where the destination DB has
  yet to seed the type.
- **Error** (publish blocked): `flowable:recordTypeShortCodes` set on a
  `<signalEventDefinition>` whose enclosing element is not a `<startEvent>`.
  Catches hand-edits that bypass the studio.
- Existing signal-name presence validation unchanged.

## EventCatalog change

`EventCatalogEntry` (positional record in
`src/AutoNate.Web/Services/Events/EventCatalog.cs`) gains a new
`bool CarriesRecordType` parameter, defaulted to `false` so existing
declarations don't need to change.

- The six entries on `record.events` (`record.created`, `record.updated`,
  `record.status.changed`, `record.assignees.changed`, `record.restored`,
  `record.deleted`) set `CarriesRecordType: true`.
- Plugins or future emitters opt in by setting the flag on their catalog
  entry. The flag is purely UI-driving; the dispatcher works off the actual
  payload contents, so unknown-but-record-carrying events still filter
  correctly when configured via raw BPMN.

The flag is surfaced on the SPA-facing event-catalog DTO
(`useEventCatalog`'s response shape) so the modal can read it without an
extra round-trip.

## Testing

### Unit

- **`WorkflowBpmnXmlTests`** — round-trip the new attribute (parse → snapshot
  → write → re-parse); absence stays absence; two start events sharing one
  signal name with different filters produce two registrations; whitespace
  and casing handled (trim, ordinal-ignore-case match); malformed/empty
  list normalised.
- **`WorkflowSignalDispatcherTests`** — empty filter starts on every payload;
  `{asset}` filter starts on Asset, skips on Vehicle, skips on payload
  missing `recordTypeId`, skips on unknown shortcode; multiple registrations
  on the same name+topic with different filters fan out correctly; one
  Flowable failure doesn't block siblings; idempotent businessKey prevents
  double-start on redelivery.
- **`EfCoreWorkflowSignalRegistryTests`** — extraction picks up
  `RecordTypeShortCodes` and `ProcessDefinitionKey`; refresh after publish
  replaces stale entries.

### Integration (`tests/AutoNate.Web.Tests`)

- Publish workflow A with filter=Asset and workflow B with no filter; fire
  a Vehicle `record.created` → only B starts. Fire an Asset `record.created`
  → both A and B start.
- Filter referencing a shortcode that doesn't exist → publish succeeds with
  warning; runtime never starts the workflow (no payload will match).

### SPA

- `SignalStartEventModal` test: field hidden for non-record event type,
  visible for `record.created`, selection preserved on eventType change,
  stripped on Apply with mismatched eventType, persisted into the saved
  snapshot.

### E2E (Playwright, optional)

- Drop a signal start event in the studio, configure record-type filter,
  publish, post a Dapr `record.created` for the matching type → instance
  starts. Repeat with non-matching type → no instance.

## Observability

- Dispatcher info log per dispatch:
  `Filtered signal {SignalName} on topic {Topic} (recordTypeId={Id}, shortCode={Code}, matched={N}/{Total})`.
- Existing `signal.dispatched` counter gains a `filter_applied: bool` dimension.
- New `signal.filtered_out` counter increments per skipped registration, with
  reason dimension (`payload_missing_record_type_id`, `record_type_unknown`,
  `record_type_excluded_by_filter`).

## Migration & Rollout

- **No DB migration.** Registry is rebuilt from BPMN on every publish.
- **No data migration.** Existing workflows have no
  `flowable:recordTypeShortCodes` → empty filter → match-all → identical to
  today.
- **Risky change is the dispatcher rebuild.** The PR splits into two commits:
  1. Per-key dispatch with always-empty filter (behavior-preserving
     refactor; full integration tests must pass green pre and post).
  2. Filter parsing + application + UI.
  Each commit independently testable; commit 1 is reversible without
  affecting commit 2's observability changes.

## Out of Scope

- **Intermediate signal catches with filters** (Q5=A). The dispatcher rework
  leaves the integration point in place; adding filters there is a
  follow-up.
- **Generic JSON-path payload filters.** Q1=C is satisfied by recognising
  `recordTypeId` specifically. A general "filter on any JSON path equals any
  value" feature is a separate, larger design.
- **Record-type-aware boundary events.** Same shape as intermediate catches;
  out of scope until requested.

## File Inventory (touched)

| File | Change |
| --- | --- |
| `src/AutoNate.Web/Services/Workflow/WorkflowElementSnapshot.cs` | Add `RecordTypeShortCodes` to snapshot; expand `WorkflowSignalRegistration`. |
| `src/AutoNate.Web/Services/Workflow/WorkflowBpmnXml.cs` | Read/write `flowable:recordTypeShortCodes`; new validation rules. |
| `src/AutoNate.Web/Services/Workflow/IWorkflowSignalRegistry.cs` | Add `GetRegistrationsForTopic`. |
| `src/AutoNate.Web/Services/Workflow/EfCoreWorkflowSignalRegistry.cs` | Implement new method; index registrations including filter and process key. |
| `src/AutoNate.Web/Services/Signals/WorkflowSignalDispatcher.cs` | Rewrite per-key; remove broadcast on start-event path. |
| `src/AutoNate.Web/Services/Flowable/IFlowableClient.cs` + `FlowableClient.cs` | Add `SignalExecutionAsync`; `StartProcessInstanceAsync` already exists. |
| `src/AutoNate.Web/Services/Records/RecordTypeShortCodeCache.cs` | New small cache (Guid → ShortCode) with audit-event invalidation. |
| `src/AutoNate.Web/Services/Events/EventCatalog.cs` | Add `carriesRecordType` flag; set on record.* entries. |
| `src/AutoNate.Spa/src/lib/bpmn/workflow.js` | Read/write `flowable:recordTypeShortCodes`; extend snapshot. |
| `src/AutoNate.Spa/src/api/workflows.ts` | Mirror new `recordTypeShortCodes` field. |
| `src/AutoNate.Spa/src/pages/workflow/WorkflowStudio.tsx` | Editor type, modal field, conditional visibility, mid-edit safety. |
| `tests/AutoNate.Web.Tests/StubFlowableClient.cs` | Stub the new `SignalExecutionAsync`. |
| `tests/AutoNate.Web.Tests/...` | New test files per "Testing" section. |
