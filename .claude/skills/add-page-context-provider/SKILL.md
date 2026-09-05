---
name: add-page-context-provider
description: Use when wiring up a new SPA page so the chatbot can see its live state and (optionally) mutate it on the user's behalf — the unsaved record being edited, the executions list currently filtered, the form being filled, the workflow draft being authored. Covers the SPA-side provider hook, the snapshot shape, the optional round-trip query handler, the optional mutation handler with confirmation flow, and the per-field opt-out for the auto-magic form-fill default.
---

# Adding chatbot page-awareness for a new page

The chatbot's page-awareness framework gives any SPA route a way to expose its live state to the assistant — and, optionally, let the assistant mutate that state on the user's behalf. The contract has three channels — all page-agnostic on the server; only the page-specific data and action shapes are yours to design.

1. **Per-message snapshot (push).** Bundled with each user message. Lands in `AgentSessionContext.PageContext` and exposed to the model via the `inspect_page` tool. Best for state that's compact and that the model needs to see by default. The snapshot also carries `data.actions` (the action catalog) and `data.forms` (auto-discovered) so the model knows what it can do.
2. **On-demand query (pull).** A round-trip the model can issue mid-turn via `query_page`. Best for fresh data (selection may have moved since the user sent the message), heavy data the snapshot omits to stay under the 64KB cap, or data that's expensive to compute.
3. **Mutation (apply).** A round-trip via `apply_page_action`, with explicit user confirmation. The agent narrates the change to the user, the user agrees in chat, then the agent calls again with `confirmed=true` to actually apply it. Mutations only change in-memory state; the user must still save manually.

Two capabilities come for free without any per-page code:

- **Form-fill** (`set_form_field` / `get_form_value` / `submit_form`) is auto-magic on every page. The framework scans the DOM for `<form>` elements, serializes their fields, and exposes them in the snapshot. Pages opt fields out via `data-agent-exclude` (see step 4 below) — password, hidden, submit, reset, button, image, and file fields are always excluded.
- A **forms-only snapshot** is produced even on pages with no registered provider, so the chatbot can fill forms anywhere without setup.

The framework lives in `src/AutoNate.Spa/src/agent/pageContext/` (provider + hook + form-fill helpers) and `src/AutoNate.Web/Services/Agent/PageQuery/` + `Skills/InspectPageSkill.cs` (server). You only touch page-specific files.

The canonical example is the workflow studio: `src/AutoNate.Spa/src/pages/workflow/useWorkflowStudioPageContext.ts`. Read it first — every step below maps to something it does.

## When to invoke this

- The user wants the chatbot to answer "what am I looking at" or "what's selected" or "what's in this form" for a page.
- The user wants the chatbot to *change* the page on their behalf — fill a form, edit a draft, apply a bulk update, scaffold a new model.
- A skill needs to act on what the user is currently editing without making the user re-state it.
- A page has imperative state the chatbot can't reach via normal API calls (in-memory drafts, third-party widget selection, transient filters).

You almost never need this skill *just* for form-fill — that works on every page automatically. Use it when (a) you want to expose page-specific data the framework can't see, (b) you want to opt some form fields out of agent control, or (c) you want page-specific mutating actions beyond the form-fill defaults.

Do **not** use this skill for state that's already saved and reachable via an authenticated read API — the chatbot can use the existing skills (`lookup_records`, `explain_workflow`, etc.) for that.

## Steps

### 1. Confirm the route has a stable `pageKey`

File: `src/AutoNate.Spa/src/agent/usePageKey.ts`

Every snapshot is tagged with a `pageKey`, and the pageKey must match what `usePageKey()` returns for the current URL — but note the **server does not enforce this**. `AgentEndpoints.cs` deliberately stopped enforcing it ("a chat opened from another page must still be able to see + act on whatever page the user is currently viewing"); the only 400 is for a *blank* pageKey. If your route isn't already mapped (for example, a brand-new admin page), add an entry to `PATTERNS`. Reuse an existing key when the new route is just another view of the same page family — conversations are scoped by pageKey, so a new key splits chat history.

### 2. Create a `useXyzPageContext` hook for the page

File: `src/AutoNate.Spa/src/pages/<area>/use<Page>PageContext.ts`

Mirror `useWorkflowStudioPageContext.ts`:

```ts
import { useRegisterPageContext } from "@/agent/pageContext/PageContextRegistry";
import {
  PageActionDefinition,
  PageActionRequest,
  PageActionResult,
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

  const onPageAction = useCallback(async (req: PageActionRequest): Promise<PageActionResult> => {
    /* switch on req.action; mutate via your page's APIs;
       return { ok: true, summary, changes? } | { ok: false, error, message? } */
  }, []);

  const entry = useMemo<PageContextProviderEntry>(() => ({
    pageKey: "my-page",
    getSnapshot,
    onPageQuery,
    actions: MY_PAGE_ACTIONS,    // omit if no custom actions
    onPageAction                  // omit if no custom actions
  }), [getSnapshot, onPageQuery, onPageAction]);

  useRegisterPageContext(entry);
}
```

Then call it once from the page component, **above every conditional early return**. `useXyzPageContext` is itself a hook that triggers ~11 nested hooks (`useRef`, `useCallback`, `useMemo`, `useRegisterPageContext`'s `useEffect`, etc.), so if it sits below an `if (query.isLoading) return <Loader/>` on the first render those hooks are skipped, and on the second render when the query resolves they appear out of nowhere — React tears the component down with *"rendered more hooks than during the previous render"* and the page goes white. Inline any values it needs straight from the queries (`query.data ?? []`) rather than from `const`s declared below the early returns. Form-fill works without registering at all — only register when you need page-specific data, custom actions, or to opt fields out of form-fill.

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

### 7. Declare mutating actions (optional)

If the agent should be able to *change* the page beyond the form-fill defaults, declare actions on your provider entry:

```ts
const MY_PAGE_ACTIONS: PageActionDefinition[] = [
  {
    name: "update_node",
    description:
      "Update properties of one node. args: { id, properties }. ScriptTask supports { script, name }; UserTask supports { name, assignee, dueDate, ... }. Only properties present in args are changed."
  },
  // ...
];
```

The `description` is the model's contract — it tells the model what args to pass. Be precise: list every supported arg, give an example for each non-obvious one, and call out preconditions ("refuses if id is unknown", "only works on draft workflows", etc.).

In `onPageAction`, dispatch by `req.action`:

```ts
const onPageAction = useCallback(async (req: PageActionRequest): Promise<PageActionResult> => {
  switch (req.action) {
    case "update_node": return updateNode(req.args);
    case "update_nodes_matching": return updateNodesMatching(req.args);
    // ...
    default:
      return { ok: false, error: "unknown_action", message: `'${req.action}' is not supported by this page.` };
  }
}, []);
```

**Confirmation flow** (handled by the framework, but understand it). The `apply_page_action` tool has a `confirmed: bool` arg. The model:

1. Calls with `confirmed=false` first. The tool returns a structured "needs confirmation" envelope without invoking your handler. The model is expected to summarise the change in chat from the snapshot it already has (the snapshot is rich enough that the model rarely needs to round-trip just to compose a preview).
2. The user agrees in chat.
3. The model calls again with `confirmed=true`. Only this call reaches your `onPageAction` handler.

Your handler is therefore always confirmed. But the snapshot the model used for the preview may be stale (the user may have clicked a different node in the meantime), so **validate preconditions in the handler and fail fast with a helpful error code**. The model can then re-narrate and re-ask.

Standard error codes for action handlers (matches the read-side conventions):
- `unknown_action` — action name isn't one this page handles.
- `bad_args` — args missing or malformed.
- `not_found` — args reference something that no longer exists.
- `unsupported_type` — the action makes sense generally but not for this specific element.
- `action_failed` — anything else; include `message` with the underlying error.

Every successful return must include a human-readable `summary` the model relays to the user ("Updated 7 of 7 user tasks; set dueDate to PT3D"). Optional `changes` is structured detail the model can reference if pressed for specifics.

**Don't persist anything**. Mutations only change the page's in-memory state; the user must save manually. If your action does anything beyond DOM/state mutation (e.g. an API call), reconsider — that probably belongs in a server-side skill, not here.

### 8. Form-fill is automatic — opt fields out, don't opt them in

Every page automatically gets `set_form_field`, `get_form_value`, and `submit_form` for any `<form>` mounted in the DOM. Fields are auto-discovered; password / hidden / submit / reset / button / image / file fields are always excluded.

To exclude a sensitive field (a credential, an SSN, an internal-only knob), add `data-agent-exclude` to the input:

```jsx
<input type="text" name="apiKey" data-agent-exclude />
```

The framework hides excluded fields from the snapshot AND refuses to set them, so the model can neither read nor write the field. To exclude a whole form (e.g. a login form), put the attribute on the `<form>` element instead.

`submit_form` exists in the default catalog because the model can imagine using it for "fill this in and submit". Treat it as conservative — the action description tells the model to always confirm with the user first, and the model will get user confirmation before calling with `confirmed=true`.

If your page has a custom widget that renders an input but does NOT update React state via the standard `change` event (some date pickers, rich-text editors), form-fill won't work cleanly. In that case, declare a custom action (`set_due_date(args)` etc.) that mutates the widget's API directly and add `data-agent-exclude` to the underlying input so the framework doesn't offer a broken default.

### 9. Verify in the running app

There is no automated SPA test for this — run it manually:

1. Start the server (`dotnet run --project src/AutoNate.Web`) and SPA (`npm run dev`).
2. Open your page; open the assistant sidebar.
3. The header should show `page: <key> · <truncated summary>`. If it shows just the page key, your snapshot is null — check that `getSnapshot` returns non-null once the page is ready.
4. Send a message like "what am I looking at?". Expect an `inspect_page` tool-call card with `{ "topic": ... }` (or no args), then a reply that names what you'd expect from your summary + data.
5. If you implemented `onPageQuery`, ask a question that requires fresh data ("what does this script do" with an unsaved edit). Expect a `query_page` tool-call card with your topic, then a reply that uses the live value.
6. If you implemented mutating actions, ask the agent to make a change ("rename UserTask_3 to 'Approval'"). Expect: an `apply_page_action` card with `confirmed: false`, the agent narrates the change in chat and asks. After you reply "yes", expect a second `apply_page_action` card with `confirmed: true` and the page mutates. The dirty/unsaved indicator on the page should activate; the user must still save manually.
7. For form-fill, open a page with a form and ask "fill in name with 'Test'". Confirm the input updates and the field's React state (form validation, dependent fields) reacts as if a user typed. For any sensitive fields, verify `data-agent-exclude` keeps them out of the snapshot AND blocks `set_form_field` with `excluded` error.
8. Open browser DevTools → Network → filter by `/messages`. Expand the request body and confirm `pageContext.data` matches your snapshot shape (incl. `actions` and `forms` arrays) and the size is well under 64KB.

## Common slip-ups

- **Calling the hook below an early `return`.** This is the most common way to break the dashboard / detail-page wirings. `useXyzPageContext` triggers ~11 inner hooks; if it sits below `if (query.isLoading) return <Loader/>`, on first render none of those hooks run, and on the second render — when the query resolves — they all appear out of nowhere. React aborts with *"rendered more hooks than during the previous render"* and the page goes white. Always call `useXyzPageContext` at the top of the component, next to your other hooks, before any conditional `return`. Inline the values it needs straight from the queries (`query.data ?? []`) rather than from `const`s declared after the early returns — those would be in TDZ at the hook position. Action-callback closures that reference handlers declared further down work fine, because closures resolve their references at *call* time, not creation time.
- **Unstable hook references.** If `getSnapshot` or `onPageQuery` are rebuilt on every render (no `useCallback`, or deps that change too often), `useRegisterPageContext` will unregister/re-register on every render. The framework still works (last-mounted wins) but the active-summary subscription churns. Always memoize.
- **Async work in `getSnapshot`.** It must be synchronous — it runs at the moment the user clicks send. If you need to compute something async, do it on a timer in a `useEffect` and stash the result in a ref that `getSnapshot` reads. Never `await` inside `getSnapshot`.
- **Reading state directly instead of via refs.** Closures captured by `useCallback` see state at the moment the callback was created, not at the moment it's called. Always read mutable state through `argsRef.current` (or similar) so the snapshot reflects the latest values.
- **PageKey mismatch with `usePageKey.ts`.** The `pageKey` in your provider entry must equal what `usePageKey()` returns for the URL the user is on. A mismatch is a **client-side** failure with no error anywhere: `AgentSidebar` keys `getActiveSnapshot`/`dispatchPageQuery`/`dispatchPageAction` on `usePageKey()`, so your provider is simply never found — the snapshot degrades to forms-only and queries/actions return `page_unreachable`. No server error, nothing in the network tab.
- **Forgetting size cap.** Pages with rich state (lots of nodes, big text bodies) is **dropped entirely** — `AgentSession` discards the whole snapshot over the 64KB cap and logs; nothing truncates `data`, and `safetyHints` is a page-authored convention the framework never writes. Over the cap the model gets **no page context at all**. Separately, `InspectPageSkill` caps every `inspect_page`/`query_page` *result* at 32KB with a `_truncated` marker, so a legal 60KB snapshot still truncates on read. Implement explicit degradation in `getSnapshot` so you control which fields get dropped first.
- **Multiple registrations on the same page.** Only one provider per pageKey is active at a time (last-mounted wins). If two components both call `useRegisterPageContext({ pageKey: "x" })`, mounting order decides — that's brittle. Compose state into a single hook called from the page-level component.
- **Topics that say what you want, not what the model needs.** `bpmn.xml` is fine because it's a noun the model already understands. `getCurrentSnapshot` is bad because it sounds like it should be the default action. Pick topics that read like data fields, not RPCs.
- **Trusting the snapshot the model used to compose its preview.** By the time `confirmed=true` reaches your handler, the user may have clicked elsewhere or edited something. Validate preconditions (id still exists, type still matches, dirty state still allows the change) and fail with a precise error code so the model can re-narrate. Don't blindly mutate.
- **Persisting in an action handler.** Mutations are in-memory only. **This rule has been overtaken by the corpus — read it as a default, not a law.**
`useDatasetsPagePageContext` ships `submit_create`, `submit_edit` and `delete_dataset`,
and `useDataStoresPagePageContext`'s own action description says it "calls the same
/api/datastores DELETE". Two of the eight shipped providers persist.

The real contract is the **confirmation gate**: a mutating action must go through the
two-call `confirmed` flow, so the model proposes and a human accepts. Prefer staging
into page state and letting the user click Save where that is natural; where the page
has no such affordance, a persisting action behind the gate is an accepted pattern.
- **Forgetting `data-agent-exclude` on credential fields.** Password fields are excluded by default, but `type=text` API-key inputs, secret-question answers, OTP codes, etc. need the attribute explicitly. If a sensitive field uses a custom widget that renders something other than a `<password>` input, opt it out.

## What you do NOT need to touch

- `src/AutoNate.Web/Services/Agent/PageQuery/` — singleton routers and per-request channels (query + action) are already wired.
- `src/AutoNate.Web/Services/Agent/Skills/InspectPageSkill.cs` — `inspect_page`, `query_page`, and `apply_page_action` are page-agnostic. They walk dotted paths through whatever you put in `data`, round-trip whatever topic the model asks for, and gate every mutation on the `confirmed: true` flag.
- `src/AutoNate.Spa/src/agent/pageContext/forms.ts` — DOM-based form discovery and the React-tracker setter. The framework calls into this from the registry; pages don't import it directly.
- `src/AutoNate.Spa/src/agent/AgentSidebar.tsx` — already reads the active provider's snapshot at submit time and dispatches page-query and page-action events to the registry.
- `src/AutoNate.Spa/src/agent/useAgentStream.ts` — already attaches `pageContext` to the POST body and handles `page_query_request` and `page_action_request` SSE events.
- `src/AutoNate.Web/Services/Agent/Loop/SystemPromptBuilder.cs` — already includes the summary string and the tool hint when a snapshot is present.

If you find yourself editing any of those files, you're either fixing the framework (different change) or you've taken a wrong turn.


## `schemaVersion` is required

`PageSnapshot.schemaVersion` is a non-optional `number` and is threaded all the way
through to the server. It is not mentioned anywhere else in this skill, and without
it your `getSnapshot` will not typecheck. Follow the exemplar:

```ts
const SCHEMA_VERSION = 1;   // useWorkflowStudioPageContext.ts
```

Bump it when the snapshot's shape changes.

**Two other conventions worth matching:** real providers use a module-level
`const PAGE_KEY` (which makes the value greppable against `usePageKey.ts`) rather than
an inline literal, and seven of the nine are named `use<Page>PagePageContext.ts` —
match the corpus, not this skill's shorter form.

**Error codes** also include `unsupported`, `unsupported_action` and `handler_threw`
alongside `page_unreachable`.

**No framework-level size check exists.** The client-side bail is something you
implement yourself; individual hooks self-limit with their own `MAX_DATA_BYTES`.
