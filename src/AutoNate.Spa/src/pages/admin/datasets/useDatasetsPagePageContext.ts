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
import type { Dataset, DatasetMode } from "@/api/datasets";
import type { DataStore } from "@/api/datastores";

const PAGE_KEY = "datasets";
const SCHEMA_VERSION = 1;

type CreateModalField =
  | "name"
  | "description"
  | "mode"
  | "sourceKind"
  | "sourceId"
  | "sourceTableName"
  | "refreshCron"
  | "columnsJson";

type EditModalField = "name" | "description" | "refreshCron";

type CreateModalState = {
  open: boolean;
  name: string;
  description: string;
  mode: DatasetMode;
  sourceKind: string;
  sourceId: string;
  sourceTableName: string;
  refreshCron: string;
  columnsJson: string;
  submitError: string | null;
};

type EditModalState = {
  open: boolean;
  editingId: string | null;
  editName: string;
  editDescription: string;
  editRefreshCron: string;
  editIsCached: boolean;
  editError: string | null;
};

type SourceCatalog = {
  datastores: readonly Pick<DataStore, "id" | "name" | "kind">[];
  tables: readonly { tableName: string; rowCount: number }[];
  connectors: readonly { id: string; name: string; kind: string }[];
};

type Options = {
  datasets: readonly Dataset[];
  loading: boolean;
  createModal: CreateModalState;
  editModal: EditModalState;
  sources: SourceCatalog;
  openCreateModal: () => void;
  openEditModal: (id: string) => void;
  closeModals: () => void;
  setCreateField: (field: CreateModalField, value: string) => void;
  setEditField: (field: EditModalField, value: string) => void;
  submitCreate: () => void;
  submitEdit: () => void;
  deleteDataset: (id: string) => Promise<void> | void;
  refreshDataset: (id: string) => Promise<void> | void;
};

const ACTIONS: PageActionDefinition[] = [
  {
    name: "open_create_modal",
    description: "Open the 'New dataset' modal. No args."
  },
  {
    name: "open_edit_modal",
    description: "Open the edit modal for a dataset. args: { id: string }. Refuses when the id isn't visible."
  },
  {
    name: "close_modals",
    description: "Close any open create/edit modal without saving. No args."
  },
  {
    name: "set_create_field",
    description:
      "Set one field on the create modal. args: { field: 'name' | 'description' | 'mode' | 'sourceKind' | 'sourceId' | 'sourceTableName' | 'refreshCron' | 'columnsJson', value: string }. " +
      "mode must be 'Virtual' or 'Cached'; sourceKind must be 'datastore' or 'dataconnector'. " +
      "Use this instead of set_form_field for the Mantine Select-backed fields (mode / sourceKind / sourceId / sourceTableName)."
  },
  {
    name: "set_edit_field",
    description:
      "Set one field on the edit modal. args: { field: 'name' | 'description' | 'refreshCron', value: string }."
  },
  {
    name: "submit_create",
    description: "Submit the create-dataset form. No args. Validates the columns JSON before posting."
  },
  {
    name: "submit_edit",
    description: "Submit the edit-dataset form. No args."
  },
  {
    name: "delete_dataset",
    description: "Delete a dataset by id. args: { id: string }."
  },
  {
    name: "refresh_dataset",
    description:
      "Refresh a Cached dataset by id. args: { id: string }. Refuses when the row is Virtual."
  }
];

export function useDatasetsPagePageContext(options: Options): void {
  const optsRef = useRef(options);
  optsRef.current = options;

  const getSnapshot = useCallback((): PageSnapshot | null => {
    const o = optsRef.current;
    const cachedCount = o.datasets.filter((d) => d.mode === 2).length;
    const virtualCount = o.datasets.filter((d) => d.mode === 1).length;
    const openModal = o.createModal.open
      ? "create"
      : o.editModal.open
      ? "edit"
      : null;
    const summary = [
      `Datasets page · ${o.datasets.length} datasets (${virtualCount} Virtual, ${cachedCount} Cached)`,
      openModal ? `modal=${openModal}` : "modal=closed"
    ].join(" · ");

    return {
      pageKey: PAGE_KEY,
      schemaVersion: SCHEMA_VERSION,
      summary,
      version:
        o.datasets.length +
        (o.createModal.open ? 1 : 0) +
        (o.editModal.open ? 1 : 0) +
        o.createModal.name.length +
        o.editModal.editName.length,
      data: {
        datasets: o.datasets.map((d) => ({
          id: d.id,
          name: d.name,
          description: d.description,
          mode: d.mode === 1 ? "Virtual" : "Cached",
          sourceKind: d.sourceKind,
          sourceId: d.sourceId,
          sourceTableName: d.sourceTableName,
          refreshCron: d.refreshCron,
          lastRefreshedAtUtc: d.lastRefreshedAtUtc,
          updatedAtUtc: d.updatedAtUtc
        })),
        counts: { total: o.datasets.length, virtual: virtualCount, cached: cachedCount },
        createModal: { ...o.createModal },
        editModal: { ...o.editModal },
        sourceCatalog: {
          datastores: o.sources.datastores.map((s) => ({
            id: s.id,
            name: s.name,
            kind: s.kind === 1 ? "FileType" : "SqlType"
          })),
          tables: o.sources.tables,
          connectors: o.sources.connectors
        }
      }
    };
  }, []);

  const onPageQuery = useCallback(async (req: PageQueryRequest): Promise<PageQueryResult> => {
    const o = optsRef.current;
    switch (req.topic) {
      case "dataset.byId": {
        const id = (req.args as { id?: string } | undefined)?.id;
        if (!id) return { ok: false, error: "bad_args", message: "dataset.byId requires { id: string }." };
        const row = o.datasets.find((d) => d.id === id);
        if (!row) return { ok: false, error: "not_found", message: `Dataset ${id} not in list.` };
        return { ok: true, data: row };
      }
      case "createModal.live":
        return { ok: true, data: o.createModal };
      case "editModal.live":
        return { ok: true, data: o.editModal };
      default:
        return { ok: false, error: "unknown_topic", message: `DatasetsPage does not handle '${req.topic}'.` };
    }
  }, []);

  const onPageAction = useCallback(async (req: PageActionRequest): Promise<PageActionResult> => {
    const o = optsRef.current;
    const args = (req.args ?? {}) as Record<string, unknown>;
    switch (req.action) {
      case "open_create_modal":
        o.openCreateModal();
        return { ok: true, summary: "Opened the New dataset modal." };
      case "open_edit_modal": {
        const id = typeof args.id === "string" ? args.id : null;
        if (!id) return { ok: false, error: "bad_args", message: "open_edit_modal requires { id: string }." };
        if (!o.datasets.some((d) => d.id === id))
          return { ok: false, error: "not_found", message: `Dataset ${id} not in list.` };
        o.openEditModal(id);
        return { ok: true, summary: `Opened edit modal for ${id}.` };
      }
      case "close_modals":
        o.closeModals();
        return { ok: true, summary: "Closed modals." };
      case "set_create_field": {
        const field = args.field as CreateModalField;
        const value = args.value;
        const valid: CreateModalField[] = [
          "name", "description", "mode", "sourceKind", "sourceId", "sourceTableName", "refreshCron", "columnsJson"
        ];
        if (!valid.includes(field))
          return { ok: false, error: "bad_args", message: `field must be one of ${valid.join(", ")}.` };
        if (typeof value !== "string")
          return { ok: false, error: "bad_args", message: "value must be a string." };
        if (field === "mode" && value !== "Virtual" && value !== "Cached")
          return { ok: false, error: "bad_args", message: "mode must be 'Virtual' or 'Cached'." };
        if (field === "sourceKind" && value !== "datastore" && value !== "dataconnector")
          return { ok: false, error: "bad_args", message: "sourceKind must be 'datastore' or 'dataconnector'." };
        o.setCreateField(field, value);
        return { ok: true, summary: `Set createModal.${field}.` };
      }
      case "set_edit_field": {
        const field = args.field as EditModalField;
        const value = args.value;
        const valid: EditModalField[] = ["name", "description", "refreshCron"];
        if (!valid.includes(field))
          return { ok: false, error: "bad_args", message: `field must be one of ${valid.join(", ")}.` };
        if (typeof value !== "string")
          return { ok: false, error: "bad_args", message: "value must be a string." };
        o.setEditField(field, value);
        return { ok: true, summary: `Set editModal.${field}.` };
      }
      case "submit_create":
        if (!o.createModal.open)
          return { ok: false, error: "action_failed", message: "Create modal isn't open." };
        o.submitCreate();
        return { ok: true, summary: "Submitted create." };
      case "submit_edit":
        if (!o.editModal.open)
          return { ok: false, error: "action_failed", message: "Edit modal isn't open." };
        o.submitEdit();
        return { ok: true, summary: "Submitted edit." };
      case "delete_dataset": {
        const id = typeof args.id === "string" ? args.id : null;
        if (!id) return { ok: false, error: "bad_args", message: "delete_dataset requires { id: string }." };
        if (!o.datasets.some((d) => d.id === id))
          return { ok: false, error: "not_found", message: `Dataset ${id} not in list.` };
        await o.deleteDataset(id);
        return { ok: true, summary: `Triggered delete of ${id}.` };
      }
      case "refresh_dataset": {
        const id = typeof args.id === "string" ? args.id : null;
        if (!id) return { ok: false, error: "bad_args", message: "refresh_dataset requires { id: string }." };
        const row = o.datasets.find((d) => d.id === id);
        if (!row) return { ok: false, error: "not_found", message: `Dataset ${id} not in list.` };
        if (row.mode !== 2)
          return { ok: false, error: "unsupported_type", message: `Dataset '${row.name}' is Virtual; only Cached datasets can be refreshed.` };
        await o.refreshDataset(id);
        return { ok: true, summary: `Triggered refresh of ${id}.` };
      }
      default:
        return { ok: false, error: "unknown_action", message: `DatasetsPage does not implement '${req.action}'.` };
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
