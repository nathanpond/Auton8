// Public contract for any page that wants the chatbot to "see" its live
// state. Pages register a PageContextProviderEntry; AgentSidebar reads the
// snapshot when sending each message and dispatches page-query events back
// to the registered onPageQuery handler.

export type PageSnapshot = {
  // Must match the conversation's pageKey (set when the conversation was
  // created). Mismatch is rejected by the server.
  pageKey: string;

  // Bumped by the page when its `data` shape changes incompatibly. The
  // server logs this; tools may key off of it but the framework does not.
  schemaVersion: number;

  // Short human-readable summary suitable for inclusion in the system
  // prompt verbatim. Capped at 1KB on the server (truncated with ellipsis
  // if longer).
  summary: string;

  // Monotonic; bumped whenever data semantically changes. Used for future
  // caching/diffing on the server. Does not need to be a hash.
  version: number;

  // Page-specific structured data the model can read via inspect_page.
  // Total serialized size is capped at 64KB; pages should degrade
  // gracefully (drop noisy fields, set hint flags) when they would
  // otherwise exceed the cap.
  data: unknown;
};

export type PageQueryRequest = {
  queryId: string;
  topic: string;
  args?: unknown;
};

export type PageQueryResult =
  | { ok: true; data: unknown }
  | { ok: false; error: string; message?: string };

export type PageActionRequest = {
  actionId: string;
  action: string;
  args?: unknown;
};

export type PageActionResult =
  | { ok: true; summary: string; changes?: unknown }
  | { ok: false; error: string; message?: string };

// One declarable page action. The framework turns these into entries on the
// snapshot's `data.actions` so the model can list them and call them via
// the apply_page_action tool. The description is the contract the model
// reads — describe what the action does, what each arg means, and any
// preconditions.
export type PageActionDefinition = {
  name: string;
  description: string;
};

export type PageContextProviderEntry = {
  pageKey: string;
  // Synchronous accessor. Called the moment the user clicks send. Must
  // return the freshest snapshot the SPA has — no async work. Returning
  // null means "no snapshot available right now" (e.g. modeler still
  // loading) and the message goes out without page context.
  getSnapshot: () => PageSnapshot | null;

  // Optional handler for backend → SPA round-trip queries. Invoked when
  // the agent emits a page_query_request SSE event. If unset (or the page
  // doesn't recognize the topic), reply with { ok: false, error: ... }.
  onPageQuery?: (request: PageQueryRequest) => Promise<PageQueryResult>;

  // Optional declarations + handler for mutating actions the agent can
  // perform on this page. `actions` is the model-facing catalog; the
  // handler is invoked with confirmed=true semantics (the apply_page_action
  // tool already gated on user confirmation). Page providers do NOT need
  // to register builtin actions like set_form_field — the framework
  // injects those automatically when forms are present.
  actions?: PageActionDefinition[];
  onPageAction?: (request: PageActionRequest) => Promise<PageActionResult>;
};
