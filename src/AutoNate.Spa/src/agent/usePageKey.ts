import { useLocation, matchPath } from "react-router-dom";

// Stable, low-cardinality strings the backend uses to scope conversations.
// Hand-curated rather than derived from arbitrary URL segments so renaming a
// route doesn't orphan a user's chat history.
const PATTERNS: Array<{ pattern: string; key: string }> = [
  { pattern: "/", key: "home" },
  { pattern: "/workflow", key: "workflow" },
  { pattern: "/workflow/:id", key: "workflow" },
  { pattern: "/executions", key: "workflow-executions" },
  { pattern: "/executions/:id", key: "workflow-executions" },
  { pattern: "/records", key: "records" },
  { pattern: "/records/:id", key: "records" },
  { pattern: "/query", key: "aql-query" },
  { pattern: "/notes/*", key: "notes" },
  // Documents subsystem (Phase 8). The editor route hosts the doc-scoped
  // chat panel inside docx-editor's agentPanel slot — pageKey scopes the
  // conversation list so doc chats don't pollute the global thread list.
  // Per-document threading happens via the conversation's own metadata,
  // not the pageKey, so all documents share one key here.
  { pattern: "/documents/edit/:documentId", key: "documents" },
  { pattern: "/documents/preview/:documentId", key: "documents" },
  { pattern: "/documents/*", key: "documents" },
  { pattern: "/notifications", key: "notifications" },
  // Phase 5b — design-surface routes. Form-fill auto-discovery already
  // gives the chatbot a usable surface on these pages without explicit
  // providers; per-page selection / dirty-state providers are a follow-up.
  { pattern: "/dashboard", key: "dashboard" },
  { pattern: "/dashboard/:id", key: "dashboard" },
  // Data-stack pages (placeable templates default-mounted at these paths).
  // Per-page providers ship data-stack snapshots + mutating actions so the
  // chatbot can interact with the create/edit modals and selected rows.
  { pattern: "/datastores", key: "data-stores" },
  { pattern: "/datastores/:id", key: "data-store-detail" },
  { pattern: "/datasets", key: "datasets" },
  { pattern: "/formdev/:shortCode", key: "form-editor" },
  { pattern: "/admin/config/forms/:id", key: "form-editor" },
  { pattern: "/admin/config/appearance", key: "appearance-editor" },
  { pattern: "/record-types", key: "record-types" },
  { pattern: "/admin/config/system-issues", key: "system-issues" },
  { pattern: "/admin/config/external-connections", key: "admin-config:external-connections" },
  { pattern: "/admin/config/*", key: "admin-config" },
  { pattern: "/admin/*", key: "admin" }
];

export type PageKey = string;

export function usePageKey(): PageKey {
  const location = useLocation();
  for (const { pattern, key } of PATTERNS) {
    if (matchPath(pattern, location.pathname)) return key;
  }
  return "default";
}
