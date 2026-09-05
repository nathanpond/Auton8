---
name: add-record-event-type
description: Use when adding a new record lifecycle event type (e.g. record.assigned, record.archived) or adding a new field to the record event payload. Walks through every file that has to be touched so subscribers, the SPA Events admin page, and the JetStream stream stay in sync.
---

# Adding a record lifecycle event type

Record events are published from `EfCoreRecordStore` to the Dapr pub/sub topic `record.events` as raw JSON (no CloudEvents envelope). The JetStream stream `workflow-execution` covers the subject filter `record.>`, so any `record.*` topic is already covered — but the catalog, schema, and emit sites must be wired up by hand.

## When to invoke this

- Adding a new event *type* on the existing topic (e.g. `record.assigned`).
- Adding a *field* to the event payload (`RecordEventEnvelope`).
- Introducing a new `record.*` topic (rare — only if the existing `record.events` topic isn't a fit; see the "new topic" note below).

## Steps

### 1. Define the event type constant
File: `src/AutoNate.Web/Services/Records/RecordEventPublisher.cs`

Add a `const string` to `RecordEventTypes`. Use the dotted convention: `record.<noun>` or `record.<noun>.<verb>` (e.g. `record.status.changed`).

### 2. Update the envelope if the schema is changing
Same file: extend `RecordEventEnvelope` with the new field. Every existing call site uses **fully named** arguments (see `EfCoreRecordStore`), so inserting a parameter mid-record is safe. Do **not** try to preserve positional order by appending: the last parameter is optional (`AuditContext? AuditContext = null`) and appending after it will not compile. Field naming on the wire is camelCase via `JsonSerializerDefaults.Web`; pick a C# PascalCase name that maps cleanly.

**Don't re-add audit-context fields**: every event already carries a nested `auditContext` (actorId, ipAddress, userAgent, requestId, correlationId, httpMethod, routePath, authOutcome, etc.) populated automatically by `DaprRecordEventPublisher` via `IRequestContext`. New top-level fields should be record-domain payload, not request-context.

### 3. Document the event in the catalog
File: `src/AutoNate.Web/Services/Events/EventCatalog.cs`

Two updates here, both mandatory:

- If you added a payload field in step 2, append an entry to `RecordPayloadFields[]` with `(name, type, description)`. The SPA Events admin page renders this directly.
- Append an `EventCatalogEntry` to the `Record` `EventCatalogCategory` (the last entry in `Categories`), using `DaprRecordEventPublisher.TopicName` and the const you added in step 1. Fill out `Summary`, `FiresWhen` (which method commits the underlying state change), and `PayloadHighlights` (anything non-obvious about which fields are populated for *this* event).

If you forget the catalog entry, the event publishes fine but doesn't appear in the SPA Events page or the signal-start-event modal.

### 4. Emit from the right store method
File: `src/AutoNate.Web/Services/Records/EfCoreRecordStore.cs`

Search for existing `await eventPublisher.PublishAsync(new RecordEventEnvelope(...))` calls — there are four (Created, Updated, StatusChanged, Deleted/Updated-on-restore). Add the new emit *after the EF transaction commits*, not before. Failures inside `PublishAsync` are logged but don't roll back; do not change that contract.

The `ActorId` should come from the same `ClaimsPrincipal`-derived id the surrounding method uses; `SourceAppId` is `_sourceAppId` (the field on the store, derived from `DaprOptions.AppId`). Leave the `AuditContext` parameter as default — `DaprRecordEventPublisher` fills it in from `IRequestContext`.

### 5. Add tests
- Unit test the emit path with a fake `IRecordEventPublisher` (look at the existing record store tests for the pattern; they capture envelopes into a list).
- If the event surfaces in the SPA, add an E2E assertion that the new entry shows up on the Events admin page (it reads `EventCatalog.Categories` via an API endpoint).

## Adding a brand-new topic (rare)

Only if the new event genuinely doesn't fit `record.events`:

1. Add a new `TopicName` const on `DaprRecordEventPublisher` (or a new publisher class).
2. Append a new `EventCatalogTransport` entry in `EventCatalog.Transports`.
3. **No JetStream change needed** as long as the subject starts with `record.` — the stream's subject filter is `record.>`. If the subject is outside that prefix, edit `DesiredStreams` in `src/AutoNate.Web/Services/Nats/NatsStreamProvisioner.cs`, and read the comment there about overlapping subjects (JetStream rejects them across streams).

## Wire format reminder

Subscribers must use `rawPayload=true`. Don't add CloudEvents framing — the publish URL already passes `metadata.rawPayload=true` and the entire Flowable telemetry topic relies on the same convention.


## Corrections (audit, 2026-09-05)

**There are six lifecycle events, not four** — Created, Updated, StatusChanged,
**AssigneesChanged**, **Purged**, and Deleted/Restored, all in `EfCoreRecordStore`.

**The catalog category is the 5th of 21, not the last** ("Data Stores" is last). And
record events span **two** categories: lifecycle events under "Record", and the view
events (`Viewed` / `ListViewed` / `Searched` / `HistoryViewed`) under a separate
**"View events (information access)"** category. A new `record.*.viewed` type appended
to "Record" lands under the wrong heading in the SPA.

**Not every event is emitted from the store.** Four of the eleven `RecordEventTypes`
are emitted from **endpoints** — the view events. If you are adding one of those,
step 4 does not apply.

**The test fake already exists** — `RecordingRecordEventPublisher`, wired by default
in `AutoNateWebApplicationFactory` and exposed as `factory.RecordedRecordEvents`.

**Free fan-out, and a volume hazard.** `RecordChannelResolver` is event-type-agnostic
— it keys on `recordId` / `assigneeIds` — so a new record event automatically reaches
every SPA subscriber on `record:{id}`, `records:visible` and
`records:assigned-to:{userId}` with no registration. Convenient, but consider the
volume before adding a high-frequency event.
