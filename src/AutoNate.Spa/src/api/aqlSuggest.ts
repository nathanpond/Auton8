import { api } from "./client";

// Phase 8c — natural-language → AQL suggestion. Posts a plain-English
// description to the server, which drafts + validates a single AQL query via
// the configured LLM. Used by the binding "suggest a query" affordance.

export type AqlSuggestion = {
  query: string;
  // Server-side validation result (parsed + type-checked against the live
  // entity schema). When false, `errors` explains why — the query is still
  // returned so the user can tweak it.
  valid: boolean;
  errors: string[];
  explanation: string | null;
};

export async function suggestAql(description: string): Promise<AqlSuggestion> {
  const { data } = await api.post<AqlSuggestion>("/api/aql/suggest", { description });
  return data;
}
