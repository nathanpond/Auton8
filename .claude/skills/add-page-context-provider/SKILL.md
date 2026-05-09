---
name: add-page-context-provider
description: Use when wiring up a new SPA page so the chatbot can see its live, in-memory state — the unsaved record being edited, the executions list currently filtered, the form being filled. Covers the SPA-side provider hook, the snapshot shape, the optional round-trip query handler, and what to verify on the server side.
---

# Adding chatbot page-awareness for a new page

The chatbot's page-awareness framework gives any SPA route a way to expose its live state to the assistant. The contract has two channels — both are page-agnostic on the server; only the page-specific data shape is yours to design.

1. **Per-message snapshot (push).** Bundled with each user message. Lands in `AgentSessionContext.PageContext` and exposed to the model via the `inspect_page` tool. Best for state that's compact and that the model needs to see by default.
2. **On-demand query (pull).** A round-trip the model can issue mid-turn via `query_page`. Best for fresh data (selection may have moved since the user sent the message), heavy data the snapshot omits to stay under the 64KB cap, or data that's expensive to compute.

The framework lives in `src/AutoNate.Spa/src/agent/pageContext/` (provider + hook) and `src/AutoNate.Web/Services/Agent/PageQuery/` + `Skills/InspectPageSkill.cs` (server). You only touch page-specific files.

The canonical example is the workflow studio: `src/AutoNate.Spa/src/pages/workflow/useWorkflowStudioPageContext.ts`. Read it first — every step below maps to something it does.

## When to invoke this

- The user wants the chatbot to answer "what am I looking at" or "what's selected" or "what's in this form" for a page.
- A skill needs to act on what the user is currently editing without making the user re-state it.
- A page has imperative state the chatbot can't reach via normal API calls (in-memory drafts, third-party widget selection, transient filters).

Do **not** use this skill for state that's already saved and reachable via an authenticated read API — the chatbot can use the existing skills (`lookup_records`, `explain_workflow`, etc.) for that.

## Steps

### 1. Confirm the route has a stable `pageKey`

File: `src/AutoNate.Spa/src/agent/usePageKey.ts`

Every snapshot is tagged with a `pageKey`, and the server enforces that the snapshot's pageKey matches the conversation's pageKey. If your route isn't already mapped (for example, a brand-new admin page), add an entry to `PATTERNS`. Reuse an existing key when the new route is just another view of the same page family — conversations are scoped by pageKey, so a new key splits chat history.

### 2. Create a `useXyzPageContext` hook for the page

File: `src/AutoNate.Spa/src/pages/<area>/use<Page>PageContext.ts`

Mirror `useWorkflowStudioPageContext.ts`:

```ts
import { useRegisterPageContext } from "@/agent/pageContext/PageContextRegistry";
import {
  PageContextProviderEntry,
  PageQueryRequest,
  PageQueryResult,
  PageSnapshot
} from "@/agent/pageContext/types";

export function useMyPagePageContext(args: { /* live state */ }): void {
  // Refs: keep latest values without re-binding getSnapshot every render.
  const argsRef = useRef(args);
  argsRef.current = args;

  const getSnapshot = useCallback((): PageSnapshot | null => {
    /* read refs, return PageSnapshot or null when page isn't ready */
  }, [/* version-like deps only */]);

  const onPageQuery = useCallback(async (req: PageQueryRequest): Promise<PageQueryResult> => {
    /* switch on req.topic, return { ok, data } | { ok: false, error, message } */
  }, []);

  const entry = useMemo<PageContextProviderEntry>(() => ({
    pageKey: "my-page",
    getSnapshot,
    onPageQuery
  }), [getSnapshot, onPageQuery]);

  useRegisterPageContext(entry);
}
```

Then call it once from the page component, near the top with the other hooks.

### 3. Design the snapshot shape (`data`)

The snapshot is opaque to the framework — the model sees whatever you put in `data` via `inspect_page`. Design it like an API response, not like internal state:

- Use stable, descriptive keys at the top level. The model lists them via `inspect_page` with no topic, so they should be self-explanatory (e.g. `record`, `selection`, `filters`, `nodes`, `behaviors`).
- Strings are tokens — keep arrays of nodes/items lean (id + display fields) and reserve full property bodies for selection or for the topic-specific subtree.
- Include any small lookup tables the model needs to interpret values (e.g. behaviour-key → display name, status-code → label).
- Total serialized size must stay under **64KB**. The server rejects oversized payloads with a 413; the client also bails defensively. If the natural shape is borderline, implement degradation in `getSnapshot`: drop noisy fields in priority order (scripts > expressions > free-text) and add a `safetyHints: { truncated: true, truncatedFields: [...] }` flag the model can read.

### 4. Build a one-line `summary`

The summary string lands in the system prompt verbatim, every turn a snapshot is sent. Keep it ≤ 280 chars; the server truncates with an ellipsis at 1KB but you should be much shorter than that. Make it deterministic and information-dense:

> "Editing draft v2 (unsaved edits) workflow 'Order Approval' (processKey: order-approval). 12 nodes total. Selected: User Task 'Manager Approval' (id: UserTask_3)."

A good summary lets the model answer the simplest questions ("what page am I on?", "is this saved?") without calling `inspect_page` at all.

### 5. Pick a `version` source

`version` is a monotonic counter that bumps whenever the snapshot's data semantically changes. The framework uses it for caching/diffing, not for correctness — but a stable, monotonic value is much easier to reason about than a hash.

For pages where the SPA owns React state, derive `version` from the count of state-update events you already have. For imperative widgets (BPMN.js, monaco, react-flow, codemirror), subscribe to the widget's change events in a `useEffect` and bump a `useState` counter:

```ts
useEffect(() => {
  const bus = widget.eventBus;
  const onChange = () => setVersion((v) => v + 1);
  bus.on("change", onChange);
  return () => bus.off("change", onChange);
}, [widget]);
```

### 6. Implement `onPageQuery` for round-trip data

Optional but usually worth it. A topic is a page-specific string; conventional shapes:

- `noun.field` for a single value (`bpmn.xml`, `record.audit-trail`).
- `noun.byId` with `args.id` for indexed lookups (`node.byId`, `attachment.byId`).
- `noun.live` for "give me the freshest version of what was in the snapshot" (`selection.live`).

Return shape on success: `{ ok: true, data: <whatever> }`. On failure: `{ ok: false, error: "<short_code>", message: "<human prose>" }`. Use these error codes for known cases:

- `unknown_topic` — the topic isn't one this page handles.
- `bad_args` — args are missing or wrong shape.
- `not_found` — args reference something that doesn't exist (e.g. a deleted node).
- `page_unreachable` — the page provider can't satisfy this right now (rare; the framework already returns this when no provider is registered).

The handler runs on the SPA, so it has direct access to imperative widget APIs — that's the whole point of the round-trip channel. Don't try to call backend APIs from here unless you have a reason; the model can call the relevant skill itself.

### 7. Verify in the running app

There is no automated SPA test for this — run it manually:

1. Start the server (`dotnet run --project src/AutoNate.Web`) and SPA (`npm run dev`).
2. Open your page; open the assistant sidebar.
3. The header should show `page: <key> · <truncated summary>`. If it shows just the page key, your snapshot is null — check that `getSnapshot` returns non-null once the page is ready.
4. Send a message like "what am I looking at?". Expect an `inspect_page` tool-call card with `{ "topic": ... }` (or no args), then a reply that names what you'd expect from your summary + data.
5. If you implemented `onPageQuery`, ask a question that requires fresh data ("what does this script do" with an unsaved edit). Expect a `query_page` tool-call card with your topic, then a reply that uses the live value.
6. Open browser DevTools → Network → filter by `/messages`. Expand the request body and confirm `pageContext.data` matches your snapshot shape and the size is well under 64KB.

## Common slip-ups

- **Unstable hook references.** If `getSnapshot` or `onPageQuery` are rebuilt on every render (no `useCallback`, or deps that change too often), `useRegisterPageContext` will unregister/re-register on every render. The framework still works (last-mounted wins) but the active-summary subscription churns. Always memoize.
- **Async work in `getSnapshot`.** It must be synchronous — it runs at the moment the user clicks send. If you need to compute something async, do it on a timer in a `useEffect` and stash the result in a ref that `getSnapshot` reads. Never `await` inside `getSnapshot`.
- **Reading state directly instead of via refs.** Closures captured by `useCallback` see state at the moment the callback was created, not at the moment it's called. Always read mutable state through `argsRef.current` (or similar) so the snapshot reflects the latest values.
- **PageKey mismatch with `usePageKey.ts`.** The `pageKey` in your provider entry must equal what `usePageKey()` returns for the URL the user is on. The server rejects mismatches with a 400 (so the message goes out without context but the user has to reload to fix it).
- **Forgetting size cap.** Pages with rich state (lots of nodes, big text bodies) will silently get truncated by the server's 64KB cap and the model will see only `safetyHints.truncated`. Implement explicit degradation in `getSnapshot` so you control which fields get dropped first.
- **Multiple registrations on the same page.** Only one provider per pageKey is active at a time (last-mounted wins). If two components both call `useRegisterPageContext({ pageKey: "x" })`, mounting order decides — that's brittle. Compose state into a single hook called from the page-level component.
- **Topics that say what you want, not what the model needs.** `bpmn.xml` is fine because it's a noun the model already understands. `getCurrentSnapshot` is bad because it sounds like it should be the default action. Pick topics that read like data fields, not RPCs.

## What you do NOT need to touch

- `src/AutoNate.Web/Services/Agent/PageQuery/` — singleton router and per-request channel are already wired.
- `src/AutoNate.Web/Services/Agent/Skills/InspectPageSkill.cs` — the `inspect_page` and `query_page` tools are page-agnostic. They walk dotted paths through whatever you put in `data`, and they round-trip whatever topic the model asks for.
- `src/AutoNate.Spa/src/agent/AgentSidebar.tsx` — already reads the active provider's snapshot at submit time and dispatches page-query events to the registered provider.
- `src/AutoNate.Spa/src/agent/useAgentStream.ts` — already attaches `pageContext` to the POST body and handles `page_query_request` SSE events.
- `src/AutoNate.Web/Services/Agent/Loop/SystemPromptBuilder.cs` — already includes the summary string and the `inspect_page` / `query_page` hint when a snapshot is present.

If you find yourself editing any of those files, you're either fixing the framework (different change) or you've taken a wrong turn.
