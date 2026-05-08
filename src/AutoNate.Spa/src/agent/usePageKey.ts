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
