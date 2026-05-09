import { useCallback, useEffect, useMemo, useRef, useState } from "react";
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

  const onPageAction = useCallback(async (request: PageActionRequest): Promise<PageActionResult> => {
    const handle = handleRef.current;
    if (!handle) return { ok: false, error: "page_unreachable", message: "Modeler is not loaded." };
    try {
      switch (request.action) {
        case "update_node":
          return updateNodeAction(handle, request.args);
        case "update_nodes_matching":
          return updateNodesMatchingAction(handle, request.args);
        case "set_node_name":
          return setNodeNameAction(handle, request.args);
        case "replace_diagram_xml":
          return await replaceDiagramXmlAction(handle, request.args);
        default:
          return { ok: false, error: "unknown_action", message: `Workflow studio does not support action '${request.action}'.` };
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return { ok: false, error: "action_failed", message };
    }
  }, []);

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
    onPageQuery,
    actions: WORKFLOW_ACTIONS,
    onPageAction
  }), [getSnapshot, onPageQuery, onPageAction]);

  useRegisterPageContext(entry);
}

// Action catalog the agent reads from the snapshot. Descriptions are the
// model's contract — keep them precise and example-laden so the model
// passes the right args. The actions only mutate the in-memory diagram;
// the user must still click Save.
const WORKFLOW_ACTIONS: PageActionDefinition[] = [
  {
    name: "update_node",
    description:
      "Update properties of one node in the current diagram. args: { id: string, properties: object }. Properties depend on node type (e.g. ScriptTask: { script, resultVariable, name }; UserTask: { name, assignee, candidateUsers, candidateGroups, dueDate, userFormMode, userFormShortCode }; ServiceTask: { name, behaviorKey }; SequenceFlow: { name, conditionExpression }; Gateway/Event/etc.: { name }). Only properties present in args are changed; omitted ones are preserved. Refuses if id is unknown."
  },
  {
    name: "update_nodes_matching",
    description:
      "Apply the same property update to every node matching a filter. args: { filter: { type?: string, behaviorKey?: string, idStartsWith?: string }, properties: object }. type is the BPMN $type (e.g. 'bpmn:UserTask'). The properties shape follows update_node and only the fields you set are changed. Returns a summary listing how many nodes were updated and the affected ids."
  },
  {
    name: "set_node_name",
    description:
      "Rename one node. args: { id: string, name: string }. Convenience over update_node when only the name changes; works for any node type including gateways and events."
  },
  {
    name: "replace_diagram_xml",
    description:
      "Replace the entire diagram with new BPMN XML. args: { xml: string }. The XML must be a complete, valid Flowable BPMN 2.0 document (a `<bpmn:definitions>` root with one `<bpmn:process>`, `<bpmn:sequenceFlow>` connections, and a matching `<bpmndi:BPMNDiagram>` for layout). All current unsaved edits are discarded. Use for whole-workflow rewrites or 'create me a new model' scenarios. The user still has to save afterward — nothing is persisted."
  }
];

type AnyArgs = Record<string, unknown> | undefined;

function updateNodeAction(handle: unknown, rawArgs: unknown): PageActionResult {
  const args = rawArgs as AnyArgs;
  const id = typeof args?.id === "string" ? args.id : null;
  const properties = (args?.properties && typeof args.properties === "object")
    ? (args.properties as Record<string, unknown>)
    : null;
  if (!id || !properties) {
    return { ok: false, error: "bad_args", message: "args.id and args.properties are required." };
  }
  const before = workflow.describeElementById(handle, id);
  if (!before) return { ok: false, error: "not_found", message: `No element '${id}' in the diagram.` };
  return applyNodeUpdate(handle, id, before, properties);
}

function updateNodesMatchingAction(handle: unknown, rawArgs: unknown): PageActionResult {
  const args = rawArgs as AnyArgs;
  const filter = (args?.filter && typeof args.filter === "object")
    ? (args.filter as { type?: string; behaviorKey?: string; idStartsWith?: string })
    : null;
  const properties = (args?.properties && typeof args.properties === "object")
    ? (args.properties as Record<string, unknown>)
    : null;
  if (!filter || !properties) {
    return { ok: false, error: "bad_args", message: "args.filter and args.properties are required." };
  }

  const all = (workflow.getElementSnapshots(handle) ?? []) as Array<Record<string, unknown>>;
  const matches = all.filter((node) => {
    if (filter.type && node.type !== filter.type) return false;
    if (filter.behaviorKey && node.behaviorKey !== filter.behaviorKey) return false;
    const id = typeof node.id === "string" ? node.id : "";
    if (filter.idStartsWith && !id.startsWith(filter.idStartsWith)) return false;
    return true;
  });

  if (matches.length === 0) {
    return { ok: true, summary: "0 nodes matched the filter; nothing to update.", changes: { updated: [] } };
  }

  const updated: string[] = [];
  const skipped: Array<{ id: string; reason: string }> = [];
  for (const match of matches) {
    const id = match.id as string;
    const result = applyNodeUpdate(handle, id, match, properties);
    if (result.ok) {
      updated.push(id);
    } else if (!result.ok) {
      skipped.push({ id, reason: result.message ?? result.error });
    }
  }

  const summary =
    `Updated ${updated.length} of ${matches.length} matching nodes.` +
    (skipped.length > 0 ? ` ${skipped.length} skipped.` : "");
  return { ok: true, summary, changes: { updated, skipped } };
}

function setNodeNameAction(handle: unknown, rawArgs: unknown): PageActionResult {
  const args = rawArgs as AnyArgs;
  const id = typeof args?.id === "string" ? args.id : null;
  const name = typeof args?.name === "string" ? args.name : null;
  if (!id || name === null) {
    return { ok: false, error: "bad_args", message: "args.id and args.name are required." };
  }
  const before = workflow.describeElementById(handle, id);
  if (!before) return { ok: false, error: "not_found", message: `No element '${id}' in the diagram.` };
  workflow.updateGenericElementName(handle, { id, name });
  return {
    ok: true,
    summary: `Renamed '${id}' from '${(before as { name?: string }).name ?? "(unnamed)"}' to '${name}'.`,
    changes: { id, name }
  };
}

async function replaceDiagramXmlAction(handle: unknown, rawArgs: unknown): Promise<PageActionResult> {
  const args = rawArgs as AnyArgs;
  const xml = typeof args?.xml === "string" ? args.xml : null;
  if (!xml || xml.trim().length === 0) {
    return { ok: false, error: "bad_args", message: "args.xml is required and must be a complete BPMN document." };
  }
  // Lightweight sanity check before handing to bpmn-js so the error
  // message is friendlier than an XML parser blow-up.
  if (!xml.includes("<bpmn:definitions") && !xml.includes("<definitions")) {
    return { ok: false, error: "bad_args", message: "xml does not look like a BPMN definitions document." };
  }
  await workflow.createNewDiagram(handle, xml);
  return {
    ok: true,
    summary: "Replaced diagram with new BPMN XML. The model has unsaved changes; remind the user to save.",
    changes: { xmlBytes: xml.length }
  };
}

// Routes a property update to the right type-specific helper. The agent
// can omit fields it doesn't want to change; we fill those from the
// existing describe so we don't accidentally clear properties.
function applyNodeUpdate(
  handle: unknown,
  id: string,
  before: Record<string, unknown>,
  properties: Record<string, unknown>
): PageActionResult {
  const type = before.type as string | undefined;
  const merged: Record<string, unknown> = { ...before, ...properties, id };

  switch (type) {
    case "bpmn:ScriptTask":
      workflow.updateScriptTaskProperties(handle, merged);
      break;
    case "bpmn:UserTask":
      workflow.updateUserTaskProperties(handle, merged);
      break;
    case "bpmn:ServiceTask":
      workflow.updateServiceTaskProperties(handle, merged);
      break;
    case "bpmn:SequenceFlow":
      workflow.updateSequenceFlowProperties(handle, merged);
      break;
    case "bpmn:StartEvent":
      // Signal-start vs timer-start vs none — pick the right helper based
      // on which sub-fields were already populated in the describe. If
      // unsure, fall back to a name-only rename.
      if (typeof before.signalName === "string") {
        workflow.updateSignalStartEventProperties(handle, merged);
      } else if (
        typeof before.timerCycleCron === "string" ||
        typeof before.timerEndDate === "string" ||
        typeof before.timerDuration === "string" ||
        typeof before.timerDate === "string"
      ) {
        workflow.updateTimerStartEventProperties(handle, merged);
      } else {
        workflow.updateGenericElementName(handle, { id, name: (merged.name as string) ?? "" } as never);
      }
      break;
    case "bpmn:IntermediateCatchEvent":
      workflow.updateTimerIntermediateCatchEventProperties(handle, merged);
      break;
    default:
      // Generic fallback: at least the name can usually be set on any
      // element via updateGenericElementName.
      if (typeof properties.name === "string") {
        workflow.updateGenericElementName(handle, { id, name: properties.name });
      } else {
        return {
          ok: false,
          error: "unsupported_type",
          message: `update_node does not support property updates on '${type ?? "unknown"}' beyond renaming.`
        };
      }
  }

  const after = workflow.describeElementById(handle, id);
  return {
    ok: true,
    summary: `Updated ${type ?? "element"} '${id}'.`,
    changes: { id, before, after }
  };
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
