---
name: add-audit-event
description: Use when adding an audit-grade event for a non-record domain (users, groups, roles, permissions, menus, settings, workflow models/executions, auth, etc.) — both for view events on read endpoints and for mutation events that don't merit a bespoke domain publisher. Covers the full path so the event appears on the bus, in the EventCatalog, and is asserted by an endpoint test.
---

# Adding a non-record audit event

For non-record domains, events go through the cross-cutting `IAuditEventPublisher` (see `src/AutoNate.Web/Services/Events/AuditEventPublisher.cs`). It builds an `AuditEventEnvelope` carrying a fully-populated nested `AuditContext` (actor, IP, user-agent, request id, etc.) and posts to a Dapr pub/sub topic with raw JSON. Use this for view events on every read endpoint and for mutation events on domains that don't have their own typed envelope.

For *record* events specifically, prefer the typed `IRecordEventPublisher` and follow the `add-record-event-type` skill instead.

## When to invoke this

- Adding a view event to a read endpoint (`*.viewed`, `*.list.viewed`, `*.searched`).
- Adding a mutation event to a domain whose CRUD doesn't already have a typed publisher (users, groups, roles, permission grants, menus, site settings, etc.).
- Introducing a new per-domain topic (e.g. `iam.events`, `site.events`, `workflow.events`, `auth.events`).

If the change is to *record* events specifically, prefer `add-record-event-type` instead.

## Steps

### 1. Pick a topic

Per-domain topics. Existing today: `record.events`, `record-schema.events`, `iam.events`, `site.events`, `workflow.events`, `notification.events`, `application.events`, `auth.events`, `system.issues.events`, `agent.events`, `external-connections.events`. Pick the closest fit — only invent a new top-level prefix when nothing matches.

### 2. Add the topic subject to the JetStream stream config (only if new)

File: `src/AutoNate.Web/Services/Nats/NatsStreamProvisioner.cs`

The single `workflow-execution` stream covers every Dapr-published subject. If your topic uses a *new* top-level prefix (e.g. `auth.events` when no `auth.>` filter exists), append `"auth.>"` to `DesiredStreams[0].Subjects`. Otherwise no change.

### 3. Pick an event type name

Dotted convention: `<domain>.<noun>.<verb>` for mutations, `<domain>.<noun>.viewed` / `<domain>.<noun>.list.viewed` for reads. Examples: `iam.user.created`, `iam.role.assignments.viewed`, `site.menu.items.changed`, `auth.access.denied`.

### 4. Define a small `resource` shape

The `IAuditEventPublisher.PublishAsync(... object? resource ...)` parameter carries a tiny typed payload identifying the thing affected — id + a human-readable key/name. Don't dump the full entity. Examples:

```csharp
new { id = user.UserId, username = user.Username }
new { id = group.Id, name = group.Name }
new { id = role.Id, name = role.Name }
new { recordId, recordTypeId, key = recordKey }   // for record.viewed
```

For `*.list.viewed` and `*.searched`, set `resource = null` and put filter/page metadata in `details`.

### 5. Define `details` for view-event volume hygiene

Auditors care **what was searched/listed**, not which rows came back. Include only summary metadata:

```csharp
new {
    page,
    pageSize,
    resultCount,
    filterHash = ComputeStableHash(filter),
    scope = "assigned-to-me"      // optional disambiguator
}
```

Cap any free-form `filter` JSON at 4 KB with a `"…"` truncation marker. Never dump the row IDs or row contents into the event.

### 6. Emit from the endpoint handler

Inject `IAuditEventPublisher` into the endpoint group, then call after the underlying operation succeeds (post-commit, just like the record store):

```csharp
await auditPublisher.PublishAsync(
    topicName: "iam.events",
    eventType: "iam.user.created",
    resourceKind: "user",
    resource: new { id = created.UserId, username = created.Username },
    details: null,
    cancellationToken: ct);
```

For mutations, emit only on success. For view events, emit on the same code path that returned 200; don't emit on 404 (the user didn't actually access anything).

### 7. Document in the EventCatalog

File: `src/AutoNate.Web/Services/Events/EventCatalog.cs`

- If your topic is new, append an `EventCatalogTransport` entry to `Transports[]`.
- Append (or extend) an `EventCatalogCategory` for your domain. Include payload-field descriptions for `resource` and `details` so the SPA Events page renders something useful.
- Inside the category, append an `EventCatalogEntry` per event type with `Summary`, `FiresWhen`, and `PayloadHighlights`.

If you skip this step, the event publishes fine but doesn't appear in the SPA Events admin page or the signal-start-event modal.

### 8. Volume-coalesce hot polls

For routes the SPA polls frequently (e.g. unread-count, BusWatcher status, anything called every few seconds), wrap the publish in a per-user `IMemoryCache` gate keyed by `(userId, topic, eventType)` with a 60-second sliding TTL. Document the coalescing window in the EventCatalog `PayloadHighlights`.

### 9. Add an endpoint test

Use `RecordingAuditEventPublisher` (in `tests/AutoNate.Web.Tests/RecordingAuditEventPublisher.cs`) to capture publish calls. Replace the default `IAuditEventPublisher` in the WebApplicationFactory with the recording instance, hit your endpoint, then assert exactly the expected events were captured with the expected `eventType` and `resource`/`details` shapes.

## Wire format reminder

`DaprAuditEventPublisher` posts raw JSON with `metadata.rawPayload=true`. No CloudEvents framing. Subscribers must be configured for raw payloads — same convention as the record/notification/Flowable topics.

## What NOT to put in events

- **PII or row contents** in `details`. Use ids and hashes.
- **Per-row events** for list endpoints. One event per request, with `resultCount` in `details`.
- **Anonymous-protocol traffic** (health checks, OPTIONS preflights, anonymous branding reads). Skip those routes — there's no actor to audit.
- **Auth-failed redirects**. The `auth.login.failed` event covers the credential-presentation step; don't also emit on the redirect that carries the user back to the login page.
