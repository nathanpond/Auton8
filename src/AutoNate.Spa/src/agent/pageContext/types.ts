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
};
