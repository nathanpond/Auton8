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
import type { DataStore, DataStoreKind } from "@/api/datastores";

const PAGE_KEY = "data-stores";
const SCHEMA_VERSION = 1;

type ModalState = {
  open: boolean;
  // null = create mode; non-null = edit mode (the row being edited).
  editingId: string | null;
  name: string;
  description: string;
  kind: DataStoreKind;
  submitError: string | null;
};

type Options = {
  stores: readonly DataStore[];
  loading: boolean;
  modal: ModalState;
  openCreate: () => void;
  openEdit: (id: string) => void;
  closeModal: () => void;
  setModalField: (field: "name" | "description" | "kind", value: string) => void;
  submitModal: () => void;
  deleteStore: (id: string) => Promise<void> | void;
};

const ACTIONS: PageActionDefinition[] = [
  {
    name: "open_create_modal",
    description: "Open the 'New data store' modal in create mode. No args."
  },
  {
    name: "open_edit_modal",
    description:
      "Open the edit modal for an existing row. args: { id: string }. Refuses when the id isn't in the visible list."
  },
  {
    name: "close_modal",
    description: "Close the create/edit modal without saving. No args."
  },
  {
    name: "set_modal_field",
    description:
      "Set one field of the create/edit modal. args: { field: 'name' | 'description' | 'kind', value: string }. " +
      "Mantine controls (Select / NativeSelect) aren't reliably driven by raw form-fill — prefer this action over set_form_field for `kind`."
  },
  {
    name: "submit_modal",
    description:
      "Submit the create/edit modal. In create mode the modal must have a non-empty name and a kind. In edit mode kind is locked. " +
      "Triggers the same network call the SPA's submit button would. No args."
  },
  {
    name: "delete_data_store",
    description:
      "Delete a data store by id. args: { id: string }. Refuses when the id isn't in the visible list. " +
      "Use this when you want the chatbot to drive a delete from the list; behind the scenes it calls the same /api/datastores DELETE."
  }
];

export function useDataStoresPagePageContext(options: Options): void {
  const optsRef = useRef(options);
  optsRef.current = options;

  const getSnapshot = useCallback((): PageSnapshot | null => {
    const o = optsRef.current;
    const fileTypeCount = o.stores.filter((s) => s.kind === 1).length;
    const sqlTypeCount = o.stores.filter((s) => s.kind === 2).length;
    const modalMode = o.modal.open ? (o.modal.editingId ? "edit" : "create") : null;
    const editingRow = o.modal.editingId
      ? o.stores.find((s) => s.id === o.modal.editingId) ?? null
      : null;
    const summary = [
      `DataStores page · ${o.stores.length} stores (${fileTypeCount} FileType, ${sqlTypeCount} SqlType)`,
      modalMode
        ? `modal=${modalMode}${editingRow ? ` editing "${editingRow.name}"` : o.modal.name ? ` name="${o.modal.name}"` : ""}`
        : "modal=closed"
    ].join(" · ");

    return {
      pageKey: PAGE_KEY,
      schemaVersion: SCHEMA_VERSION,
      summary,
      version: o.stores.length + (o.modal.open ? 1 : 0) + o.modal.name.length,
      data: {
        stores: o.stores.map((s) => ({
          id: s.id,
          name: s.name,
          description: s.description,
          kind: s.kind === 1 ? "FileType" : "SqlType",
          updatedAtUtc: s.updatedAtUtc
        })),
        counts: { total: o.stores.length, fileType: fileTypeCount, sqlType: sqlTypeCount },
        modal: {
          open: o.modal.open,
          mode: modalMode,
          editingId: o.modal.editingId,
          name: o.modal.name,
          description: o.modal.description,
          kind: o.modal.kind,
          submitError: o.modal.submitError
        }
      }
    };
  }, []);

  const onPageQuery = useCallback(async (req: PageQueryRequest): Promise<PageQueryResult> => {
    const o = optsRef.current;
    switch (req.topic) {
      case "store.byId": {
        const id = (req.args as { id?: string } | undefined)?.id;
        if (!id) return { ok: false, error: "bad_args", message: "store.byId requires { id: string }." };
        const row = o.stores.find((s) => s.id === id);
        if (!row) return { ok: false, error: "not_found", message: `Data store ${id} not in the visible list.` };
        return { ok: true, data: row };
      }
      case "modal.live":
        return { ok: true, data: o.modal };
      default:
        return { ok: false, error: "unknown_topic", message: `DataStoresPage does not handle '${req.topic}'.` };
    }
  }, []);

  const onPageAction = useCallback(async (req: PageActionRequest): Promise<PageActionResult> => {
    const o = optsRef.current;
    const args = (req.args ?? {}) as Record<string, unknown>;
    switch (req.action) {
      case "open_create_modal":
        o.openCreate();
        return { ok: true, summary: "Opened the New data store modal." };
      case "open_edit_modal": {
        const id = typeof args.id === "string" ? args.id : null;
        if (!id) return { ok: false, error: "bad_args", message: "open_edit_modal requires { id: string }." };
        if (!o.stores.some((s) => s.id === id))
          return { ok: false, error: "not_found", message: `Data store ${id} not in the visible list.` };
        o.openEdit(id);
        return { ok: true, summary: `Opened edit modal for ${id}.` };
      }
      case "close_modal":
        o.closeModal();
        return { ok: true, summary: "Closed the modal." };
      case "set_modal_field": {
        const field = args.field;
        const value = args.value;
        if (field !== "name" && field !== "description" && field !== "kind")
          return { ok: false, error: "bad_args", message: "field must be 'name', 'description', or 'kind'." };
        if (typeof value !== "string")
          return { ok: false, error: "bad_args", message: "value must be a string." };
        if (field === "kind" && value !== "FileType" && value !== "SqlType")
          return { ok: false, error: "bad_args", message: "kind must be 'FileType' or 'SqlType'." };
        if (field === "kind" && o.modal.editingId !== null)
          return { ok: false, error: "action_failed", message: "Kind is locked in edit mode." };
        o.setModalField(field, value);
        return { ok: true, summary: `Set ${field} = "${value}".` };
      }
      case "submit_modal": {
        if (!o.modal.open)
          return { ok: false, error: "action_failed", message: "Modal isn't open." };
        if (!o.modal.name.trim())
          return { ok: false, error: "action_failed", message: "Name is required." };
        o.submitModal();
        return { ok: true, summary: o.modal.editingId ? "Submitted edit." : "Submitted create." };
      }
      case "delete_data_store": {
        const id = typeof args.id === "string" ? args.id : null;
        if (!id) return { ok: false, error: "bad_args", message: "delete_data_store requires { id: string }." };
        if (!o.stores.some((s) => s.id === id))
          return { ok: false, error: "not_found", message: `Data store ${id} not in the visible list.` };
        await o.deleteStore(id);
        return { ok: true, summary: `Triggered delete of data store ${id}.` };
      }
      default:
        return { ok: false, error: "unknown_action", message: `DataStoresPage does not implement '${req.action}'.` };
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
