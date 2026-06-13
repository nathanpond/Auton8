import { useCallback, useMemo, useRef } from "react";
import { useRegisterPageContext } from "@/agent/pageContext/PageContextRegistry";
import type {
  PageActionDefinition,
  PageActionRequest,
  PageActionResult,
  PageContextProviderEntry,
  PageQueryRequest,
  PageQueryResult,
  PageSnapshot
} from "@/agent/pageContext/types";
import type { DataStore, DataStoreTable } from "@/api/datastores";

const PAGE_KEY = "data-store-detail";
const SCHEMA_VERSION = 1;

type Options = {
  store: DataStore | null;
  isFiles: boolean;
  tables: readonly DataStoreTable[];
  tablesLoading: boolean;
  refreshTables: () => Promise<unknown>;
};

const ACTIONS: PageActionDefinition[] = [
  {
    name: "refresh_tables",
    description:
      "Re-fetch the list of ingested SQL tables for this datastore. No args. " +
      "Useful after the chatbot has just ingested or deleted a table via another tool."
  }
];

export function useDataStoreDetailPagePageContext(options: Options): void {
  const optsRef = useRef(options);
  optsRef.current = options;

  const getSnapshot = useCallback((): PageSnapshot | null => {
    const o = optsRef.current;
    if (!o.store) return null;
    const summary = o.isFiles
      ? `DataStore detail · "${o.store.name}" (FileType)`
      : `DataStore detail · "${o.store.name}" (SqlType) · ${o.tables.length} table${o.tables.length === 1 ? "" : "s"}`;
    return {
      pageKey: PAGE_KEY,
      schemaVersion: SCHEMA_VERSION,
      summary,
      version: o.tables.length + (o.tablesLoading ? 1 : 0) + o.store.name.length,
      data: {
        store: {
          id: o.store.id,
          name: o.store.name,
          description: o.store.description,
          kind: o.store.kind === 1 ? "FileType" : "SqlType",
          ownerUserId: o.store.ownerUserId,
          createdAtUtc: o.store.createdAtUtc,
          updatedAtUtc: o.store.updatedAtUtc
        },
        isFiles: o.isFiles,
        tables: o.isFiles
          ? null
          : o.tables.map((t) => ({
              id: t.id,
              schemaName: t.schemaName,
              tableName: t.tableName,
              rowCount: t.rowCount,
              columnCount: t.columns.length
            })),
        // The CSV ingest wizard and file manager are sub-component-owned. The
        // chatbot can use lookup-data-stores tools (preview_data_store_table,
        // list_data_store_files) to drill into either tree; this snapshot
        // intentionally stays light so it never trips the 64KB cap.
        hint: o.isFiles
          ? "File manager rendered by sub-component. Use lookup-data-stores.list_data_store_files for browsing."
          : "Use lookup-data-stores.preview_data_store_table for row samples; ingest must be driven via the SPA's CSV dropzone."
      }
    };
  }, []);

  const onPageQuery = useCallback(async (req: PageQueryRequest): Promise<PageQueryResult> => {
    const o = optsRef.current;
    switch (req.topic) {
      case "table.byId": {
        const id = (req.args as { id?: string } | undefined)?.id;
        if (!id) return { ok: false, error: "bad_args", message: "table.byId requires { id: string }." };
        const t = o.tables.find((x) => x.id === id);
        if (!t) return { ok: false, error: "not_found", message: `Table ${id} not in this datastore.` };
        return { ok: true, data: t };
      }
      case "table.byName": {
        const name = (req.args as { name?: string } | undefined)?.name;
        if (!name) return { ok: false, error: "bad_args", message: "table.byName requires { name: string }." };
        const t = o.tables.find((x) => x.tableName.toLowerCase() === name.toLowerCase());
        if (!t) return { ok: false, error: "not_found", message: `Table "${name}" not in this datastore.` };
        return { ok: true, data: t };
      }
      default:
        return { ok: false, error: "unknown_topic", message: `DataStoreDetailPage does not handle '${req.topic}'.` };
    }
  }, []);

  const onPageAction = useCallback(async (req: PageActionRequest): Promise<PageActionResult> => {
    const o = optsRef.current;
    switch (req.action) {
      case "refresh_tables":
        await o.refreshTables();
        return { ok: true, summary: `Refreshed table list (${o.tables.length} now).` };
      default:
        return { ok: false, error: "unknown_action", message: `DataStoreDetailPage does not implement '${req.action}'.` };
    }
  }, []);

  const entry = useMemo<PageContextProviderEntry>(
    () => ({
      pageKey: PAGE_KEY,
      getSnapshot,
      onPageQuery,
      actions: ACTIONS,
      onPageAction
    }),
    [getSnapshot, onPageQuery, onPageAction]
  );

  useRegisterPageContext(entry);
}
