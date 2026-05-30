import { useMutation } from "@tanstack/react-query";
import { suggestAql } from "@/api/aqlSuggest";

// Phase 8c — NL→AQL suggestion mutation. Stateless (no cache); each call is a
// fresh draft. Callers read `data` for the suggested query + validation.
export function useSuggestAql() {
  return useMutation({
    mutationFn: (description: string) => suggestAql(description)
  });
}
