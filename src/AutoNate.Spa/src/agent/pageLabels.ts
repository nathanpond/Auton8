// Friendly labels for the low-cardinality page keys produced by usePageKey.
// Used by the chatbot sidebar + cross-page chat palette so chats from other
// pages can show a human-readable "where this chat lives" breadcrumb even
// though the AgentConversation only carries a pageKey on the wire.
const LABELS: Record<string, string> = {
  home: "Home",
  workflow: "Workflows",
  "workflow-executions": "Executions",
  records: "Records",
  "aql-query": "Query",
  notes: "Notes",
  notifications: "Notifications",
  dashboard: "Dashboards",
  "form-editor": "Forms",
  "appearance-editor": "Appearance",
  "record-types": "Record types",
  "system-issues": "System issues",
  admin: "Admin",
  "admin-config": "Admin · Site configuration",
  "admin-config:external-connections": "Admin · External connections",
  default: "Other"
};

export function pageKeyLabel(pageKey: string | null | undefined): string {
  if (!pageKey) return "Other";
  return LABELS[pageKey] ?? pageKey;
}

// Some keys are themselves a "section · sub-section" breadcrumb. Split on
// the conventional "·" separator so the renderer can show them as crumb
// segments.
export function pageKeyCrumb(pageKey: string | null | undefined): string[] {
  const label = pageKeyLabel(pageKey);
  return label.split(" · ");
}

export const KNOWN_PAGE_KEYS = Object.keys(LABELS).filter((k) => k !== "default");
