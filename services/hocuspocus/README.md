# AutoNate Hocuspocus sidecar

Yjs sync server for the AutoNate SPA's BlockNote editors. Runs as a Node
container alongside the .NET app, NATS, Flowable, etc. (see
`infra/docker-compose.yml`).

## What it does

- **Persists** Y.Doc binary state to the shared Postgres cluster
  (`yjs_documents` table, owned by this service).
- **Authenticates** every incoming WebSocket connection by calling back to
  `.NET POST /internal/yjs-auth` with the browser-supplied ticket. .NET
  remains the sole owner of permission decisions — Hocuspocus never reads
  AutoNate's auth tables itself.
- **Mirrors** changes to .NET via `POST /internal/yjs-webhook` after each
  debounced save. .NET writes the materialized BlockNote JSON into
  `page.body_jsonb` / `note.content_jsonb` so the existing read paths
  (HistoryModal, future search indexing, PDF export) keep working.

## Design contract

- **Document name format**: `kind:guid`. Phase 1 supports `page:<pageId>`
  and `note:<noteId>` (richtext notes only). Phase 4 will add
  `napkin:<noteId>` and `diagram:<noteId>`.
- **Snapshot shape**: BlockNote `Block[]` JSON. Materialization happens
  here via `ServerBlockNoteEditor.yDocToBlocks(doc)`, so .NET takes the
  produced string verbatim and stores it.
- **Service is stateless** outside Postgres. Restarting the container is
  safe; clients reconnect and Y.Doc state reloads from `yjs_documents`.

## AI agents / workflow callers (not implemented in Phase 1)

Non-browser clients that need to write content connect to Hocuspocus
exactly like a browser would, using a service-principal ticket minted by
.NET. They mutate via `ServerBlockNoteEditor` against a Y.Doc, push the
update through `HocuspocusProvider` (or a server-side client), then
disconnect. **Do not** patch `body_jsonb` / `content_jsonb` via the REST
endpoints — the `YjsManagedContentGuard` rejects those writes for
Yjs-managed documents, and a stray patch would race the next webhook
flush.

## Environment

| Variable | Purpose | Default |
|---|---|---|
| `HOCUSPOCUS_PORT` | TCP port to listen on | `1234` |
| `YJS_INTERNAL_SHARED_SECRET` | Shared with .NET. Used for HMAC-signing the webhook body AND as the `X-AutoNate-Internal-Token` header for callbacks. | (required) |
| `AUTONATE_WEB_URL` | Base URL Hocuspocus calls back to. In docker compose this is the .NET service name; locally it's `http://host.docker.internal:5000`. | (required) |
| `POSTGRES_HOST` | | `postgres` |
| `POSTGRES_PORT` | | `5432` |
| `POSTGRES_DB` | AutoNate database name | (required) |
| `POSTGRES_USER` | | (required) |
| `POSTGRES_PASSWORD` | | (required) |

## Local dev

```bash
npm install
YJS_INTERNAL_SHARED_SECRET=dev-secret \
AUTONATE_WEB_URL=http://localhost:5000 \
POSTGRES_DB=AutoNate POSTGRES_USER=postgres POSTGRES_PASSWORD=... \
POSTGRES_HOST=localhost \
npm run build && npm start
```

The .NET app's `YjsServer:InternalSharedSecret` must match
`YJS_INTERNAL_SHARED_SECRET`. In dev with both unset, the dev fallback
strings line up — but production must set them.

## Risks / known incomplete bits

- **Y.Doc growth**: Hocuspocus doesn't compact stored state automatically.
  Long-lived docs accumulate Yjs updates over time. A periodic GC pass
  (rewriting the full `Y.encodeStateAsUpdate(doc)` on load) is a planned
  follow-up — not shipped here.
- **Replay protection survives single-instance only**: `.NET`'s ticket
  jti cache is `IMemoryCache`; a process restart empties it. Combined
  with the 60-second ticket TTL the window is small, but consider Redis
  if we ever run multiple .NET instances behind a load balancer.
- **Webhook delivery is fire-and-forget**: a sustained .NET outage means
  the snapshot mirror falls behind. Y.Doc state is durable in
  `yjs_documents` regardless; the next successful onStoreDocument flush
  catches the mirror up.
