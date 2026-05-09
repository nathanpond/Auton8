import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useRegisterPageContext } from "@/agent/pageContext/PageContextRegistry";
import {
  PageContextProviderEntry,
  PageQueryRequest,
  PageQueryResult,
  PageSnapshot
} from "@/agent/pageContext/types";
import { ModelerHandle } from "@/hooks/useBpmnModeler";
import { WorkflowModel } from "@/types/flowable";
import { WorkflowBehaviorCatalogEntry } from "@/api/workflowBehaviors";
// `workflow.js` is untyped JS — TypeScript infers any here. This hook is
// the boundary that maps the loose shapes into the typed PageSnapshot.
import * as workflow from "@/lib/bpmn/workflow.js";

const PAGE_KEY = "workflow";
const SCHEMA_VERSION = 1;
const MAX_DATA_BYTES = 64 * 1024;

type Options = {
  modelerHandle: ModelerHandle | null;
  model: WorkflowModel | null;
  isDirty: boolean;
  behaviorsCatalog: WorkflowBehaviorCatalogEntry[] | undefined;
};

// Registers the workflow-studio's live state with the chatbot's page-context
// registry. The chatbot can read it cheaply via inspect_page (a per-message
// snapshot) and fall through to query_page for fresh / heavier data
// (canonical XML, per-node deep reads). The hook is a no-op while the
// modeler is still loading.
export function useWorkflowStudioPageContext({
  modelerHandle,
  model,
  isDirty,
  behaviorsCatalog
}: Options): void {
  // Selection is owned by BPMN.js, not React state. We mirror it into a
  // simple ref so the snapshot reflects the latest user click without
  // re-rendering this hook on every selection change.
  const selectedIdsRef = useRef<string[]>([]);
  // Bumped on selection.changed and commandStack.changed. Surfaces in the
  // snapshot's `version` so the server can tell whether anything changed
  // between turns.
  const [version, setVersion] = useState(0);

  // Keep the latest values in refs for getSnapshot, which must be a stable
  // function across re-registrations.
  const modelRef = useRef(model);
  const isDirtyRef = useRef(isDirty);
  const handleRef = useRef(modelerHandle);
  const behaviorsRef = useRef(behaviorsCatalog);
  modelRef.current = model;
  isDirtyRef.current = isDirty;
  handleRef.current = modelerHandle;
  behaviorsRef.current = behaviorsCatalog;

  // Subscribe to BPMN.js events. The modeler exposes its event bus once the
  // diagram is loaded; rebind whenever the handle changes (model reload,
  // route change). Cleanup removes both listeners.
  useEffect(() => {
    if (!modelerHandle) return;
    // workflow.js stores the bpmn-js modeler instance on `handle.modeler`.
    // We poke at it directly here because the existing untyped helpers do
    // the same — encapsulation would mean a much bigger refactor of
    // workflow.js, which is out of scope.
    const modeler = (modelerHandle as { modeler?: { get: (n: string, optional?: boolean) => unknown } }).modeler;
    const eventBus = modeler?.get?.("eventBus", false) as
      | { on: (e: string, cb: (...a: unknown[]) => void) => void; off: (e: string, cb: (...a: unknown[]) => void) => void }
      | undefined;
    if (!eventBus) return;

    const onSelectionChanged = () => {
      selectedIdsRef.current = workflow.getSelectedElementIds(modelerHandle);
      setVersion((v) => v + 1);
    };
    const onCommandStackChanged = () => {
      // Edits change the model body but not necessarily the selection.
      // Bump the version so the next snapshot reads as fresher.
      setVersion((v) => v + 1);
    };

    eventBus.on("selection.changed", onSelectionChanged);
    eventBus.on("commandStack.changed", onCommandStackChanged);
    // Seed initial selection.
    selectedIdsRef.current = workflow.getSelectedElementIds(modelerHandle);

    return () => {
      eventBus.off("selection.changed", onSelectionChanged);
      eventBus.off("commandStack.changed", onCommandStackChanged);
    };
  }, [modelerHandle]);

  const getSnapshot = useCallback((): PageSnapshot | null => {
    const handle = handleRef.current;
    const m = modelRef.current;
    if (!handle || !m) return null;

    const allNodes = (workflow.getElementSnapshots(handle) ?? []) as Array<Record<string, unknown>>;
    const selectionIds: string[] = selectedIdsRef.current ?? [];
    const selectionElements = selectionIds
      .map((id) => workflow.describeElementById(handle, id))
      .filter((d) => d !== null && typeof d === "object") as Array<Record<string, unknown>>;

    // Behavior catalog index keyed by behaviorKey, restricted to keys
    // referenced by service tasks in the model. Keeps the snapshot small.
    const behaviorIndex: Record<string, { displayName: string; description: string | null }> = {};
    const catalog = behaviorsRef.current;
    if (catalog && catalog.length > 0) {
      const referenced = new Set<string>();
      for (const node of allNodes) {
        const behaviorKey = (node as { behaviorKey?: string }).behaviorKey;
        if (behaviorKey) referenced.add(behaviorKey);
      }
      for (const entry of catalog) {
        if (referenced.has(entry.key)) {
          behaviorIndex[entry.key] = {
            displayName: entry.displayName,
            description: entry.description
          };
        }
      }
    }

    let safetyHints: { truncated?: true; truncatedFields?: string[] } | undefined;

    let data: Record<string, unknown> = {
      workflow: {
        id: m.id,
        name: m.name,
        processKey: m.processKey,
        isDraft: m.isDraft,
        publishedVersionNumber: m.publishedVersionNumber,
        draftVersionNumber: m.draftVersionNumber,
        isDirty: isDirtyRef.current
      },
      selection: {
        ids: selectionIds,
        elements: selectionElements
      },
      nodes: allNodes,
      behaviors: behaviorIndex
    };

    // Stay under the server's 64KB cap. If we're over, drop noisy fields
    // from `nodes` in degrading order: scripts → conditions → assignees.
    // The model can still call query_page to fetch per-node detail.
    let raw = JSON.stringify(data);
    if (raw.length > MAX_DATA_BYTES) {
      const truncatedFields: string[] = [];
      const dropFromNodes = (field: string) => {
        truncatedFields.push(field);
        const trimmed = (data.nodes as Array<Record<string, unknown>>).map((n) => {
          const copy = { ...n } as Record<string, unknown>;
          delete copy[field];
          return copy;
        });
        data = { ...data, nodes: trimmed };
      };
      for (const field of ["script", "conditionExpression", "assignee", "candidateUsers", "candidateGroups"]) {
        if (raw.length <= MAX_DATA_BYTES) break;
        dropFromNodes(field);
        raw = JSON.stringify(data);
      }
      safetyHints = { truncated: true, truncatedFields };
      data = { ...data, safetyHints };
    }

    const summary = buildSummary(m, isDirtyRef.current, allNodes.length, selectionElements);

    return {
      pageKey: PAGE_KEY,
      schemaVersion: SCHEMA_VERSION,
      summary,
      version,
      data
    };
  }, [version]);

  const onPageQuery = useCallback(async (request: PageQueryRequest): Promise<PageQueryResult> => {
    const handle = handleRef.current;
    if (!handle) {
      return { ok: false, error: "page_unreachable", message: "Modeler is not loaded." };
    }
    switch (request.topic) {
      case "bpmn.xml": {
        try {
          const xml: string = await workflow.saveXml(handle);
          return { ok: true, data: { xml } };
        } catch (err) {
          return { ok: false, error: "save_xml_failed", message: err instanceof Error ? err.message : String(err) };
        }
      }
      case "node.byId": {
        const id = (request.args as { id?: string } | undefined)?.id;
        if (!id) return { ok: false, error: "bad_args", message: "args.id is required." };
        const desc = workflow.describeElementById(handle, id);
        if (!desc) return { ok: false, error: "not_found", message: `No element '${id}' in the modeler.` };
        return { ok: true, data: desc };
      }
      case "selection.live": {
        const ids: string[] = workflow.getSelectedElementIds(handle);
        const elements = ids
          .map((id) => workflow.describeElementById(handle, id))
          .filter((d: unknown) => d !== null && typeof d === "object");
        return { ok: true, data: { ids, elements } };
      }
      default:
        return { ok: false, error: "unknown_topic", message: `Topic '${request.topic}' is not supported by the workflow studio.` };
    }
  }, []);

  const entry = useMemo<PageContextProviderEntry>(() => ({
    pageKey: PAGE_KEY,
    getSnapshot,
    onPageQuery
  }), [getSnapshot, onPageQuery]);

  useRegisterPageContext(entry);
}

// Short, model-friendly summary string for the system prompt. Capped at
// ~280 chars to leave token budget for the rest of the prompt.
function buildSummary(
  model: WorkflowModel,
  isDirty: boolean,
  nodeCount: number,
  selectionElements: Array<Record<string, unknown>>
): string {
  const draftMarker = model.isDraft
    ? `draft v${model.draftVersionNumber}`
    : `published v${model.publishedVersionNumber ?? "?"}`;
  const dirtyMarker = isDirty ? " (unsaved edits)" : "";
  let selectionClause: string;
  if (selectionElements.length === 0) {
    selectionClause = "Nothing currently selected.";
  } else if (selectionElements.length === 1) {
    const el = selectionElements[0];
    const type = friendlyTypeLabel((el as { type?: string }).type);
    const name = (el as { name?: string | null }).name;
    const id = (el as { id?: string }).id;
    selectionClause = name
      ? `Selected: ${type} '${name}' (id: ${id}).`
      : `Selected: ${type} (id: ${id}).`;
  } else {
    selectionClause = `${selectionElements.length} elements selected.`;
  }
  const out = `Editing ${draftMarker}${dirtyMarker} workflow '${model.name}' (processKey: ${model.processKey}). ${nodeCount} nodes total. ${selectionClause}`;
  return out.length > 280 ? out.slice(0, 279) + "…" : out;
}

function friendlyTypeLabel(rawType: string | undefined): string {
  if (!rawType) return "Element";
  // bpmn:UserTask → User Task; bpmn:ExclusiveGateway → Exclusive Gateway.
  const stripped = rawType.replace(/^bpmn:/, "");
  return stripped.replace(/([A-Z])/g, " $1").trim();
}
