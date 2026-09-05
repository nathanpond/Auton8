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
Same file: extend `RecordEventEnvelope` with the new field.

**Append an optional parameter at the end** — `IReadOnlyList<Guid>? Foo = null`. C# only
forbids a *required* parameter after an optional one, so appending an optional one
compiles fine, and it preserves the generated `Deconstruct` arity. Mid-record insertion
also works (every call site uses fully named arguments, see `EfCoreRecordStore`) but
reorders `Deconstruct`, so prefer appending. Field naming on the wire is camelCase via `JsonSerializerDefaults.Web`; pick a C# PascalCase name that maps cleanly.

**Don't re-add audit-context fields**: every event already carries a nested `auditContext` (actorId, ipAddress, userAgent, requestId, correlationId, httpMethod, routePath, authOutcome, etc.) populated automatically by `DaprRecordEventPublisher` via `IRequestContext`. New top-level fields should be record-domain payload, not request-context.

### 3. Document the event in the catalog
File: `src/AutoNate.Web/Services/Events/EventCatalog.cs`

Two updates here, both mandatory:

- If you added a payload field in step 2, append an entry to `RecordPayloadFields[]` with `(name, type, description)`. The SPA Events admin page renders this directly.
- Append an `EventCatalogEntry` using `DaprRecordEventPublisher.TopicName` and the const from step 1. Fill out `Summary`, `FiresWhen` (which method commits the underlying state change), and `PayloadHighlights` (anything non-obvious about which fields are populated for *this* event).

  **Which category:** `Record` is the **5th of 21** entries in `Categories`, not the last — the last is "Data Stores". And a **view** event (`record.*.viewed` and friends) does not go in `Record` at all: there is a dedicated **"View events (information access)"** category, and every `*.viewed` event lives there regardless of domain. Put one in `Record` and it renders under the wrong heading in the SPA.

  **Set `CarriesRecordType: true`** — the 6th positional parameter, defaulting to `false`. It is what makes the record-type shortcode filter appear on the signal-start-event modal. `ViewEventPublishingTests` asserts *every* event in the `Record` category sets it, so omitting it is a red test with a message you cannot decipher without reading `EventCatalog`.

If you forget the catalog entry, the event publishes fine but doesn't appear in the SPA Events page or the signal-start-event modal.

### 4. Emit from the right store method
File: `src/AutoNate.Web/Services/Records/EfCoreRecordStore.cs`

There are **seven** lifecycle event types — Created, Updated, StatusChanged, AssigneesChanged, Purged, Deleted and Restored — plus four **view** types (Viewed, ListViewed, Searched, HistoryViewed) which are emitted from **endpoints**, not from the store. `Deleted` and `Restored` share one emit site through a ternary, so `EfCoreRecordStore` has six `PublishAsync` calls covering the seven.

If you are adding a view event, this step does not apply — emit from the endpoint, and see the view-event category note in step 3. Add the new emit *after the EF transaction commits*, not before. Failures inside `PublishAsync` are logged but don't roll back; do not change that contract.

The `ActorId` should come from the same `ClaimsPrincipal`-derived id the surrounding method uses; `SourceAppId` is `_sourceAppId` (the field on the store, derived from `DaprOptions.AppId`). Leave the `AuditContext` parameter as default — `DaprRecordEventPublisher` fills it in from `IRequestContext`.

### 5. Add tests
- Unit test the emit path. **There are two classes named `RecordingRecordEventPublisher`** and they are not interchangeable: the nested `PostgresTestDatabase.RecordingRecordEventPublisher` is what store-level tests use, and the top-level one in `tests/AutoNate.Web.Tests/` is what `AutoNateWebApplicationFactory` installs and exposes as `factory.RecordedRecordEvents` for endpoint-level tests. Pick by test level.
- ⚠️ **A new event type breaks an existing test, by design.** `ViewEventPublishingTests` pins the record event types against a hardcoded list and asserts every one sets `CarriesRecordType`. Update that list deliberately — it is the guard that stops an event being added without a catalog entry, so a change to it is a claim that you added one.
- If the event surfaces in the SPA, add an E2E assertion that the new entry shows up on the Events admin page (it reads `EventCatalog.Categories` via an API endpoint).

## Adding a brand-new topic (rare)

Only if the new event genuinely doesn't fit `record.events`:

1. Add a new `TopicName` const on `DaprRecordEventPublisher` (or a new publisher class).
2. Append a new `EventCatalogTransport` entry in `EventCatalog.Transports`.
3. **No JetStream change needed** as long as the subject starts with `record.` — the stream's subject filter is `record.>`. If the subject is outside that prefix, edit `DesiredStreams` in `src/AutoNate.Web/Services/Nats/NatsStreamProvisioner.cs`, and read the comment there about overlapping subjects (JetStream rejects them across streams).

## Wire format reminder

Subscribers must use `rawPayload=true`. Don't add CloudEvents framing — the publish URL already passes `metadata.rawPayload=true` and the entire Flowable telemetry topic relies on the same convention.

## Free fan-out, and its volume hazard

`RecordChannelResolver` is event-type-agnostic — it keys on `recordId` and
`assigneeIds`, never on the event type. So a new record event automatically reaches
every SPA subscriber on `record:{id}`, `records:visible` and
`records:assigned-to:{userId}` with no registration at all. Convenient; consider the
volume before adding a high-frequency event.
