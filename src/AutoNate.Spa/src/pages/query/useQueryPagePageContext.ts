import { useCallback, useMemo, useRef } from "react";
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
import type { AqlQueryResponse } from "@/api/aql";
import type { SavedQuery } from "@/api/savedQueries";

const PAGE_KEY = "aql-query";
const SCHEMA_VERSION = 1;

type Options = {
  // Current editor text and last successfully-executed text.
  queryText: string;
  lastSuccessfulText: string | null;
  // Last response from /api/query (rows + columns + truncation) or last errors.
  response: AqlQueryResponse | null;
  errors: string[] | null;
  running: boolean;
  // Selected saved-query row (when the user loaded one into the editor).
  selectedQuery: SavedQuery | null;
  // Imperative actions the page exposes to the agent.
  setQueryText: (text: string) => void;
  runQuery: () => Promise<void> | void;
  openSaveModal: (defaults?: { name?: string; description?: string; isShared?: boolean }) => void;
};

// Heuristic FROM-clause extractor — mirrors entityFromQueryText in QueryPage so
// the snapshot's `editor.entity` stays consistent with the deep-link logic.
function inferEntity(queryText: string): string {
  const m = /\bfrom\s+([A-Za-z_][A-Za-z0-9_]*)/i.exec(queryText);
  if (m) return m[1];
  return "Records";
}

const QUERY_PAGE_ACTIONS: PageActionDefinition[] = [
  {
    name: "set_aql_text",
    description:
      "Replace the AQL editor's contents. args: { text: string }. " +
      "Use this to insert a query you drafted (always confirm with the user first via confirmed=false). " +
      "Clears any 'currently editing this saved query' indicator."
  },
  {
    name: "append_aql",
    description:
      "Append text to the end of the AQL editor (with a leading newline if the buffer is non-empty). " +
      "args: { text: string }. Useful for incremental additions like an ORDER BY clause."
  },
  {
    name: "run_query",
    description:
      "Execute the current contents of the AQL editor against /api/query and show the result table. " +
      "No args. Refuses while a previous run is in flight."
  },
  {
    name: "save_query",
    description:
      "Open the Save modal pre-filled with the supplied name/description/isShared. " +
      "args: { name?: string, description?: string, isShared?: boolean }. " +
      "Refuses when the current editor text has not been successfully executed (saveEnabled=false on the page)."
  }
];

export function useQueryPagePageContext(options: Options): void {
  const optsRef = useRef(options);
  optsRef.current = options;

  const getSnapshot = useCallback((): PageSnapshot | null => {
    const o = optsRef.current;
    const entity = inferEntity(o.queryText);
    const dirty = o.selectedQuery !== null && o.selectedQuery.queryText !== o.queryText;

    const lastResult = o.response
      ? {
          ok: true,
          rowCount: o.response.rows.length,
          columnCount: o.response.columns.length,
          totalCount: o.response.totalCount,
          truncated: o.response.truncated,
          durationMs: o.response.durationMs,
          columns: o.response.columns.map((c) => ({ name: c.name, dataType: c.dataType })),
          errors: null as string[] | null
        }
      : o.errors
      ? {
          ok: false,
          rowCount: 0,
          columnCount: 0,
          totalCount: 0,
          truncated: false,
          durationMs: 0,
          columns: [] as { name: string; dataType: string }[],
          errors: o.errors
        }
      : null;

    const savedQueryFacts = o.selectedQuery
      ? {
          id: o.selectedQuery.id,
          name: o.selectedQuery.name,
          isShared: o.selectedQuery.isShared,
          isOwn: o.selectedQuery.isOwn,
          dirty
        }
      : null;

    const summaryParts = [
      `QueryPage editing AQL for entity '${entity}'`,
      o.queryText.length === 0 ? "(empty)" : `${o.queryText.length} chars`,
      lastResult?.ok
        ? `last run OK: ${lastResult.rowCount} rows${lastResult.truncated ? " (truncated)" : ""}`
        : lastResult
        ? `last run failed: ${(lastResult.errors ?? []).slice(0, 1).join("")}`
        : "no run yet",
      savedQueryFacts ? `saved="${savedQueryFacts.name}"${savedQueryFacts.dirty ? " (dirty)" : ""}` : "unsaved"
    ];
    const summary = summaryParts.join(" · ");

    const snapshot: PageSnapshot = {
      pageKey: PAGE_KEY,
      schemaVersion: SCHEMA_VERSION,
      summary,
      version: o.queryText.length + (lastResult?.rowCount ?? 0) + (o.running ? 1 : 0),
      data: {
        editor: {
          queryText: o.queryText,
          length: o.queryText.length,
          entity,
          lastSuccessfulText: o.lastSuccessfulText,
          running: o.running
        },
        lastResult,
        savedQuery: savedQueryFacts,
        saveEnabled:
          !o.running && o.lastSuccessfulText !== null && o.lastSuccessfulText === o.queryText
      }
    };
    return snapshot;
  }, []);

  const onPageQuery = useCallback(async (req: PageQueryRequest): Promise<PageQueryResult> => {
    const o = optsRef.current;
    switch (req.topic) {
      case "editor.text":
        return { ok: true, data: { queryText: o.queryText } };
      case "result.rows": {
        if (!o.response) {
          return { ok: false, error: "not_found", message: "No result is available — run a query first." };
        }
        // Bounded — the model rarely needs more than a few rows in chat.
        const limit = (req.args as { limit?: number } | undefined)?.limit ?? 25;
        return {
          ok: true,
          data: {
            columns: o.response.columns,
            rows: o.response.rows.slice(0, Math.max(1, Math.min(limit, 100))),
            totalCount: o.response.totalCount,
            truncated: o.response.truncated
          }
        };
      }
      default:
        return { ok: false, error: "unknown_topic", message: `QueryPage does not handle topic '${req.topic}'.` };
    }
  }, []);

  const onPageAction = useCallback(async (req: PageActionRequest): Promise<PageActionResult> => {
    const o = optsRef.current;
    const args = (req.args ?? {}) as Record<string, unknown>;
    switch (req.action) {
      case "set_aql_text": {
        const text = typeof args.text === "string" ? args.text : null;
        if (text === null) {
          return { ok: false, error: "bad_args", message: "set_aql_text requires { text: string }." };
        }
        o.setQueryText(text);
        return {
          ok: true,
          summary: `Editor replaced with ${text.length} characters.`,
          changes: { previousLength: o.queryText.length, newLength: text.length }
        };
      }
      case "append_aql": {
        const text = typeof args.text === "string" ? args.text : null;
        if (text === null) {
          return { ok: false, error: "bad_args", message: "append_aql requires { text: string }." };
        }
        const joiner = o.queryText.length === 0 || o.queryText.endsWith("\n") ? "" : "\n";
        o.setQueryText(o.queryText + joiner + text);
        return {
          ok: true,
          summary: `Appended ${text.length} characters.`,
          changes: { addedLength: text.length, totalLength: o.queryText.length + joiner.length + text.length }
        };
      }
      case "run_query": {
        if (o.running) {
          return { ok: false, error: "action_failed", message: "A previous run is already in flight." };
        }
        await o.runQuery();
        return { ok: true, summary: "Query executed." };
      }
      case "save_query": {
        const saveEnabled =
          !o.running && o.lastSuccessfulText !== null && o.lastSuccessfulText === o.queryText;
        if (!saveEnabled) {
          return {
            ok: false,
            error: "action_failed",
            message: "Save is disabled — execute the current query successfully before saving."
          };
        }
        const name = typeof args.name === "string" ? args.name : undefined;
        const description = typeof args.description === "string" ? args.description : undefined;
        const isShared = typeof args.isShared === "boolean" ? args.isShared : undefined;
        o.openSaveModal({ name, description, isShared });
        return { ok: true, summary: "Save dialog opened." };
      }
      default:
        return { ok: false, error: "unknown_action", message: `QueryPage does not implement '${req.action}'.` };
    }
  }, []);

  const entry = useMemo<PageContextProviderEntry>(
    () => ({
      pageKey: PAGE_KEY,
      getSnapshot,
      onPageQuery,
      actions: QUERY_PAGE_ACTIONS,
      onPageAction
    }),
    [getSnapshot, onPageQuery, onPageAction]
  );

  useRegisterPageContext(entry);
}
