import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import CodeMirror from "@uiw/react-codemirror";
import { javascript } from "@codemirror/lang-javascript";
import {
  ActionIcon,
  Alert,
  Box,
  Button,
  Checkbox,
  Code,
  Group,
  List,
  Modal,
  MultiSelect,
  NumberInput,
  Paper,
  Radio,
  ScrollArea,
  Select,
  Stack,
  Text,
  TextInput,
  Textarea,
  Title
} from "@mantine/core";
import { useBpmnModeler } from "@/hooks/useBpmnModeler";
import { EXECUTIONS_QUERY_KEY, useExecutions } from "@/hooks/useExecutions";
import {
  usePauseWorkflow,
  usePublishWorkflow,
  useResumeWorkflow,
  useSaveWorkflow,
  useStartInstance,
  useWorkflows,
  workflowQueryKey,
  WORKFLOW_LATEST_QUERY_KEY,
  WORKFLOWS_QUERY_KEY
} from "@/hooks/useWorkflows";
import {
  PrepareWorkflowResponse,
  WorkflowElementSnapshot,
  markWorkflowViewed,
  prepareWorkflow,
  saveWorkflow
} from "@/api/workflows";
import {
  WorkflowDefaultVariable,
  WorkflowDefaultVariableType,
  WorkflowModel
} from "@/types/flowable";
import * as workflow from "@/lib/bpmn/workflow.js";
import { extractProcessVariables } from "@/lib/bpmn/processVariables";
import {
  defaultRecurrenceState,
  describeRecurrence,
  generateCron,
  parseCron,
  WEEK_DAYS,
  type MonthlyKind,
  type Ordinal,
  type RecurrenceState,
  type TimerMode,
  type WeekDay
} from "@/lib/cron/recurrence";
import AssigneePicker from "@/components/AssigneePicker";
import { useUsers } from "@/hooks/useUsers";
import { useUserDirectory, userDisplayName } from "@/hooks/useUserDirectory";
import { useForms } from "@/hooks/useForms";
import { useEventCatalog } from "@/hooks/useEventCatalog";
import type { EventCatalogResponse } from "@/api/eventCatalog";
import { useRecordTypes } from "@/hooks/useRecordTypes";
import { useWorkflowBehaviors } from "@/hooks/useWorkflowBehaviors";
import { useWorkflowStudioPageContext } from "./useWorkflowStudioPageContext";
import "./Workflow.css";

type ScriptTaskEditor = {
  id: string;
  type: string;
  name: string;
  scriptFormat: string;
  script: string;
  resultVariable: string;
};

type SequenceFlowEditor = {
  id: string;
  type: string;
  name: string;
  conditionExpression: string;
  sourceType: string | null;
};

type GatewayOutgoingFlow = {
  id: string;
  name: string;
};

type GatewayEditor = {
  id: string;
  type: string;
  name: string;
  defaultFlowId: string;
  outgoingFlows: GatewayOutgoingFlow[];
};

type SignalStartEventEditor = {
  id: string;
  type: string;
  name: string;
  signalName: string;
  signalTopic: string;
  recordTypeShortCodes: string[];
};

// Strict `=== true` semantics — undefined/null defaults to false (conservative).
// Used by the signal-start modal to decide whether to show the record-type
// picker, and by `applySignalStart` to decide whether to strip the filter when
// the user has switched to an event type that doesn't carry a recordTypeId.
function eventCarriesRecordType(
  catalog: EventCatalogResponse | undefined,
  topic: string,
  eventType: string
): boolean {
  if (!catalog) return false;
  const trimmedTopic = topic.trim();
  const trimmedEventType = eventType.trim();
  for (const category of catalog.categories ?? []) {
    for (const evt of category.events) {
      if (evt.topic === trimmedTopic && evt.eventType === trimmedEventType) {
        return evt.carriesRecordType === true;
      }
    }
  }
  return false;
}

type TimerStartEventEditor = {
  id: string;
  type: string;
  name: string;
  recurrence: RecurrenceState;
  endDate: string;
  advancedOpen: boolean;
  rawCronOverride: boolean;
  rawCronText: string;
  parseError: string | null;
};

type TimerIntermediateMode = "duration" | "date";
type TimerIntermediateValueKind = "literal" | "expression";

type TimerIntermediateCatchEventEditor = {
  id: string;
  type: string;
  name: string;
  mode: TimerIntermediateMode;
  durationKind: TimerIntermediateValueKind;
  durationLiteral: string;
  durationExpression: string;
  dateKind: TimerIntermediateValueKind;
  dateLiteral: string;
  dateExpression: string;
};

type ServiceTaskKind = "behavior";

type ServiceTaskEditor = {
  id: string;
  type: string;
  name: string;
  kind: ServiceTaskKind;
  behaviorKey: string;
};

type GenericElementEditor = {
  id: string;
  type: string;
  name: string;
};

const DEFAULT_SIGNAL_TOPIC = "workflow.signals";

type AssignmentMode = "picker" | "expression";

type DueDateMode = "none" | "afterActivation" | "afterProcessStart" | "expression";

type UserFormMode = "simple" | "modal" | "page";

type UserTaskEditor = {
  id: string;
  type: string;
  name: string;
  assigneeMode: AssignmentMode;
  assigneeUserId: string;
  assigneeExpression: string;
  candidateUsersMode: AssignmentMode;
  candidateUserIds: string[];
  candidateUsersExpression: string;
  candidateGroupsRaw: string;
  dueDateMode: DueDateMode;
  dueDateDays: string;
  dueDateExpression: string;
  userFormMode: UserFormMode;
  userFormShortCode: string;
};

type ElementSelection = {
  id: string;
  type: string;
  name?: string | null;
  scriptFormat?: string | null;
  script?: string | null;
  resultVariable?: string | null;
  conditionExpression?: string | null;
  assignee?: string | null;
  candidateUsers?: string[] | null;
  candidateGroups?: string[] | null;
  dueDate?: string | null;
  signalName?: string | null;
  signalTopic?: string | null;
  recordTypeShortCodes?: string[] | null;
  timerCycleCron?: string | null;
  timerEndDate?: string | null;
  timerDuration?: string | null;
  timerDate?: string | null;
  serviceTaskKind?: string | null;
  behaviorKey?: string | null;
  defaultFlowId?: string | null;
  outgoingFlows?: Array<{ id: string; name: string | null }> | null;
  sourceType?: string | null;
  userFormMode?: string | null;
  userFormShortCode?: string | null;
} | null;

function looksLikeExpression(value: string | null | undefined): boolean {
  return !!value && value.trim().startsWith("${");
}

const DUE_DATE_FROM_START_PATTERN =
  /^\$\{dueDateHelper\.fromProcessStart\(execution,\s*(.+?)\)\}$/;
const DUE_DATE_AFTER_ACTIVATION_LITERAL_PATTERN = /^P(\d+)D$/;
const DUE_DATE_AFTER_ACTIVATION_EXPRESSION_PATTERN = /^P(\$\{.+\})D$/;

function parseDueDate(raw: string | null | undefined): {
  mode: DueDateMode;
  days: string;
  expression: string;
} {
  const trimmed = (raw ?? "").trim();
  if (!trimmed) {
    return { mode: "none", days: "", expression: "" };
  }

  const literal = trimmed.match(DUE_DATE_AFTER_ACTIVATION_LITERAL_PATTERN);
  if (literal) {
    return { mode: "afterActivation", days: literal[1], expression: "" };
  }

  const activationExpr = trimmed.match(DUE_DATE_AFTER_ACTIVATION_EXPRESSION_PATTERN);
  if (activationExpr) {
    return { mode: "afterActivation", days: activationExpr[1], expression: "" };
  }

  const fromStart = trimmed.match(DUE_DATE_FROM_START_PATTERN);
  if (fromStart) {
    return { mode: "afterProcessStart", days: fromStart[1].trim(), expression: "" };
  }

  return { mode: "expression", days: "", expression: trimmed };
}

function buildTimerIntermediateEditorState(
  id: string,
  type: string,
  name: string,
  duration: string | null | undefined,
  date: string | null | undefined
): TimerIntermediateCatchEventEditor {
  const trimmedDuration = (duration ?? "").trim();
  const trimmedDate = (date ?? "").trim();

  const durationIsExpression = looksLikeExpression(trimmedDuration);
  const dateIsExpression = looksLikeExpression(trimmedDate);

  // Date wins only when explicitly set; otherwise default to duration so the
  // picker opens on a sensible mode for a freshly-dropped node.
  const mode: TimerIntermediateMode = trimmedDate && !trimmedDuration ? "date" : "duration";

  return {
    id,
    type,
    name,
    mode,
    durationKind: durationIsExpression ? "expression" : "literal",
    durationLiteral: durationIsExpression ? "" : trimmedDuration,
    durationExpression: durationIsExpression ? trimmedDuration : "",
    dateKind: dateIsExpression ? "expression" : "literal",
    dateLiteral: dateIsExpression ? "" : trimmedDate,
    dateExpression: dateIsExpression ? trimmedDate : ""
  };
}

function buildTimerIntermediatePayload(editor: TimerIntermediateCatchEventEditor): {
  timerDuration: string | null;
  timerDate: string | null;
} {
  if (editor.mode === "duration") {
    const value =
      editor.durationKind === "expression"
        ? editor.durationExpression.trim()
        : editor.durationLiteral.trim();
    return { timerDuration: value || null, timerDate: null };
  }
  const value =
    editor.dateKind === "expression"
      ? editor.dateExpression.trim()
      : editor.dateLiteral.trim();
  return { timerDuration: null, timerDate: value || null };
}

function buildDueDate(editor: UserTaskEditor): string | null {
  switch (editor.dueDateMode) {
    case "none":
      return null;
    case "afterActivation": {
      const value = editor.dueDateDays.trim();
      if (!value) return null;
      return `P${value}D`;
    }
    case "afterProcessStart": {
      const value = editor.dueDateDays.trim();
      if (!value) return null;
      return `\${dueDateHelper.fromProcessStart(execution, ${value})}`;
    }
    case "expression": {
      const value = editor.dueDateExpression.trim();
      return value || null;
    }
    default:
      return null;
  }
}

export default function WorkflowStudio() {
  const qc = useQueryClient();
  const { data: workflows = [], isSuccess: workflowsLoaded } = useWorkflows();
  // Used by `applySignalStart` to strip the record-type filter when the user
  // has switched to an event type that doesn't carry a recordTypeId. The modal
  // calls `useEventCatalog` separately for its own picker visibility logic.
  const { data: signalEventCatalog } = useEventCatalog();
  const [currentModel, setCurrentModel] = useState<WorkflowModel | null>(null);
  const [loadedXml, setLoadedXml] = useState<string | null>(null);
  const [dirty, setDirty] = useState(false);
  const [sidebarActiveId, setSidebarActiveId] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [warnings, setWarnings] = useState<string[]>([]);
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [showBpmnTypesModal, setShowBpmnTypesModal] = useState(false);
  const [scriptTaskEditor, setScriptTaskEditor] = useState<ScriptTaskEditor | null>(null);
  const [sequenceFlowEditor, setSequenceFlowEditor] = useState<SequenceFlowEditor | null>(null);
  const [userTaskEditor, setUserTaskEditor] = useState<UserTaskEditor | null>(null);
  const [signalStartEditor, setSignalStartEditor] = useState<SignalStartEventEditor | null>(null);
  const [timerStartEditor, setTimerStartEditor] = useState<TimerStartEventEditor | null>(null);
  const [timerIntermediateEditor, setTimerIntermediateEditor] =
    useState<TimerIntermediateCatchEventEditor | null>(null);
  const [serviceTaskEditor, setServiceTaskEditor] = useState<ServiceTaskEditor | null>(null);
  const [gatewayEditor, setGatewayEditor] = useState<GatewayEditor | null>(null);
  const [genericEditor, setGenericEditor] = useState<GenericElementEditor | null>(null);

  const sortedWorkflows = useMemo(
    () => [...workflows].sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: "base" })),
    [workflows]
  );

  // Seed currentModel from the first workflow once the list query resolves. Gating on
  // workflowsLoaded prevents a false "no workflows yet" flash while the query is in flight.
  useEffect(() => {
    if (!workflowsLoaded || currentModel) {
      return;
    }
    if (workflows.length > 0) {
      selectWorkflow(workflows[0]);
    } else {
      setShowCreateModal(true);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [workflowsLoaded, workflows]);

  const onDiagramChanged = useCallback(() => {
    setDirty(true);
  }, []);

  const onRequestConfigure = useCallback((raw: unknown) => {
    const selection = raw as ElementSelection;
    const isTimerIntermediateCatch =
      !!selection &&
      selection.type === "bpmn:IntermediateCatchEvent" &&
      // describeBusinessObject only sets timerDuration/timerDate when the
      // intermediate catch event has a TimerEventDefinition; non-timer
      // catch events leave them undefined entirely.
      ("timerDuration" in selection || "timerDate" in selection);
    if (isTimerIntermediateCatch && selection) {
      setTimerIntermediateEditor(
        buildTimerIntermediateEditorState(
          selection.id,
          selection.type,
          selection.name ?? "",
          selection.timerDuration,
          selection.timerDate
        )
      );
      setTimerStartEditor(null);
      setSignalStartEditor(null);
      setScriptTaskEditor(null);
      setSequenceFlowEditor(null);
      setUserTaskEditor(null);
      setServiceTaskEditor(null);
      setGatewayEditor(null);
      setGenericEditor(null);
      return;
    }
    const isTimerStart =
      !!selection &&
      selection.type === "bpmn:StartEvent" &&
      // describeBusinessObject only sets timerCycleCron/timerEndDate when the
      // start event has a TimerEventDefinition; plain start events leave them
      // undefined entirely.
      ("timerCycleCron" in selection || "timerEndDate" in selection);
    if (isTimerStart && selection) {
      const cron = (selection.timerCycleCron ?? "").trim();
      const parsed = cron ? parseCron(cron) : null;
      const recurrence = parsed ?? defaultRecurrenceState();
      const couldNotParse = cron.length > 0 && parsed === null;
      setTimerStartEditor({
        id: selection.id,
        type: selection.type,
        name: selection.name ?? "",
        recurrence,
        endDate: selection.timerEndDate ?? "",
        advancedOpen: couldNotParse,
        rawCronOverride: couldNotParse,
        rawCronText: cron,
        parseError: couldNotParse
          ? "AutoNate doesn't recognize this cron expression. The picker is locked — edit the raw cron below or clear it to start fresh."
          : null
      });
      setSignalStartEditor(null);
      setScriptTaskEditor(null);
      setSequenceFlowEditor(null);
      setUserTaskEditor(null);
      setTimerIntermediateEditor(null);
      setServiceTaskEditor(null);
      setGatewayEditor(null);
      setGenericEditor(null);
      return;
    }
    const isSignalStart =
      !!selection &&
      selection.type === "bpmn:StartEvent" &&
      // describeBusinessObject only sets signalName/signalTopic when the
      // start event has a SignalEventDefinition; plain start events leave
      // these properties undefined entirely (not just null).
      ("signalName" in selection || "signalTopic" in selection);
    if (isSignalStart && selection) {
      // describeBusinessObject only emits signalName/signalTopic when a signal
      // event definition is present. Plain start events keep them undefined.
      setSignalStartEditor({
        id: selection.id,
        type: selection.type,
        name: selection.name ?? "",
        signalName: selection.signalName ?? "",
        signalTopic: selection.signalTopic ?? "",
        // Defensive copy — don't share the array reference with the modeler's
        // description object, which describeBusinessObject re-emits on each
        // selection change.
        recordTypeShortCodes: Array.isArray(selection.recordTypeShortCodes)
          ? [...selection.recordTypeShortCodes]
          : []
      });
      setTimerStartEditor(null);
      setScriptTaskEditor(null);
      setSequenceFlowEditor(null);
      setUserTaskEditor(null);
      setTimerIntermediateEditor(null);
      setServiceTaskEditor(null);
      setGatewayEditor(null);
      setGenericEditor(null);
      return;
    }
    const isServiceTask =
      !!selection &&
      selection.type === "bpmn:ServiceTask" &&
      // describeBusinessObject only sets these when the service task is wired
      // through the AutoNate behavior bridge; service tasks pointing at a
      // different delegateExpression are left to bare-XML editing.
      ("serviceTaskKind" in selection || "behaviorKey" in selection);
    if (isServiceTask && selection) {
      setServiceTaskEditor({
        id: selection.id,
        type: selection.type,
        name: selection.name ?? "",
        kind: "behavior",
        behaviorKey: selection.behaviorKey ?? ""
      });
      setScriptTaskEditor(null);
      setSequenceFlowEditor(null);
      setUserTaskEditor(null);
      setSignalStartEditor(null);
      setTimerStartEditor(null);
      setTimerIntermediateEditor(null);
      setGatewayEditor(null);
      setGenericEditor(null);
      return;
    }
    if (selection && selection.type === "bpmn:ScriptTask") {
      setScriptTaskEditor({
        id: selection.id,
        type: selection.type,
        name: selection.name ?? "",
        scriptFormat: "javascript",
        script: selection.script ?? "",
        resultVariable: selection.resultVariable ?? ""
      });
      setSequenceFlowEditor(null);
      setUserTaskEditor(null);
      setSignalStartEditor(null);
      setTimerStartEditor(null);
      setTimerIntermediateEditor(null);
      setServiceTaskEditor(null);
      setGatewayEditor(null);
      setGenericEditor(null);
    } else if (selection && selection.type === "bpmn:SequenceFlow") {
      setSequenceFlowEditor({
        id: selection.id,
        type: selection.type,
        name: selection.name ?? "",
        conditionExpression: selection.conditionExpression ?? "",
        sourceType: selection.sourceType ?? null
      });
      setScriptTaskEditor(null);
      setUserTaskEditor(null);
      setSignalStartEditor(null);
      setTimerStartEditor(null);
      setTimerIntermediateEditor(null);
      setServiceTaskEditor(null);
      setGatewayEditor(null);
      setGenericEditor(null);
    } else if (
      selection &&
      (selection.type === "bpmn:ExclusiveGateway" || selection.type === "bpmn:InclusiveGateway")
    ) {
      const outgoingFlows = (selection.outgoingFlows ?? []).map((flow) => ({
        id: flow.id,
        name: flow.name ?? ""
      }));
      const defaultFlowId = selection.defaultFlowId ?? "";
      // If the previously stored default flow no longer exists (e.g. the user
      // deleted it), drop the stale id so the picker renders "(none)".
      const validDefaultFlowId = outgoingFlows.some((flow) => flow.id === defaultFlowId)
        ? defaultFlowId
        : "";
      setGatewayEditor({
        id: selection.id,
        type: selection.type,
        name: selection.name ?? "",
        defaultFlowId: validDefaultFlowId,
        outgoingFlows
      });
      setScriptTaskEditor(null);
      setSequenceFlowEditor(null);
      setUserTaskEditor(null);
      setSignalStartEditor(null);
      setTimerStartEditor(null);
      setTimerIntermediateEditor(null);
      setServiceTaskEditor(null);
      setGenericEditor(null);
    } else if (selection && selection.type === "bpmn:UserTask") {
      const assignee = selection.assignee ?? "";
      const candidateUsers = selection.candidateUsers ?? [];
      const candidateGroups = selection.candidateGroups ?? [];
      const assigneeIsExpression = looksLikeExpression(assignee);
      const candidateUsersFirst = candidateUsers[0] ?? "";
      const candidateUsersIsExpression =
        candidateUsers.length === 1 && looksLikeExpression(candidateUsersFirst);
      const dueDate = parseDueDate(selection.dueDate);
      const rawUserFormMode = (selection.userFormMode ?? "").trim().toLowerCase();
      const userFormMode: UserFormMode =
        rawUserFormMode === "modal" || rawUserFormMode === "page" ? rawUserFormMode : "simple";
      setUserTaskEditor({
        id: selection.id,
        type: selection.type,
        name: selection.name ?? "",
        assigneeMode: assigneeIsExpression ? "expression" : "picker",
        assigneeUserId: assigneeIsExpression ? "" : assignee,
        assigneeExpression: assigneeIsExpression ? assignee : "",
        candidateUsersMode: candidateUsersIsExpression ? "expression" : "picker",
        candidateUserIds: candidateUsersIsExpression ? [] : candidateUsers,
        candidateUsersExpression: candidateUsersIsExpression ? candidateUsersFirst : "",
        candidateGroupsRaw: candidateGroups.join(", "),
        dueDateMode: dueDate.mode,
        dueDateDays: dueDate.days,
        dueDateExpression: dueDate.expression,
        userFormMode,
        userFormShortCode: (selection.userFormShortCode ?? "").trim()
      });
      setScriptTaskEditor(null);
      setSequenceFlowEditor(null);
      setSignalStartEditor(null);
      setTimerStartEditor(null);
      setTimerIntermediateEditor(null);
      setServiceTaskEditor(null);
      setGatewayEditor(null);
      setGenericEditor(null);
    } else if (selection) {
      setGenericEditor({
        id: selection.id,
        type: selection.type,
        name: selection.name ?? ""
      });
      setScriptTaskEditor(null);
      setSequenceFlowEditor(null);
      setUserTaskEditor(null);
      setSignalStartEditor(null);
      setTimerStartEditor(null);
      setTimerIntermediateEditor(null);
      setServiceTaskEditor(null);
      setGatewayEditor(null);
    } else {
      setScriptTaskEditor(null);
      setSequenceFlowEditor(null);
      setUserTaskEditor(null);
      setSignalStartEditor(null);
      setTimerStartEditor(null);
      setTimerIntermediateEditor(null);
      setServiceTaskEditor(null);
      setGatewayEditor(null);
      setGenericEditor(null);
    }
  }, []);

  const callbacks = useMemo(
    () => ({
      NotifyDiagramChanged: onDiagramChanged,
      RequestConfigureElement: onRequestConfigure
    }),
    [onDiagramChanged, onRequestConfigure]
  );

  const { containerRef, handle, loading: modelerLoading, error: modelerError } = useBpmnModeler({
    xml: loadedXml,
    callbacks
  });

  // Expose the live, possibly-unsaved workflow model + selection to the
  // chatbot's page-context registry. The chatbot reads it via inspect_page
  // (per-message snapshot) and can fetch fresh slices via query_page (e.g.
  // bpmn.xml, node.byId).
  const behaviorsQuery = useWorkflowBehaviors();
  useWorkflowStudioPageContext({
    modelerHandle: handle,
    model: currentModel,
    isDirty: dirty,
    behaviorsCatalog: behaviorsQuery.data
  });

  const saveMutation = useSaveWorkflow();
  const publishMutation = usePublishWorkflow();
  const startMutation = useStartInstance();
  const pauseMutation = usePauseWorkflow();
  const resumeMutation = useResumeWorkflow();

  const selectWorkflow = (model: WorkflowModel) => {
    setCurrentModel(model);
    setLoadedXml(model.bpmnXml);
    setDirty(false);
    setWarnings([]);
    setStatus(null);
    setError(null);
    setScriptTaskEditor(null);
    setSequenceFlowEditor(null);
    setUserTaskEditor(null);
    setSignalStartEditor(null);
    setTimerStartEditor(null);
    setTimerIntermediateEditor(null);
    setServiceTaskEditor(null);
    setGatewayEditor(null);
    setGenericEditor(null);
    // Fire-and-forget audit ping. The studio reuses one workflow list call
    // for the whole session, so without this the audit log would only ever
    // see the list-view event; this ensures one ModelViewed event per
    // distinct model the user opens in the modeler.
    void markWorkflowViewed(model.id);
  };

  const onSelectionChange = async (id: string) => {
    const target = workflows.find((w) => w.id === id);
    if (!target || target.id === currentModel?.id) return;
    if (dirty && !window.confirm("Discard unsaved changes to the current workflow?")) {
      return;
    }
    selectWorkflow(target);
  };

  const getModelerSnapshot = async (): Promise<{
    xml: string;
    snapshots: WorkflowElementSnapshot[];
  } | null> => {
    if (!handle) {
      setError("The BPMN modeler is not ready yet.");
      return null;
    }

    try {
      const xml: string = await workflow.saveXml(handle);
      const snapshots: WorkflowElementSnapshot[] = await workflow.getElementSnapshots(handle);
      return { xml, snapshots };
    } catch (err) {
      setError(describeError(err));
      return null;
    }
  };

  const runBusy = async <T,>(operation: string, task: () => Promise<T>): Promise<T | null> => {
    if (busy) return null;
    setBusy(operation);
    setError(null);
    setStatus(null);
    try {
      const result = await task();
      return result;
    } catch (err) {
      setError(describeError(err));
      return null;
    } finally {
      setBusy(null);
    }
  };

  const prepareAndStore = async (): Promise<{
    prepared: WorkflowModel;
    response: PrepareWorkflowResponse;
  } | null> => {
    if (!currentModel) {
      setError("Select or create a workflow model before saving.");
      return null;
    }
    const snap = await getModelerSnapshot();
    if (!snap) return null;

    const response = await prepareWorkflow({
      model: { ...currentModel, bpmnXml: snap.xml },
      elementSnapshots: snap.snapshots
    });
    setWarnings(response.warnings);
    if (response.errors.length > 0) {
      setError(response.errors.join(" "));
      return null;
    }
    return { prepared: response.model, response };
  };

  const onSave = () =>
    runBusy("saving the workflow draft", async () => {
      const prep = await prepareAndStore();
      if (!prep) return;
      const saved = await saveWorkflow(prep.prepared);
      qc.setQueryData(workflowQueryKey(saved.id), saved);
      qc.invalidateQueries({ queryKey: WORKFLOWS_QUERY_KEY });
      qc.invalidateQueries({ queryKey: WORKFLOW_LATEST_QUERY_KEY });
      setCurrentModel(saved);
      setLoadedXml(saved.bpmnXml);
      setDirty(false);
      setStatus(`Saved workflow model '${saved.name}'.`);
    });

  const onPublish = () =>
    runBusy("publishing the workflow model", async () => {
      const prep = await prepareAndStore();
      if (!prep) return;
      const result = await publishMutation.mutateAsync(prep.prepared);
      setCurrentModel(result.model);
      setLoadedXml(result.model.bpmnXml);
      setDirty(false);

      setStatus(
        `Published '${result.model.name}' draft v${result.model.draftVersionNumber} to Flowable as definition version ${result.deployment.processDefinitionVersion}.`
      );
    });

  const onStartInstance = () =>
    runBusy("starting the workflow instance", async () => {
      if (!currentModel || !currentModel.publishedVersionNumber) {
        throw new Error("Publish the workflow model to Flowable before starting an instance.");
      }
      if (currentModel.isSuspended) {
        throw new Error("This workflow is paused. Resume it before starting a new instance.");
      }
      const instance = await startMutation.mutateAsync({ processKey: currentModel.processKey });
      qc.invalidateQueries({ queryKey: EXECUTIONS_QUERY_KEY });

      const nextModel = { ...currentModel, activeProcessInstanceId: instance.id };
      setCurrentModel(nextModel);

      const hasUnpublishedChanges = dirty || currentModel.isDraft;
      const label = instance.name ? `'${instance.name}' (${instance.id})` : instance.id;
      const prefix = `Started process instance ${label}`;
      setStatus(
        hasUnpublishedChanges
          ? `${prefix} from published v${currentModel.publishedVersionNumber}. Local draft v${currentModel.draftVersionNumber} has unpublished changes; publish to run them.`
          : `${prefix}.`
      );
    });

  const applyScriptTask = () =>
    runBusy("applying script task changes", async () => {
      if (!handle || !scriptTaskEditor) {
        throw new Error("Select a script task before applying script changes.");
      }
      await workflow.updateScriptTaskProperties(handle, scriptTaskEditor);
      setScriptTaskEditor(null);
    });

  const applySequenceFlow = () =>
    runBusy("applying sequence flow changes", async () => {
      if (!handle || !sequenceFlowEditor) {
        throw new Error("Select a sequence flow before applying condition changes.");
      }
      await workflow.updateSequenceFlowProperties(handle, sequenceFlowEditor);
      setSequenceFlowEditor(null);
    });

  const applySignalStart = () =>
    runBusy("applying signal start event changes", async () => {
      if (!handle || !signalStartEditor) {
        throw new Error("Select a signal start event before applying changes.");
      }

      // Mid-edit safety: if the event type no longer carries a recordTypeId,
      // drop any lingering filter selection so the BPMN attribute is cleared.
      const carriesRecordType = eventCarriesRecordType(
        signalEventCatalog,
        signalStartEditor.signalTopic,
        signalStartEditor.signalName
      );
      const finalShortCodes = carriesRecordType
        ? signalStartEditor.recordTypeShortCodes
        : [];

      await workflow.updateSignalStartEventProperties(handle, {
        id: signalStartEditor.id,
        name: signalStartEditor.name,
        signalName: signalStartEditor.signalName.trim(),
        signalTopic: signalStartEditor.signalTopic.trim(),
        recordTypeShortCodes: finalShortCodes
      });
      setSignalStartEditor(null);
    });

  const applyTimerStart = () =>
    runBusy("applying timer start event changes", async () => {
      if (!handle || !timerStartEditor) {
        throw new Error("Select a timer start event before applying changes.");
      }

      let cron: string;
      if (timerStartEditor.rawCronOverride) {
        cron = timerStartEditor.rawCronText.trim();
        if (!cron) {
          throw new Error("Enter a cron expression in the Advanced section before applying.");
        }
      } else {
        const result = generateCron(timerStartEditor.recurrence);
        if (!result.ok) {
          throw new Error(result.error);
        }
        cron = result.cron;
      }

      await workflow.updateTimerStartEventProperties(handle, {
        id: timerStartEditor.id,
        name: timerStartEditor.name,
        timeCycle: cron,
        endDate: timerStartEditor.endDate.trim() || null
      });
      setTimerStartEditor(null);
    });

  const applyTimerIntermediate = () =>
    runBusy("applying timer intermediate catch event changes", async () => {
      if (!handle || !timerIntermediateEditor) {
        throw new Error("Select a timer intermediate catch event before applying changes.");
      }

      const { timerDuration, timerDate } = buildTimerIntermediatePayload(timerIntermediateEditor);
      if (!timerDuration && !timerDate) {
        throw new Error(
          timerIntermediateEditor.mode === "duration"
            ? "Enter a duration (e.g. PT15M) or a Flowable expression before applying."
            : "Enter a date/time (e.g. 2026-12-31T09:00:00) or a Flowable expression before applying."
        );
      }

      await workflow.updateTimerIntermediateCatchEventProperties(handle, {
        id: timerIntermediateEditor.id,
        name: timerIntermediateEditor.name,
        timerDuration,
        timerDate
      });
      setTimerIntermediateEditor(null);
    });

  const applyServiceTask = () =>
    runBusy("applying service task changes", async () => {
      if (!handle || !serviceTaskEditor) {
        throw new Error("Select a service task before applying changes.");
      }
      const behaviorKey = serviceTaskEditor.behaviorKey.trim();
      if (!behaviorKey) {
        throw new Error("Pick a behavior before applying.");
      }
      await workflow.updateServiceTaskProperties(handle, {
        id: serviceTaskEditor.id,
        name: serviceTaskEditor.name,
        serviceTaskKind: serviceTaskEditor.kind,
        behaviorKey
      });
      setServiceTaskEditor(null);
    });

  const applyGeneric = () =>
    runBusy("applying element changes", async () => {
      if (!handle || !genericEditor) {
        throw new Error("Select an element before applying changes.");
      }
      await workflow.updateGenericElementName(handle, {
        id: genericEditor.id,
        name: genericEditor.name
      });
      setGenericEditor(null);
    });

  const applyGateway = () =>
    runBusy("applying gateway changes", async () => {
      if (!handle || !gatewayEditor) {
        throw new Error("Select a gateway before applying changes.");
      }
      await workflow.updateGenericElementName(handle, {
        id: gatewayEditor.id,
        name: gatewayEditor.name
      });
      await workflow.updateGatewayDefaultFlow(handle, {
        id: gatewayEditor.id,
        defaultFlowId: gatewayEditor.defaultFlowId
      });
      setGatewayEditor(null);
    });

  const applyUserTask = () =>
    runBusy("applying user task changes", async () => {
      if (!handle || !userTaskEditor) {
        throw new Error("Select a user task before applying assignment changes.");
      }

      const assignee =
        userTaskEditor.assigneeMode === "expression"
          ? userTaskEditor.assigneeExpression.trim() || null
          : userTaskEditor.assigneeUserId.trim() || null;

      const candidateUsers =
        userTaskEditor.candidateUsersMode === "expression"
          ? (() => {
              const expr = userTaskEditor.candidateUsersExpression.trim();
              return expr ? [expr] : [];
            })()
          : userTaskEditor.candidateUserIds;

      const candidateGroups = userTaskEditor.candidateGroupsRaw
        .split(",")
        .map((entry) => entry.trim())
        .filter((entry) => entry.length > 0);

      const dueDate = buildDueDate(userTaskEditor);

      const userFormMode = userTaskEditor.userFormMode;
      const userFormShortCode =
        userFormMode === "modal" || userFormMode === "page"
          ? userTaskEditor.userFormShortCode.trim()
          : "";

      await workflow.updateUserTaskProperties(handle, {
        id: userTaskEditor.id,
        name: userTaskEditor.name,
        assignee,
        candidateUsers,
        candidateGroups,
        dueDate,
        userFormMode,
        userFormShortCode: userFormShortCode || null
      });
      setUserTaskEditor(null);
    });

  const onPause = () =>
    runBusy("pausing the workflow", async () => {
      if (!currentModel) return;
      const updated = await pauseMutation.mutateAsync(currentModel.id);
      setCurrentModel(updated);
      setStatus(`Paused '${updated.name}'. Existing executions continue running; new starts are blocked until you resume.`);
    });

  const onResume = () =>
    runBusy("resuming the workflow", async () => {
      if (!currentModel) return;
      const updated = await resumeMutation.mutateAsync(currentModel.id);
      setCurrentModel(updated);
      setStatus(`Resumed '${updated.name}'. New executions can be started again.`);
    });

  const canPublish =
    !busy && !!currentModel && (dirty || currentModel.isDraft || currentModel.lastDeployment === null);
  const canStart =
    !busy
    && !!currentModel
    && !!currentModel.lastDeployment
    && currentModel.publishedVersionNumber !== null
    && !currentModel.isSuspended;
  const canTogglePause =
    !busy && !!currentModel && !!currentModel.lastDeployment;
  const isPaused = currentModel?.isSuspended === true;

  const onUpdateModelFromSidebar = useCallback(
    (next: WorkflowModel) => {
      setCurrentModel(next);
      setDirty(true);
    },
    [setCurrentModel, setDirty]
  );
  const sidebarPanels = useWorkflowSidebarPanels({
    currentModel,
    dirty,
    onUpdateModel: onUpdateModelFromSidebar
  });
  const activeSidebar =
    sidebarPanels.find((p) => p.id === sidebarActiveId) ?? null;

  return (
    <>
      <Group justify="space-between" align="flex-start" wrap="wrap" gap="md" mb="md">
        <Stack gap={4}>
          <Title order={1}>Workflow Studio</Title>
          <Text size="sm" c="dimmed" maw={720}>
            Select a saved workflow model, edit it in the browser, save drafts to AutoNate, publish
            to Flowable, and start new executions from the current model.
          </Text>
        </Stack>
        <Button
          variant="gradient"
          gradient={{ from: "#2680c2", to: "#0f609b", deg: 135 }}
          radius="xl"
          size="sm"
          onClick={() => setShowBpmnTypesModal(true)}
          title="View supported BPMN node types"
          leftSection={<i className="fa fa-sitemap" aria-hidden="true" />}
          rightSection={<i className="fa fa-arrow-right" aria-hidden="true" />}
        >
          Supported BPMN Types
        </Button>
      </Group>

      {error && (
        <Alert color="red" variant="light" mb="sm">
          {error}
        </Alert>
      )}
      {status && (
        <Alert color="green" variant="light" mb="sm">
          {status}
        </Alert>
      )}
      {warnings.length > 0 && (
        <Alert color="yellow" variant="light" title="Compatibility warnings" mb="sm">
          <List size="sm">
            {warnings.map((w, i) => (
              <List.Item key={i}>{w}</List.Item>
            ))}
          </List>
        </Alert>
      )}

      <Box className="workflow-toolbar">
        <div className="workflow-selector-panel">
          <Group gap="xs" wrap="nowrap" align="flex-end">
            <Select
              label="Workflow Model"
              placeholder={
                workflows.length === 0 ? "No workflow models yet" : "Select a workflow model"
              }
              value={currentModel?.id ?? null}
              onChange={(v) => onSelectionChange(v ?? "")}
              disabled={!!busy}
              data={sortedWorkflows.map((w) => ({ value: w.id, label: w.name }))}
              searchable
              clearable={false}
              style={{ flex: 1, minWidth: 240 }}
            />
            <ActionIcon
              variant="default"
              size="lg"
              onClick={() => setShowCreateModal(true)}
              disabled={!!busy}
              aria-label="Create workflow model"
              title="Create workflow model"
            >
              <i className="fa fa-plus" aria-hidden="true"></i>
            </ActionIcon>
          </Group>
        </div>

        <Group className="workflow-actions" gap="xs" wrap="wrap">
          <Button
            onClick={onSave}
            disabled={!handle || !!busy || !currentModel}
            title="Save"
          >
            Save
          </Button>
          <Button
            variant="outline"
            onClick={onPublish}
            disabled={!canPublish}
            title="Publish"
          >
            Publish
          </Button>
          <Button
            variant="outline"
            color="green"
            onClick={onStartInstance}
            disabled={!canStart}
            title={
              isPaused
                ? "Workflow is paused — resume to start a new instance"
                : "Start instance"
            }
            leftSection={<i className="fa fa-play" aria-hidden="true" />}
          >
            Start Instance
          </Button>
          {isPaused ? (
            <Button
              variant="outline"
              color="green"
              onClick={onResume}
              disabled={!canTogglePause}
              title="Resume — allow new instances to start"
              leftSection={<i className="fa fa-play" aria-hidden="true" />}
            >
              Resume
            </Button>
          ) : (
            <Button
              variant="outline"
              color="yellow"
              onClick={onPause}
              disabled={!canTogglePause}
              title="Pause — block new instances; existing runs continue"
              leftSection={<i className="fa fa-pause" aria-hidden="true" />}
            >
              Pause
            </Button>
          )}
        </Group>
      </Box>

      {busy && (
        <Text size="sm" c="dimmed" my="xs">
          Working on {busy}...
        </Text>
      )}

      <div
        className={`workflow-layout${activeSidebar ? " workflow-layout--sidebar-open" : ""}`}
      >
        <section className="workflow-main">
          {!currentModel ? (
            <div className="workflow-empty-state">
              <div className="workflow-empty-icon">
                <i className="fa fa-sitemap" aria-hidden="true"></i>
              </div>
              <h2>Create Your First Workflow</h2>
              <p>
                Workflow models live in the application database and are loaded into the modeler
                from there. Create one to start modeling.
              </p>
              <Button
                onClick={() => setShowCreateModal(true)}
                title="Create workflow model"
              >
                Create Workflow Model
              </Button>
            </div>
          ) : (
            <div className="workflow-shell">
              <div
                ref={containerRef}
                className="workflow-canvas"
                aria-label="BPMN modeler"
              ></div>
              {modelerLoading && (
                <Text size="sm" c="dimmed" px="md" py="xs">
                  Loading BPMN modeler...
                </Text>
              )}
              {modelerError && (
                <Text size="sm" c="red" px="md" py="xs">
                  {modelerError.message}
                </Text>
              )}
              <WorkflowSidebarRail
                panels={sidebarPanels}
                activeId={sidebarActiveId}
                onSelect={setSidebarActiveId}
              />
            </div>
          )}
        </section>

        {activeSidebar && (
          <WorkflowSidebarPanel
            panel={activeSidebar}
            onClose={() => setSidebarActiveId(null)}
          />
        )}
      </div>

      {showCreateModal && (
        <CreateWorkflowModal
          onClose={() => {
            if (busy) return;
            setShowCreateModal(false);
          }}
          onCreated={(model) => {
            qc.invalidateQueries({ queryKey: WORKFLOWS_QUERY_KEY });
            qc.invalidateQueries({ queryKey: WORKFLOW_LATEST_QUERY_KEY });
            setShowCreateModal(false);
            selectWorkflow(model);
            setStatus(`Created workflow model '${model.name}'.`);
          }}
          onError={(msg) => setError(msg)}
        />
      )}

      {scriptTaskEditor && (
        <ScriptTaskModal
          editor={scriptTaskEditor}
          onChange={setScriptTaskEditor}
          onClose={() => {
            if (busy) return;
            setScriptTaskEditor(null);
          }}
          onApply={applyScriptTask}
          disabled={!!busy || !handle}
        />
      )}

      {sequenceFlowEditor && (
        <SequenceFlowModal
          editor={sequenceFlowEditor}
          onChange={setSequenceFlowEditor}
          onClose={() => {
            if (busy) return;
            setSequenceFlowEditor(null);
          }}
          onApply={applySequenceFlow}
          disabled={!!busy || !handle}
        />
      )}

      {userTaskEditor && (
        <UserTaskModal
          editor={userTaskEditor}
          onChange={setUserTaskEditor}
          onClose={() => {
            if (busy) return;
            setUserTaskEditor(null);
          }}
          onApply={applyUserTask}
          disabled={!!busy || !handle}
        />
      )}

      {signalStartEditor && (
        <SignalStartEventModal
          editor={signalStartEditor}
          onChange={setSignalStartEditor}
          onClose={() => {
            if (busy) return;
            setSignalStartEditor(null);
          }}
          onApply={applySignalStart}
          disabled={!!busy || !handle}
        />
      )}

      {timerStartEditor && (
        <TimerStartEventModal
          editor={timerStartEditor}
          onChange={setTimerStartEditor}
          onClose={() => {
            if (busy) return;
            setTimerStartEditor(null);
          }}
          onApply={applyTimerStart}
          disabled={!!busy || !handle}
        />
      )}

      {timerIntermediateEditor && (
        <TimerIntermediateCatchEventModal
          editor={timerIntermediateEditor}
          onChange={setTimerIntermediateEditor}
          onClose={() => {
            if (busy) return;
            setTimerIntermediateEditor(null);
          }}
          onApply={applyTimerIntermediate}
          disabled={!!busy || !handle}
        />
      )}

      {serviceTaskEditor && (
        <ServiceTaskModal
          editor={serviceTaskEditor}
          onChange={setServiceTaskEditor}
          onClose={() => {
            if (busy) return;
            setServiceTaskEditor(null);
          }}
          onApply={applyServiceTask}
          disabled={!!busy || !handle}
        />
      )}

      {gatewayEditor && (
        <GatewayModal
          editor={gatewayEditor}
          onChange={setGatewayEditor}
          onClose={() => {
            if (busy) return;
            setGatewayEditor(null);
          }}
          onApply={applyGateway}
          disabled={!!busy || !handle}
        />
      )}

      {genericEditor && (
        <GenericElementModal
          editor={genericEditor}
          onChange={setGenericEditor}
          onClose={() => {
            if (busy) return;
            setGenericEditor(null);
          }}
          onApply={applyGeneric}
          disabled={!!busy || !handle}
        />
      )}

      {showBpmnTypesModal && (
        <BpmnTypesModal onClose={() => setShowBpmnTypesModal(false)} />
      )}
    </>
  );
}

type WorkflowSidebarPanel = {
  id: string;
  icon: string;
  label: string;
  render: () => React.ReactNode;
};

function useWorkflowSidebarPanels({
  currentModel,
  dirty,
  onUpdateModel
}: {
  currentModel: WorkflowModel | null;
  dirty: boolean;
  onUpdateModel: (model: WorkflowModel) => void;
}): WorkflowSidebarPanel[] {
  const { data: executions = [] } = useExecutions();

  const runningCount = useMemo(() => {
    if (!currentModel) return 0;
    return executions.filter(
      (e) => e.workflowModelName === currentModel.name && e.status === "Running"
    ).length;
  }, [executions, currentModel]);

  return useMemo<WorkflowSidebarPanel[]>(
    () => [
      {
        id: "model-info",
        icon: "fa fa-circle-info",
        label: "Model Information",
        render: () => (
          <ModelInformationPanel
            currentModel={currentModel}
            dirty={dirty}
            runningCount={runningCount}
          />
        )
      },
      {
        id: "model-config",
        icon: "fa fa-gear",
        label: "Model Configuration",
        render: () => (
          <ModelConfigurationPanel
            currentModel={currentModel}
            onUpdateModel={onUpdateModel}
          />
        )
      }
    ],
    [currentModel, dirty, runningCount, onUpdateModel]
  );
}

function WorkflowSidebarRail({
  panels,
  activeId,
  onSelect
}: {
  panels: WorkflowSidebarPanel[];
  activeId: string | null;
  onSelect: (id: string | null) => void;
}) {
  return (
    <div className="workflow-rsb-rail" role="tablist" aria-orientation="vertical">
      {panels.map((p) => {
        const selected = activeId === p.id;
        return (
          <button
            key={p.id}
            type="button"
            role="tab"
            aria-selected={selected}
            className={`workflow-rsb-rail-btn${selected ? " is-active" : ""}`}
            onClick={() => onSelect(selected ? null : p.id)}
            data-tooltip={p.label}
            aria-label={p.label}
          >
            <i className={p.icon} aria-hidden="true"></i>
          </button>
        );
      })}
    </div>
  );
}

function WorkflowSidebarPanel({
  panel,
  onClose
}: {
  panel: WorkflowSidebarPanel;
  onClose: () => void;
}) {
  return (
    <aside className="workflow-rsb-panel" role="region" aria-label={panel.label}>
      <div className="workflow-rsb-panel-header">
        <h2 className="workflow-rsb-panel-title">
          <i className={panel.icon} aria-hidden="true"></i>
          <span>{panel.label}</span>
        </h2>
        <button
          type="button"
          className="workflow-rsb-collapse-btn"
          onClick={onClose}
          aria-label="Collapse sidebar"
          title="Collapse"
        >
          <i className="fa fa-angles-right" aria-hidden="true"></i>
        </button>
      </div>
      <div className="workflow-rsb-panel-body">{panel.render()}</div>
    </aside>
  );
}

function ModelInformationPanel({
  currentModel,
  dirty,
  runningCount
}: {
  currentModel: WorkflowModel | null;
  dirty: boolean;
  runningCount: number;
}) {
  if (!currentModel) {
    return <p className="workflow-muted">No workflow model is selected.</p>;
  }

  const stateLabel: "Draft / unpublished" | "Active" | "Paused" | "Unknown" =
    currentModel.publishedVersionNumber === null || !currentModel.lastDeployment
      ? "Draft / unpublished"
      : currentModel.isSuspended === true
        ? "Paused"
        : currentModel.isSuspended === false
          ? "Active"
          : "Unknown";

  const stateClassName =
    stateLabel === "Active"
      ? "workflow-state-active"
      : stateLabel === "Paused"
        ? "workflow-state-paused"
        : stateLabel === "Draft / unpublished"
          ? "workflow-state-draft"
          : "workflow-state-unknown";

  const saveVersionDisplay = dirty
    ? `v${currentModel.draftVersionNumber} (unsaved)`
    : `v${currentModel.draftVersionNumber}`;

  return (
    <div className="workflow-model-info">
      <div className="workflow-model-info-header">
        <div className="workflow-model-info-name" title={currentModel.name}>
          {currentModel.name}
        </div>
        <div className="workflow-model-info-id" title={currentModel.id}>
          {currentModel.id}
        </div>
      </div>
      <dl className="workflow-meta">
        <div>
          <dt>Current Save Version</dt>
          <dd>{saveVersionDisplay}</dd>
        </div>
        <div>
          <dt>Current Publish Version</dt>
          <dd>
            {currentModel.publishedVersionNumber === null
              ? "Not published"
              : `v${currentModel.publishedVersionNumber}`}
          </dd>
        </div>
        <div>
          <dt>Last Updated</dt>
          <dd>{formatTimestamp(currentModel.updatedAtUtc)}</dd>
        </div>
        <div>
          <dt>Last Published</dt>
          <dd>
            {currentModel.lastDeployment
              ? formatTimestamp(currentModel.lastDeployment.deployedAtUtc)
              : "Never published"}
          </dd>
        </div>
        <div>
          <dt>Current State</dt>
          <dd>
            <span className={`workflow-state-pill ${stateClassName}`}>
              {stateLabel === "Paused" && (
                <i className="fa fa-circle-pause" aria-hidden="true"></i>
              )}
              {stateLabel}
            </span>
          </dd>
        </div>
        <div>
          <dt>Running Executions</dt>
          <dd>{runningCount}</dd>
        </div>
      </dl>
    </div>
  );
}

function ModelConfigurationPanel({
  currentModel,
  onUpdateModel
}: {
  currentModel: WorkflowModel | null;
  onUpdateModel: (model: WorkflowModel) => void;
}) {
  if (!currentModel) {
    return <p className="workflow-muted">No workflow model is selected.</p>;
  }

  return (
    <div className="workflow-config-sections">
      <DefaultProcessVariablesSection
        currentModel={currentModel}
        onUpdateModel={onUpdateModel}
      />
    </div>
  );
}

const DEFAULT_VARIABLE_TYPES: WorkflowDefaultVariableType[] = [
  "string",
  "number",
  "boolean",
  "json"
];

function DefaultProcessVariablesSection({
  currentModel,
  onUpdateModel
}: {
  currentModel: WorkflowModel;
  onUpdateModel: (model: WorkflowModel) => void;
}) {
  const referenced = useMemo(
    () => extractProcessVariables(currentModel.bpmnXml),
    [currentModel.bpmnXml]
  );
  const referencedNames = useMemo(
    () => referenced.map((r) => r.name),
    [referenced]
  );

  // Merge: every referenced variable gets a row (creating an empty default
  // entry on the fly with a usage-inferred type), plus any saved defaults
  // that aren't currently referenced (so the user doesn't lose them just
  // because the BPMN changed).
  const rows = useMemo(() => {
    const saved = currentModel.defaultVariables ?? [];
    const byName = new Map<string, WorkflowDefaultVariable>();
    for (const v of saved) byName.set(v.name, v);

    const referencedRows: {
      variable: WorkflowDefaultVariable;
      referenced: true;
      inferredType?: WorkflowDefaultVariableType;
    }[] = referenced.map(({ name, inferredType }) => ({
      variable:
        byName.get(name) ?? {
          name,
          type: inferredType ?? "string",
          value: null
        },
      referenced: true,
      inferredType
    }));

    const referencedSet = new Set(referencedNames);
    const orphanRows: {
      variable: WorkflowDefaultVariable;
      referenced: false;
      inferredType?: undefined;
    }[] = saved
      .filter((v) => !referencedSet.has(v.name))
      .map((v) => ({ variable: v, referenced: false }));

    return [...referencedRows, ...orphanRows];
  }, [currentModel.defaultVariables, referenced, referencedNames]);

  const updateVariable = (
    name: string,
    patch: Partial<Pick<WorkflowDefaultVariable, "type" | "value">>,
    seedType: WorkflowDefaultVariableType = "string"
  ) => {
    const current = currentModel.defaultVariables ?? [];
    let next: WorkflowDefaultVariable[];
    const existing = current.find((v) => v.name === name);
    if (existing) {
      next = current.map((v) => (v.name === name ? { ...v, ...patch } : v));
    } else {
      next = [
        ...current,
        { name, type: seedType, value: null, ...patch } as WorkflowDefaultVariable
      ];
    }
    onUpdateModel({
      ...currentModel,
      defaultVariables: next.length === 0 ? null : next
    });
  };

  const removeVariable = (name: string) => {
    const next = (currentModel.defaultVariables ?? []).filter(
      (v) => v.name !== name
    );
    onUpdateModel({
      ...currentModel,
      defaultVariables: next.length === 0 ? null : next
    });
  };

  const addCustomVariable = (name: string): string | null => {
    const trimmed = name.trim();
    if (!trimmed) {
      return "Enter a variable name.";
    }
    if (!/^[A-Za-z_$][A-Za-z0-9_$]*$/.test(trimmed)) {
      return "Variable names must start with a letter, _, or $ and contain only letters, digits, _, or $.";
    }
    const referencedSet = new Set(referencedNames);
    const savedNames = new Set(
      (currentModel.defaultVariables ?? []).map((v) => v.name)
    );
    if (referencedSet.has(trimmed) || savedNames.has(trimmed)) {
      return `'${trimmed}' is already in the list.`;
    }
    const next: WorkflowDefaultVariable[] = [
      ...(currentModel.defaultVariables ?? []),
      { name: trimmed, type: "string", value: null }
    ];
    onUpdateModel({ ...currentModel, defaultVariables: next });
    return null;
  };

  return (
    <section className="workflow-config-section">
      <h3 className="workflow-config-section-title">Default Process Variables</h3>
      <p className="workflow-config-section-copy">
        Variables referenced by scripts and expressions in this model, plus any
        custom ones you add. The default you set here is applied when an
        instance starts (callers can still override per-start).
      </p>
      {rows.length === 0 ? (
        <p className="workflow-muted">
          No process variables are referenced in this model yet — add a custom
          one below if your workflow needs an initial seed value.
        </p>
      ) : (
        <ul className="workflow-default-vars">
          {rows.map(({ variable, referenced, inferredType }) => {
            const saved = (currentModel.defaultVariables ?? []).some(
              (v) => v.name === variable.name
            );
            const showInferredHint =
              referenced && !saved && inferredType !== undefined;
            return (
              <li
                key={variable.name}
                className={`workflow-default-var${referenced ? "" : " is-custom"}`}
              >
                <div className="workflow-default-var-name">
                  <span title={variable.name}>{variable.name}</span>
                  <div className="workflow-default-var-name-tags">
                    {!referenced && (
                      <span
                        className="workflow-default-var-tag"
                        title="This variable isn't referenced by the BPMN — it'll still be passed in at start time."
                      >
                        Custom
                      </span>
                    )}
                    {showInferredHint && (
                      <span
                        className="workflow-default-var-tag workflow-default-var-tag-info"
                        title={`Type inferred from how '${variable.name}' is used in scripts and expressions.`}
                      >
                        inferred
                      </span>
                    )}
                    {!referenced && (
                      <button
                        type="button"
                        className="workflow-default-var-remove"
                        onClick={() => removeVariable(variable.name)}
                        aria-label={`Remove ${variable.name}`}
                        title="Remove this variable"
                      >
                        <i className="fa fa-xmark" aria-hidden="true"></i>
                      </button>
                    )}
                  </div>
                </div>
                <div className="workflow-default-var-controls">
                  <select
                    className="form-select form-select-sm"
                    value={variable.type}
                    onChange={(e) =>
                      updateVariable(
                        variable.name,
                        {
                          type: e.target.value as WorkflowDefaultVariableType,
                          value: coerceDefaultValue(
                            variable.value,
                            e.target.value as WorkflowDefaultVariableType
                          )
                        },
                        inferredType ?? "string"
                      )
                    }
                    aria-label={`Type for ${variable.name}`}
                  >
                    {DEFAULT_VARIABLE_TYPES.map((t) => (
                      <option key={t} value={t}>
                        {t}
                      </option>
                    ))}
                  </select>
                  <DefaultVariableValueInput
                    variable={variable}
                    onChange={(value) =>
                      updateVariable(
                        variable.name,
                        { value },
                        inferredType ?? "string"
                      )
                    }
                  />
                </div>
              </li>
            );
          })}
        </ul>
      )}
      <AddCustomVariableForm onAdd={addCustomVariable} />
    </section>
  );
}

function AddCustomVariableForm({
  onAdd
}: {
  onAdd: (name: string) => string | null;
}) {
  const [name, setName] = useState("");
  const [error, setError] = useState<string | null>(null);

  const submit = () => {
    const result = onAdd(name);
    if (result) {
      setError(result);
      return;
    }
    setName("");
    setError(null);
  };

  return (
    <div className="workflow-default-var-add">
      <div className="workflow-default-var-add-row">
        <input
          type="text"
          className="form-control form-control-sm"
          value={name}
          onChange={(e) => {
            setName(e.target.value);
            if (error) setError(null);
          }}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              submit();
            }
          }}
          placeholder="Add a custom variable…"
          aria-label="Custom variable name"
        />
        <button
          type="button"
          className="btn btn-sm btn-outline-primary"
          onClick={submit}
          disabled={name.trim() === ""}
        >
          <i className="fa fa-plus" aria-hidden="true"></i> Add
        </button>
      </div>
      {error && (
        <p className="workflow-default-var-add-error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}

function DefaultVariableValueInput({
  variable,
  onChange
}: {
  variable: WorkflowDefaultVariable;
  onChange: (value: WorkflowDefaultVariable["value"]) => void;
}) {
  const ariaLabel = `Default value for ${variable.name}`;
  if (variable.type === "boolean") {
    return (
      <select
        className="form-select form-select-sm"
        value={variable.value === true ? "true" : variable.value === false ? "false" : ""}
        onChange={(e) =>
          onChange(e.target.value === "" ? null : e.target.value === "true")
        }
        aria-label={ariaLabel}
      >
        <option value="">(not set)</option>
        <option value="true">true</option>
        <option value="false">false</option>
      </select>
    );
  }
  if (variable.type === "number") {
    return (
      <input
        type="number"
        className="form-control form-control-sm"
        value={variable.value === null || variable.value === undefined ? "" : String(variable.value)}
        onChange={(e) => {
          const raw = e.target.value;
          if (raw === "") {
            onChange(null);
            return;
          }
          const n = Number(raw);
          onChange(Number.isNaN(n) ? raw : n);
        }}
        aria-label={ariaLabel}
        placeholder="(not set)"
      />
    );
  }
  // string + json both edit as text. JSON is left as a raw string so the
  // user can author objects/arrays without us imposing a parser here; the
  // backend treats type="json" as raw JSON when applied at start.
  return (
    <input
      type="text"
      className="form-control form-control-sm"
      value={variable.value === null || variable.value === undefined ? "" : String(variable.value)}
      onChange={(e) => onChange(e.target.value === "" ? null : e.target.value)}
      aria-label={ariaLabel}
      placeholder={variable.type === "json" ? '{"...":...}' : "(not set)"}
    />
  );
}

function coerceDefaultValue(
  value: WorkflowDefaultVariable["value"],
  toType: WorkflowDefaultVariableType
): WorkflowDefaultVariable["value"] {
  if (value === null || value === undefined) return null;
  if (toType === "boolean") {
    if (typeof value === "boolean") return value;
    if (typeof value === "string") {
      if (value === "true") return true;
      if (value === "false") return false;
      return null;
    }
    return null;
  }
  if (toType === "number") {
    if (typeof value === "number") return value;
    if (typeof value === "string" && value !== "") {
      const n = Number(value);
      return Number.isNaN(n) ? null : n;
    }
    return null;
  }
  // string / json
  return typeof value === "string" ? value : String(value);
}

function CreateWorkflowModal({
  onClose,
  onCreated,
  onError
}: {
  onClose: () => void;
  onCreated: (model: WorkflowModel) => void;
  onError: (message: string) => void;
}) {
  const [name, setName] = useState("");
  const [busy, setBusy] = useState(false);
  const inputRef = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    inputRef.current?.focus();
  }, []);

  const onCreate = async () => {
    if (!name.trim()) return;
    setBusy(true);
    try {
      // Server-side prepare + save: build a prepared model from a blank BPMN starter.
      // We submit a model with just the name and an empty XML placeholder; the server
      // generates the starter via prepareWorkflow + saveWorkflow.
      const prepared = await prepareWorkflow({
        model: {
          id: crypto.randomUUID(),
          name: name.trim(),
          processKey: "",
          bpmnXml: STARTER_DIAGRAM_PLACEHOLDER,
          isDraft: true,
          draftVersionNumber: 1,
          publishedVersionNumber: null,
          lastDeployment: null,
          isSuspended: null,
          activeProcessInstanceId: null,
          defaultVariables: null,
          createdAtUtc: new Date().toISOString(),
          updatedAtUtc: new Date().toISOString()
        },
        elementSnapshots: []
      });

      if (prepared.errors.length > 0) {
        onError(prepared.errors.join(" "));
        return;
      }

      const saved = await saveWorkflow(prepared.model);
      onCreated(saved);
    } catch (err) {
      onError(describeError(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      opened
      onClose={onClose}
      title="Create Workflow Model"
      closeOnClickOutside={!busy}
      closeOnEscape={!busy}
      withCloseButton={!busy}
    >
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          Name the new workflow model. AutoNate will create a blank draft in the database and load
          it into the modeler.
        </Text>
        <TextInput
          ref={inputRef}
          label="Workflow Name"
          value={name}
          onChange={(e) => setName(e.currentTarget.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              onCreate();
            }
          }}
        />
        <Group justify="flex-end" gap="xs">
          <Button variant="default" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button onClick={onCreate} loading={busy} disabled={!name.trim()}>
            Create
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function ScriptTaskModal({
  editor,
  onChange,
  onClose,
  onApply,
  disabled
}: {
  editor: ScriptTaskEditor;
  onChange: (next: ScriptTaskEditor) => void;
  onClose: () => void;
  onApply: () => void;
  disabled: boolean;
}) {
  return (
    <Modal opened onClose={onClose} title="Script Task" size="xl">
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          Edit the selected BPMN script task. AutoNate saves the JavaScript body inline in the
          BPMN XML and validates it before save or publish.
        </Text>

        <Group gap="xs" wrap="wrap">
          <Code>{editor.id}</Code>
          <Code>{editor.type}</Code>
        </Group>

        <Group gap="md" grow align="flex-start" wrap="wrap">
          <TextInput
            label="Task Name"
            value={editor.name}
            onChange={(e) => onChange({ ...editor, name: e.currentTarget.value })}
          />
          <TextInput label="Script Format" value="javascript" readOnly />
          <TextInput
            label="Result Variable"
            value={editor.resultVariable}
            onChange={(e) => onChange({ ...editor, resultVariable: e.currentTarget.value })}
          />
        </Group>

        <Stack gap={4}>
          <Text size="sm" fw={500}>
            Script Body
          </Text>
          <Box
            className="workflow-script-task-editor"
            style={{
              border: "1px solid var(--mantine-color-default-border)",
              borderRadius: 4,
              overflow: "hidden",
              minHeight: 280
            }}
          >
            <CodeMirror
              value={editor.script}
              onChange={(value) => onChange({ ...editor, script: value })}
              height="280px"
              extensions={[javascript()]}
              basicSetup={{
                lineNumbers: true,
                highlightActiveLineGutter: true,
                highlightSpecialChars: true,
                history: true,
                foldGutter: true,
                drawSelection: true,
                dropCursor: true,
                allowMultipleSelections: true,
                indentOnInput: true,
                syntaxHighlighting: true,
                bracketMatching: true,
                closeBrackets: true,
                autocompletion: true,
                rectangularSelection: true,
                crosshairCursor: true,
                highlightActiveLine: true,
                highlightSelectionMatches: true,
                closeBracketsKeymap: true,
                defaultKeymap: true,
                searchKeymap: true,
                historyKeymap: true,
                foldKeymap: true,
                completionKeymap: true,
                lintKeymap: true
              }}
            />
          </Box>
        </Stack>

        <Group justify="flex-end" gap="xs">
          <Button variant="default" onClick={onClose}>
            Close
          </Button>
          <Button onClick={onApply} disabled={disabled}>
            Apply
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function SignalStartEventModal({
  editor,
  onChange,
  onClose,
  onApply,
  disabled
}: {
  editor: SignalStartEventEditor;
  onChange: (next: SignalStartEventEditor) => void;
  onClose: () => void;
  onApply: () => void;
  disabled: boolean;
}) {
  const { data: catalog } = useEventCatalog();
  const { data: recordTypes } = useRecordTypes(false);

  const carriesRecordType = useMemo(
    () => eventCarriesRecordType(catalog, editor.signalTopic, editor.signalName),
    [catalog, editor.signalTopic, editor.signalName]
  );

  // Merge static catalog entries (events Flowable / future publishers raise)
  // with dynamic registrations (event types other workflows are listening for)
  // so the user can pick anything the system knows about, plus type free-form.
  const knownEvents = useMemo(() => {
    const entries = new Map<string, { topic: string; eventType: string; description?: string }>();
    // Composite key is topic + eventType joined by U+0000, which cannot occur
    // in either value — a plain concatenation would collide ("ab"+"c" vs
    // "a"+"bc"). It was previously a *literal* NUL byte in this file, which
    // made grep classify all 3,900 lines as binary and skip them (#116); the
    // escape keeps the exact same runtime key without that.
    for (const category of catalog?.categories ?? []) {
      for (const evt of category.events) {
        entries.set(`${evt.topic}\u0000${evt.eventType}`, {
          topic: evt.topic,
          eventType: evt.eventType,
          description: evt.summary
        });
      }
    }
    for (const reg of catalog?.workflowRegistrations ?? []) {
      const key = `${reg.topic}\u0000${reg.eventType}`;
      if (!entries.has(key)) {
        entries.set(key, { topic: reg.topic, eventType: reg.eventType });
      }
    }
    return [...entries.values()].sort((a, b) =>
      a.topic === b.topic ? a.eventType.localeCompare(b.eventType) : a.topic.localeCompare(b.topic)
    );
  }, [catalog]);

  const knownTopics = useMemo(() => {
    const set = new Set<string>([DEFAULT_SIGNAL_TOPIC]);
    for (const evt of knownEvents) {
      set.add(evt.topic);
    }
    return [...set].sort((a, b) => a.localeCompare(b));
  }, [knownEvents]);

  // When the user has typed (or defaulted to) a topic, only suggest event
  // types that match — saves them from picking process.started while their
  // topic is set to orders.events. With no topic typed, fall back to all.
  const effectiveTopic = editor.signalTopic.trim() || DEFAULT_SIGNAL_TOPIC;
  const eventTypeSuggestions = useMemo(() => {
    const filtered = knownEvents.filter((evt) => evt.topic === effectiveTopic);
    return filtered.length > 0 ? filtered : knownEvents;
  }, [knownEvents, effectiveTopic]);

  const topicListId = `signal-topic-${editor.id}`;
  const eventTypeListId = `signal-event-type-${editor.id}`;
  const missingEventType = editor.signalName.trim().length === 0;

  return (
    <Modal opened onClose={onClose} title="Signal Start Event" size="lg">
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          Configure a Dapr pub/sub event that starts this workflow. AutoNate listens on the
          configured Topic and starts a new instance when an incoming message&apos;s{" "}
          <Code>eventType</Code> field matches the Event Type. The full payload is exposed to the
          workflow as a process variable named <Code>eventData</Code> (a JSON string —
          <Code> JSON.parse(eventData)</Code> in script tasks).
        </Text>

        <Group gap="xs" wrap="wrap">
          <Code>{editor.id}</Code>
          <Code>{editor.type}</Code>
        </Group>

        <TextInput
          label="Event Name (optional)"
          value={editor.name}
          onChange={(e) => onChange({ ...editor, name: e.currentTarget.value })}
          placeholder="Order placed"
        />

        <TextInput
          label="Topic"
          list={topicListId}
          value={editor.signalTopic}
          onChange={(e) => onChange({ ...editor, signalTopic: e.currentTarget.value })}
          placeholder={DEFAULT_SIGNAL_TOPIC}
          description={
            <>
              Dapr pub/sub topic. Defaults to <Code>{DEFAULT_SIGNAL_TOPIC}</Code> when blank. Adding
              a new topic requires a Dapr sidecar restart for messages to flow.
            </>
          }
        />
        <datalist id={topicListId}>
          {knownTopics.map((topic) => (
            <option key={topic} value={topic} />
          ))}
        </datalist>

        <TextInput
          label="Event Type"
          list={eventTypeListId}
          value={editor.signalName}
          onChange={(e) => onChange({ ...editor, signalName: e.currentTarget.value })}
          placeholder="OrderPlaced"
          description={
            <>
              Matched verbatim against the top-level <Code>eventType</Code> field of incoming
              messages. Required.
            </>
          }
        />
        <datalist id={eventTypeListId}>
          {eventTypeSuggestions.map((evt) => (
            <option
              key={`${evt.topic}:${evt.eventType}`}
              value={evt.eventType}
              label={evt.description}
            />
          ))}
        </datalist>

        {!carriesRecordType && editor.recordTypeShortCodes.length > 0 && (
          <Alert color="yellow" variant="light">
            This event type doesn&rsquo;t carry a record type — the configured record-type filter
            will be cleared when you apply.
          </Alert>
        )}

        {carriesRecordType && (
          <Stack gap={4}>
            <Text size="sm" fw={500}>
              Record types (optional)
            </Text>
            <RecordTypeMultiSelect
              selected={editor.recordTypeShortCodes}
              options={recordTypes ?? []}
              onChange={(next) => onChange({ ...editor, recordTypeShortCodes: next })}
            />
            <Text size="xs" c="dimmed">
              Empty = all record types match. When set, only payloads whose <Code>recordTypeId</Code>{" "}
              matches one of these will start this workflow.
            </Text>
          </Stack>
        )}

        <Group justify="flex-end" gap="xs">
          <Button variant="default" onClick={onClose}>
            Close
          </Button>
          <Button onClick={onApply} disabled={disabled || missingEventType}>
            Apply
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function RecordTypeMultiSelect({
  selected,
  options,
  onChange
}: {
  selected: string[];
  options: { shortCode: string; name: string; isArchived?: boolean }[];
  onChange: (next: string[]) => void;
}) {
  const toggle = (shortCode: string) => {
    if (selected.includes(shortCode)) {
      onChange(selected.filter((s) => s !== shortCode));
    } else {
      onChange([...selected, shortCode]);
    }
  };

  if (options.length === 0) {
    return (
      <Text size="xs" c="dimmed">
        No record types defined yet. Empty selection means &ldquo;match all record types.&rdquo;
      </Text>
    );
  }

  return (
    <Group gap="xs" wrap="wrap">
      {options.map((opt) => (
        <Checkbox
          key={opt.shortCode}
          checked={selected.includes(opt.shortCode)}
          onChange={() => toggle(opt.shortCode)}
          label={opt.name + (opt.isArchived ? " (archived)" : "")}
        />
      ))}
    </Group>
  );
}

type TimerStartEventModalProps = {
  editor: TimerStartEventEditor;
  onChange: (next: TimerStartEventEditor) => void;
  onClose: () => void;
  onApply: () => void;
  disabled: boolean;
};

const ORDINAL_LABELS: Record<Ordinal, string> = {
  "1": "First",
  "2": "Second",
  "3": "Third",
  "4": "Fourth",
  L: "Last"
};

const WEEK_DAY_LABELS: Record<WeekDay, string> = {
  MON: "Mon",
  TUE: "Tue",
  WED: "Wed",
  THU: "Thu",
  FRI: "Fri",
  SAT: "Sat",
  SUN: "Sun"
};

const MONTH_LABELS = [
  "January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December"
];

function TimerStartEventModal({
  editor,
  onChange,
  onClose,
  onApply,
  disabled
}: TimerStartEventModalProps) {
  const setRecurrence = (mutator: (r: RecurrenceState) => RecurrenceState) => {
    onChange({ ...editor, recurrence: mutator(editor.recurrence) });
  };

  const generation = useMemo(() => generateCron(editor.recurrence), [editor.recurrence]);
  const generatedCron = generation.ok ? generation.cron : "";
  const generatorError = generation.ok ? null : generation.error;
  const summary = useMemo(() => describeRecurrence(editor.recurrence), [editor.recurrence]);

  const timeValue = `${pad2(editor.recurrence.hour)}:${pad2(editor.recurrence.minute)}`;

  const onTimeChange = (raw: string) => {
    const [h = "0", m = "0"] = raw.split(":");
    setRecurrence((r) => ({ ...r, hour: String(parseInt(h, 10) || 0), minute: String(parseInt(m, 10) || 0) }));
  };

  const toggleWeekDay = (day: WeekDay) => {
    setRecurrence((r) => {
      const next = r.weeklyDays.includes(day)
        ? r.weeklyDays.filter((d) => d !== day)
        : [...r.weeklyDays, day];
      return { ...r, weeklyDays: next };
    });
  };

  const applyDisabled =
    disabled ||
    (!editor.rawCronOverride && !generation.ok) ||
    (editor.rawCronOverride && editor.rawCronText.trim().length === 0);

  return (
    <Modal opened onClose={onClose} title="Timer Start Event" size="lg">
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          Schedule this workflow with an Outlook-style recurrence picker. Times use the Flowable
          engine&apos;s timezone (UTC by default) — pick the time as it should fire on the server.
        </Text>

        <Group gap="xs" wrap="wrap">
          <Code>{editor.id}</Code>
          <Code>{editor.type}</Code>
        </Group>

        {editor.parseError && (
          <Alert color="yellow" variant="light" role="alert">
            {editor.parseError}
          </Alert>
        )}

        <label className="workflow-field">
          <span>Event Name (optional)</span>
          <input
            className="form-control"
            value={editor.name}
            onChange={(e) => onChange({ ...editor, name: e.target.value })}
            placeholder="Daily reminder"
          />
        </label>

        <fieldset disabled={editor.rawCronOverride} className="workflow-field">
          <legend>
            <span>Pattern</span>
          </legend>
          <select
            className="form-select"
            value={editor.recurrence.mode}
            onChange={(e) =>
              setRecurrence((r) => ({ ...r, mode: e.target.value as TimerMode }))
            }
          >
            <option value="daily">Daily</option>
            <option value="weekly">Weekly</option>
            <option value="monthly">Monthly</option>
            <option value="yearly">Yearly</option>
          </select>
        </fieldset>

        {!editor.rawCronOverride && editor.recurrence.mode === "daily" && (
          <div className="workflow-field">
            <span>Recurrence</span>
            <div className="d-flex align-items-center gap-2 mt-1">
              <span>Every</span>
              <input
                type="number"
                min={1}
                max={31}
                className="form-control"
                style={{ width: "5rem" }}
                value={editor.recurrence.dailyEveryN}
                disabled={editor.recurrence.dailyWeekdaysOnly}
                onChange={(e) =>
                  setRecurrence((r) => ({ ...r, dailyEveryN: e.target.value }))
                }
              />
              <span>day(s)</span>
            </div>
            <label className="form-check mt-2">
              <input
                type="checkbox"
                className="form-check-input"
                checked={editor.recurrence.dailyWeekdaysOnly}
                onChange={(e) =>
                  setRecurrence((r) => ({ ...r, dailyWeekdaysOnly: e.target.checked }))
                }
              />
              <span className="form-check-label">Weekdays only (Mon–Fri)</span>
            </label>
          </div>
        )}

        {!editor.rawCronOverride && editor.recurrence.mode === "weekly" && (
          <div className="workflow-field">
            <span>Recurrence</span>
            <div className="d-flex align-items-center gap-2 mt-1">
              <span>Every</span>
              <input
                type="number"
                min={1}
                max={52}
                className="form-control"
                style={{ width: "5rem" }}
                value={editor.recurrence.weeklyEveryN}
                onChange={(e) =>
                  setRecurrence((r) => ({ ...r, weeklyEveryN: e.target.value }))
                }
              />
              <span>week(s) on:</span>
            </div>
            <div className="btn-group mt-2" role="group" aria-label="Days of the week">
              {WEEK_DAYS.map((day) => {
                const active = editor.recurrence.weeklyDays.includes(day);
                return (
                  <button
                    key={day}
                    type="button"
                    className={`btn btn-sm ${active ? "btn-primary" : "btn-outline-primary"}`}
                    onClick={() => toggleWeekDay(day)}
                  >
                    {WEEK_DAY_LABELS[day]}
                  </button>
                );
              })}
            </div>
          </div>
        )}

        {!editor.rawCronOverride && editor.recurrence.mode === "monthly" && (
          <div className="workflow-field">
            <span>Recurrence</span>
            <div className="form-check mt-1">
              <input
                type="radio"
                className="form-check-input"
                id="timer-monthly-dom"
                checked={editor.recurrence.monthlyKind === "dayOfMonth"}
                onChange={() =>
                  setRecurrence((r) => ({ ...r, monthlyKind: "dayOfMonth" as MonthlyKind }))
                }
              />
              <label className="form-check-label d-flex align-items-center gap-2" htmlFor="timer-monthly-dom">
                <span>Day</span>
                <input
                  type="number"
                  min={1}
                  max={31}
                  className="form-control"
                  style={{ width: "5rem" }}
                  value={editor.recurrence.monthlyDayOfMonth}
                  disabled={editor.recurrence.monthlyKind !== "dayOfMonth"}
                  onChange={(e) =>
                    setRecurrence((r) => ({ ...r, monthlyDayOfMonth: e.target.value }))
                  }
                />
                <span>of every</span>
                <input
                  type="number"
                  min={1}
                  max={12}
                  className="form-control"
                  style={{ width: "5rem" }}
                  value={editor.recurrence.monthlyEveryN}
                  disabled={editor.recurrence.monthlyKind !== "dayOfMonth"}
                  onChange={(e) =>
                    setRecurrence((r) => ({ ...r, monthlyEveryN: e.target.value }))
                  }
                />
                <span>month(s)</span>
              </label>
            </div>
            <div className="form-check mt-2">
              <input
                type="radio"
                className="form-check-input"
                id="timer-monthly-ord"
                checked={editor.recurrence.monthlyKind === "ordinalWeekday"}
                onChange={() =>
                  setRecurrence((r) => ({ ...r, monthlyKind: "ordinalWeekday" as MonthlyKind }))
                }
              />
              <label className="form-check-label d-flex align-items-center gap-2" htmlFor="timer-monthly-ord">
                <span>The</span>
                <select
                  className="form-select"
                  style={{ width: "auto" }}
                  value={editor.recurrence.monthlyOrdinal}
                  disabled={editor.recurrence.monthlyKind !== "ordinalWeekday"}
                  onChange={(e) =>
                    setRecurrence((r) => ({ ...r, monthlyOrdinal: e.target.value as Ordinal }))
                  }
                >
                  {(["1", "2", "3", "4", "L"] as Ordinal[]).map((ord) => (
                    <option key={ord} value={ord}>
                      {ORDINAL_LABELS[ord]}
                    </option>
                  ))}
                </select>
                <select
                  className="form-select"
                  style={{ width: "auto" }}
                  value={editor.recurrence.monthlyOrdinalDay}
                  disabled={editor.recurrence.monthlyKind !== "ordinalWeekday"}
                  onChange={(e) =>
                    setRecurrence((r) => ({ ...r, monthlyOrdinalDay: e.target.value as WeekDay }))
                  }
                >
                  {WEEK_DAYS.map((d) => (
                    <option key={d} value={d}>
                      {WEEK_DAY_LABELS[d]}
                    </option>
                  ))}
                </select>
                <span>of every</span>
                <input
                  type="number"
                  min={1}
                  max={12}
                  className="form-control"
                  style={{ width: "5rem" }}
                  value={editor.recurrence.monthlyEveryN}
                  disabled={editor.recurrence.monthlyKind !== "ordinalWeekday"}
                  onChange={(e) =>
                    setRecurrence((r) => ({ ...r, monthlyEveryN: e.target.value }))
                  }
                />
                <span>month(s)</span>
              </label>
            </div>
          </div>
        )}

        {!editor.rawCronOverride && editor.recurrence.mode === "yearly" && (
          <div className="workflow-field">
            <span>Recurrence</span>
            <div className="d-flex align-items-center gap-2 mt-1">
              <span>Every year on</span>
              <select
                className="form-select"
                style={{ width: "auto" }}
                value={editor.recurrence.yearlyMonth}
                onChange={(e) =>
                  setRecurrence((r) => ({ ...r, yearlyMonth: e.target.value }))
                }
              >
                {MONTH_LABELS.map((label, i) => (
                  <option key={label} value={String(i + 1)}>
                    {label}
                  </option>
                ))}
              </select>
              <input
                type="number"
                min={1}
                max={31}
                className="form-control"
                style={{ width: "5rem" }}
                value={editor.recurrence.yearlyDay}
                onChange={(e) =>
                  setRecurrence((r) => ({ ...r, yearlyDay: e.target.value }))
                }
              />
            </div>
          </div>
        )}

        <label className="workflow-field">
          <span>Time of day</span>
          <input
            type="time"
            className="form-control"
            style={{ width: "10rem" }}
            value={timeValue}
            disabled={editor.rawCronOverride}
            onChange={(e) => onTimeChange(e.target.value)}
          />
        </label>

        <label className="workflow-field">
          <span>End by (optional)</span>
          <input
            type="date"
            className="form-control"
            style={{ width: "12rem" }}
            value={editor.endDate}
            onChange={(e) => onChange({ ...editor, endDate: e.target.value })}
          />
          <p className="workflow-modal-note">
            Leave blank to recur indefinitely. Otherwise, no instances start after this date (engine timezone).
          </p>
        </label>

        {generatorError && !editor.rawCronOverride && (
          <div className="alert alert-danger" role="alert">
            {generatorError}
            {" Open Advanced below and override with a raw cron expression."}
          </div>
        )}

        {!editor.rawCronOverride && generation.ok && (
          <p className="workflow-modal-note" aria-live="polite">
            <strong>Schedule:</strong> {summary}
            {generation.warnings.map((w, i) => (
              <span key={i}>
                <br />
                <em>Note: {w}</em>
              </span>
            ))}
          </p>
        )}

        <details
          open={editor.advancedOpen}
          onToggle={(e) =>
            onChange({ ...editor, advancedOpen: (e.target as HTMLDetailsElement).open })
          }
        >
          <summary>Advanced</summary>
          <label className="workflow-field mt-2">
            <span>Generated cron expression</span>
            <input
              className="form-control font-monospace"
              readOnly
              value={generatedCron}
              placeholder="(Configure recurrence above to see the generated cron)"
            />
          </label>
          <label className="form-check mt-2">
            <input
              type="checkbox"
              className="form-check-input"
              checked={editor.rawCronOverride}
              onChange={(e) =>
                onChange({
                  ...editor,
                  rawCronOverride: e.target.checked,
                  rawCronText: e.target.checked
                    ? editor.rawCronText || generatedCron
                    : editor.rawCronText
                })
              }
            />
            <span className="form-check-label">Override with raw cron expression</span>
          </label>
          {editor.rawCronOverride && (
            <label className="workflow-field mt-2">
              <span>Raw cron (Quartz 6-field)</span>
              <input
                className="form-control font-monospace"
                value={editor.rawCronText}
                onChange={(e) => onChange({ ...editor, rawCronText: e.target.value })}
                placeholder="0 0 9 * * ?"
              />
              <p className="workflow-modal-note">
                Format: <code>seconds minutes hours day-of-month month day-of-week</code>. Example:{" "}
                <code>0 0 9 ? * MON-FRI</code> = 9:00 AM every weekday.
              </p>
            </label>
          )}
        </details>

        <Group justify="flex-end" gap="xs">
          <Button variant="default" onClick={onClose}>
            Close
          </Button>
          <Button onClick={onApply} disabled={applyDisabled}>
            Apply
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

type TimerIntermediateCatchEventModalProps = {
  editor: TimerIntermediateCatchEventEditor;
  onChange: (next: TimerIntermediateCatchEventEditor) => void;
  onClose: () => void;
  onApply: () => void;
  disabled: boolean;
};

function TimerIntermediateCatchEventModal({
  editor,
  onChange,
  onClose,
  onApply,
  disabled
}: TimerIntermediateCatchEventModalProps) {
  const setMode = (mode: TimerIntermediateMode) => onChange({ ...editor, mode });
  const setDurationKind = (durationKind: TimerIntermediateValueKind) =>
    onChange({ ...editor, durationKind });
  const setDateKind = (dateKind: TimerIntermediateValueKind) => onChange({ ...editor, dateKind });

  const activeValueEmpty =
    editor.mode === "duration"
      ? (editor.durationKind === "expression"
          ? editor.durationExpression
          : editor.durationLiteral
        ).trim().length === 0
      : (editor.dateKind === "expression" ? editor.dateExpression : editor.dateLiteral).trim()
          .length === 0;

  return (
    <Modal opened onClose={onClose} title="Timer Intermediate Catch Event" size="lg">
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          Pause the workflow until either a fixed delay has elapsed since this node was reached or
          a specific date/time arrives. Use a literal ISO 8601 value, or a Flowable expression like{" "}
          <Code>{"${execution.getVariable('reminderDate')}"}</Code> to compute it from process
          variables at runtime.
        </Text>

        <Group gap="xs" wrap="wrap">
          <Code>{editor.id}</Code>
          <Code>{editor.type}</Code>
        </Group>

        <label className="workflow-field">
          <span>Event Name (optional)</span>
          <input
            className="form-control"
            value={editor.name}
            onChange={(e) => onChange({ ...editor, name: e.target.value })}
            placeholder="Wait for review window"
          />
        </label>

        <fieldset className="workflow-field">
          <legend>
            <span>Trigger</span>
          </legend>
          <div className="form-check">
            <input
              type="radio"
              className="form-check-input"
              id="timer-catch-mode-duration"
              checked={editor.mode === "duration"}
              onChange={() => setMode("duration")}
            />
            <label className="form-check-label" htmlFor="timer-catch-mode-duration">
              Duration after node start
            </label>
          </div>
          <div className="form-check">
            <input
              type="radio"
              className="form-check-input"
              id="timer-catch-mode-date"
              checked={editor.mode === "date"}
              onChange={() => setMode("date")}
            />
            <label className="form-check-label" htmlFor="timer-catch-mode-date">
              Specific date / time
            </label>
          </div>
        </fieldset>

        {editor.mode === "duration" && (
          <fieldset className="workflow-field">
            <legend>
              <span>Duration</span>
            </legend>
            <div className="form-check form-check-inline">
              <input
                type="radio"
                className="form-check-input"
                id="timer-catch-duration-literal"
                checked={editor.durationKind === "literal"}
                onChange={() => setDurationKind("literal")}
              />
              <label className="form-check-label" htmlFor="timer-catch-duration-literal">
                Hard-coded
              </label>
            </div>
            <div className="form-check form-check-inline">
              <input
                type="radio"
                className="form-check-input"
                id="timer-catch-duration-expression"
                checked={editor.durationKind === "expression"}
                onChange={() => setDurationKind("expression")}
              />
              <label className="form-check-label" htmlFor="timer-catch-duration-expression">
                Expression
              </label>
            </div>
            {editor.durationKind === "literal" ? (
              <>
                <input
                  className="form-control mt-2"
                  value={editor.durationLiteral}
                  onChange={(e) => onChange({ ...editor, durationLiteral: e.target.value })}
                  placeholder="PT15M"
                />
                <p className="workflow-modal-note">
                  ISO 8601 duration — for example <code>PT15M</code> (15 minutes), <code>PT2H</code>{" "}
                  (2 hours), <code>P1D</code> (1 day), <code>P1DT12H</code> (1 day 12 hours).
                </p>
              </>
            ) : (
              <>
                <textarea
                  className="form-control workflow-expression-editor mt-2"
                  rows={3}
                  spellCheck={false}
                  value={editor.durationExpression}
                  onChange={(e) => onChange({ ...editor, durationExpression: e.target.value })}
                  placeholder="${execution.getVariable('waitDuration')}"
                />
                <p className="workflow-modal-note">
                  Flowable expression evaluated when the token reaches this event. Must resolve to
                  an ISO 8601 duration string like <code>PT15M</code>.
                </p>
              </>
            )}
          </fieldset>
        )}

        {editor.mode === "date" && (
          <fieldset className="workflow-field">
            <legend>
              <span>Date / Time</span>
            </legend>
            <div className="form-check form-check-inline">
              <input
                type="radio"
                className="form-check-input"
                id="timer-catch-date-literal"
                checked={editor.dateKind === "literal"}
                onChange={() => setDateKind("literal")}
              />
              <label className="form-check-label" htmlFor="timer-catch-date-literal">
                Hard-coded
              </label>
            </div>
            <div className="form-check form-check-inline">
              <input
                type="radio"
                className="form-check-input"
                id="timer-catch-date-expression"
                checked={editor.dateKind === "expression"}
                onChange={() => setDateKind("expression")}
              />
              <label className="form-check-label" htmlFor="timer-catch-date-expression">
                Expression
              </label>
            </div>
            {editor.dateKind === "literal" ? (
              <>
                <input
                  className="form-control mt-2"
                  value={editor.dateLiteral}
                  onChange={(e) => onChange({ ...editor, dateLiteral: e.target.value })}
                  placeholder="2026-12-31T09:00:00"
                />
                <p className="workflow-modal-note">
                  ISO 8601 date or date/time — <code>YYYY-MM-DD</code> or{" "}
                  <code>YYYY-MM-DDTHH:mm:ss</code>. Times use the Flowable engine's timezone (UTC by
                  default).
                </p>
              </>
            ) : (
              <>
                <textarea
                  className="form-control workflow-expression-editor mt-2"
                  rows={3}
                  spellCheck={false}
                  value={editor.dateExpression}
                  onChange={(e) => onChange({ ...editor, dateExpression: e.target.value })}
                  placeholder="${execution.getVariable('reminderDate')}"
                />
                <p className="workflow-modal-note">
                  Flowable expression evaluated when the token reaches this event. Must resolve to
                  an ISO 8601 date or date/time string.
                </p>
              </>
            )}
          </fieldset>
        )}

        <Group justify="flex-end" gap="xs">
          <Button variant="default" onClick={onClose}>
            Close
          </Button>
          <Button onClick={onApply} disabled={disabled || activeValueEmpty}>
            Apply
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

type ServiceTaskModalProps = {
  editor: ServiceTaskEditor;
  onChange: (next: ServiceTaskEditor) => void;
  onClose: () => void;
  onApply: () => void;
  disabled: boolean;
};

function ServiceTaskModal({
  editor,
  onChange,
  onClose,
  onApply,
  disabled
}: ServiceTaskModalProps) {
  const { data: behaviors = [], isLoading, error } = useWorkflowBehaviors();
  const selected = behaviors.find((b) => b.key === editor.behaviorKey) ?? null;

  return (
    <Modal opened onClose={onClose} title="Service Task" size="lg">
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          Run a predefined AutoNate routine when the workflow reaches this step. The behavior
          receives every process variable plus execution metadata, and may write process variables
          back for downstream steps to branch on.
        </Text>

        <Group gap="xs" wrap="wrap">
          <Code>{editor.id}</Code>
          <Code>{editor.type}</Code>
        </Group>

        <label className="workflow-field">
          <span>Task Name (optional)</span>
          <input
            className="form-control"
            value={editor.name}
            onChange={(e) => onChange({ ...editor, name: e.target.value })}
            placeholder="Unlock account"
          />
        </label>

        <label className="workflow-field">
          <span>Type</span>
          <select
            className="form-select"
            value={editor.kind}
            onChange={(e) => onChange({ ...editor, kind: e.target.value as ServiceTaskKind })}
          >
            <option value="behavior">Behavior</option>
          </select>
          <p className="workflow-modal-note">
            Behavior runs a curated routine inside AutoNate. More service-task types (HTTP webhook,
            etc.) will appear here as they ship.
          </p>
        </label>

        {editor.kind === "behavior" && (
          <label className="workflow-field">
            <span>Behavior</span>
            {error ? (
              <div className="alert alert-danger" role="alert">
                Failed to load workflow behaviors. Try reopening this modal.
              </div>
            ) : (
              <select
                className="form-select"
                value={editor.behaviorKey}
                disabled={isLoading}
                onChange={(e) => onChange({ ...editor, behaviorKey: e.target.value })}
              >
                <option value="">{isLoading ? "Loading…" : "Select a behavior…"}</option>
                {behaviors.map((behavior) => (
                  <option key={behavior.key} value={behavior.key}>
                    {behavior.displayName}
                  </option>
                ))}
                {/* If the saved key isn't in the catalog (plugin disabled, key
                    renamed, etc.), keep it visible so authors can still see
                    what's wired up before changing it. */}
                {editor.behaviorKey && !behaviors.some((b) => b.key === editor.behaviorKey) && (
                  <option value={editor.behaviorKey}>
                    {editor.behaviorKey} (not registered on this server)
                  </option>
                )}
              </select>
            )}
            {selected?.description && (
              <p className="workflow-modal-note">{selected.description}</p>
            )}
            {!selected && editor.behaviorKey && (
              <p className="workflow-modal-note text-warning">
                The selected behavior key is not registered on this server. Saving will keep the
                key, but the workflow can't run until a matching behavior is registered.
              </p>
            )}
          </label>
        )}

        <Group justify="flex-end" gap="xs">
          <Button variant="default" onClick={onClose}>
            Close
          </Button>
          <Button onClick={onApply} disabled={disabled || !editor.behaviorKey.trim()}>
            Apply
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

// Humanize a BPMN moddle type (e.g. "bpmn:ExclusiveGateway") for the modal
// header. The fallback editor catches every type without a dedicated modal,
// so this is the only place users see the raw $type rendered as a label.
function humanizeBpmnType(type: string): string {
  const local = type.includes(":") ? type.split(":")[1] : type;
  return local.replace(/([A-Z])/g, " $1").trim();
}

function GenericElementModal({
  editor,
  onChange,
  onClose,
  onApply,
  disabled
}: {
  editor: GenericElementEditor;
  onChange: (next: GenericElementEditor) => void;
  onClose: () => void;
  onApply: () => void;
  disabled: boolean;
}) {
  const heading = humanizeBpmnType(editor.type);
  return (
    <Modal opened onClose={onClose} title={heading}>
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          Edit the display name of this element. Additional configuration for this node type will
          appear here as it ships.
        </Text>

        <Group gap="xs" wrap="wrap">
          <Code>{editor.id}</Code>
          <Code>{editor.type}</Code>
        </Group>

        <TextInput
          label="Name"
          value={editor.name}
          onChange={(e) => onChange({ ...editor, name: e.currentTarget.value })}
        />

        <Group justify="flex-end" gap="xs">
          <Button variant="default" onClick={onClose}>
            Close
          </Button>
          <Button onClick={onApply} disabled={disabled}>
            Apply
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function pad2(value: string): string {
  const n = parseInt(value, 10);
  if (!Number.isFinite(n)) return "00";
  return n.toString().padStart(2, "0");
}

function SequenceFlowModal({
  editor,
  onChange,
  onClose,
  onApply,
  disabled
}: {
  editor: SequenceFlowEditor;
  onChange: (next: SequenceFlowEditor) => void;
  onClose: () => void;
  onApply: () => void;
  disabled: boolean;
}) {
  return (
    <Modal opened onClose={onClose} title="Sequence Flow" size="lg">
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          Edit the selected path leaving a task or gateway. Use a Flowable expression like{" "}
          <Code>{"${needsApproval}"}</Code> or <Code>{"${riskLevel == 'high'}"}</Code> to route
          decisions based on process variables.
        </Text>

        <Group gap="xs" wrap="wrap">
          <Code>{editor.id}</Code>
          <Code>{editor.type}</Code>
        </Group>

        <TextInput
          label="Flow Name"
          value={editor.name}
          onChange={(e) => onChange({ ...editor, name: e.currentTarget.value })}
        />

        {editor.sourceType === "bpmn:ParallelGateway" ? (
          <Text size="xs" c="dimmed">
            This flow leaves a parallel gateway. Conditions are ignored on parallel-gateway
            outflows &mdash; every outgoing path always fires, so there&apos;s nothing to gate.
          </Text>
        ) : (
          <Textarea
            label="Condition Expression"
            description="Leave the condition blank for an unconditional path. For exclusive and inclusive gateways, put the condition on the outgoing branch itself, not on the gateway node."
            minRows={6}
            autosize
            spellCheck={false}
            value={editor.conditionExpression}
            onChange={(e) =>
              onChange({ ...editor, conditionExpression: e.currentTarget.value })
            }
          />
        )}

        <Group justify="flex-end" gap="xs">
          <Button variant="default" onClick={onClose}>
            Close
          </Button>
          <Button onClick={onApply} disabled={disabled}>
            Apply
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function GatewayModal({
  editor,
  onChange,
  onClose,
  onApply,
  disabled
}: {
  editor: GatewayEditor;
  onChange: (next: GatewayEditor) => void;
  onClose: () => void;
  onApply: () => void;
  disabled: boolean;
}) {
  const heading = editor.type === "bpmn:InclusiveGateway" ? "Inclusive Gateway" : "Exclusive Gateway";
  return (
    <Modal opened onClose={onClose} title={heading} size="lg">
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          Conditions live on the outgoing flows themselves &mdash; click an outgoing arrow to edit
          them. The default flow runs when no other condition matches.
        </Text>

        <Group gap="xs" wrap="wrap">
          <Code>{editor.id}</Code>
          <Code>{editor.type}</Code>
        </Group>

        <TextInput
          label="Name"
          value={editor.name}
          onChange={(e) => onChange({ ...editor, name: e.currentTarget.value })}
        />

        <Select
          label="Default Outgoing Flow"
          value={editor.defaultFlowId || null}
          onChange={(v) => onChange({ ...editor, defaultFlowId: v ?? "" })}
          disabled={editor.outgoingFlows.length === 0}
          clearable
          placeholder="(none)"
          data={editor.outgoingFlows.map((flow) => ({
            value: flow.id,
            label: flow.name ? `${flow.name} (${flow.id})` : flow.id
          }))}
          description={
            editor.outgoingFlows.length === 0
              ? "This gateway has no outgoing flows yet. Draw at least one outgoing arrow before picking a default."
              : "The default flow fires only when none of the other outgoing flows have a matching condition. Leave it as \"(none)\" if every path is conditional."
          }
        />

        <Group justify="flex-end" gap="xs">
          <Button variant="default" onClick={onClose}>
            Close
          </Button>
          <Button onClick={onApply} disabled={disabled}>
            Apply
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function UserTaskModal({
  editor,
  onChange,
  onClose,
  onApply,
  disabled
}: {
  editor: UserTaskEditor;
  onChange: (next: UserTaskEditor) => void;
  onClose: () => void;
  onApply: () => void;
  disabled: boolean;
}) {
  const { data: users = [] } = useUsers();
  const directory = useUserDirectory();
  const { data: forms = [] } = useForms();
  const sortedForms = useMemo(
    () => [...forms].sort((a, b) => a.name.localeCompare(b.name)),
    [forms]
  );
  const formNeedsPick =
    editor.userFormMode === "modal" || editor.userFormMode === "page";
  const userFormError =
    formNeedsPick && !editor.userFormShortCode
      ? "Select a form, or change the User Form mode to Simple Complete."
      : null;

  const sortedUsers = useMemo(
    () =>
      [...users].sort((a, b) => {
        const an = userDisplayName(a) ?? a.username;
        const bn = userDisplayName(b) ?? b.username;
        return an.localeCompare(bn);
      }),
    [users]
  );

  const assigneeName = (() => {
    if (!editor.assigneeUserId) return null;
    const user = directory.get(editor.assigneeUserId);
    return userDisplayName(user) ?? editor.assigneeUserId;
  })();

  return (
    <Modal opened onClose={onClose} title="User Task" size="xl">
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          Edit the selected user task. Pick assignees from the directory or supply a Flowable
          expression like <Code>{"${initiator}"}</Code> that resolves at runtime.
        </Text>

        <Group gap="xs" wrap="wrap">
          <Code>{editor.id}</Code>
          <Code>{editor.type}</Code>
        </Group>

        <label className="workflow-field">
          <span>Task Name</span>
          <input
            className="form-control"
            value={editor.name}
            onChange={(e) => onChange({ ...editor, name: e.target.value })}
          />
        </label>

        <fieldset className="workflow-field">
          <legend>Assignee</legend>
          <div className="form-check form-check-inline">
            <input
              type="radio"
              className="form-check-input"
              id="userTask-assignee-mode-picker"
              checked={editor.assigneeMode === "picker"}
              onChange={() => onChange({ ...editor, assigneeMode: "picker" })}
            />
            <label className="form-check-label" htmlFor="userTask-assignee-mode-picker">
              Pick user
            </label>
          </div>
          <div className="form-check form-check-inline">
            <input
              type="radio"
              className="form-check-input"
              id="userTask-assignee-mode-expression"
              checked={editor.assigneeMode === "expression"}
              onChange={() => onChange({ ...editor, assigneeMode: "expression" })}
            />
            <label className="form-check-label" htmlFor="userTask-assignee-mode-expression">
              Expression
            </label>
          </div>

          {editor.assigneeMode === "picker" ? (
            <div className="d-flex flex-column gap-2 mt-2">
              {editor.assigneeUserId ? (
                <div className="d-flex align-items-center gap-2">
                  <span className="badge bg-secondary">{assigneeName}</span>
                  <button
                    type="button"
                    className="btn btn-sm btn-outline-secondary"
                    onClick={() => onChange({ ...editor, assigneeUserId: "" })}
                  >
                    Clear
                  </button>
                </div>
              ) : (
                <span className="text-body text-opacity-50 small">No assignee selected</span>
              )}
              <select
                className="form-select"
                value=""
                onChange={(e) => {
                  const id = e.target.value;
                  if (id) onChange({ ...editor, assigneeUserId: id });
                }}
              >
                <option value="">Select user…</option>
                {sortedUsers.map((u) => (
                  <option key={u.userId} value={u.userId}>
                    {userDisplayName(u) ?? u.username}
                  </option>
                ))}
              </select>
            </div>
          ) : (
            <input
              className="form-control mt-2"
              placeholder="${initiator}"
              value={editor.assigneeExpression}
              onChange={(e) => onChange({ ...editor, assigneeExpression: e.target.value })}
            />
          )}
        </fieldset>

        <fieldset className="workflow-field">
          <legend>Candidate Users</legend>
          <div className="form-check form-check-inline">
            <input
              type="radio"
              className="form-check-input"
              id="userTask-candidate-mode-picker"
              checked={editor.candidateUsersMode === "picker"}
              onChange={() => onChange({ ...editor, candidateUsersMode: "picker" })}
            />
            <label className="form-check-label" htmlFor="userTask-candidate-mode-picker">
              Pick users
            </label>
          </div>
          <div className="form-check form-check-inline">
            <input
              type="radio"
              className="form-check-input"
              id="userTask-candidate-mode-expression"
              checked={editor.candidateUsersMode === "expression"}
              onChange={() => onChange({ ...editor, candidateUsersMode: "expression" })}
            />
            <label className="form-check-label" htmlFor="userTask-candidate-mode-expression">
              Expression
            </label>
          </div>

          {editor.candidateUsersMode === "picker" ? (
            <div className="mt-2">
              <AssigneePicker
                value={editor.candidateUserIds}
                onChange={(ids) => onChange({ ...editor, candidateUserIds: ids })}
              />
            </div>
          ) : (
            <textarea
              className="form-control mt-2"
              rows={2}
              placeholder="${candidateUsers}"
              value={editor.candidateUsersExpression}
              onChange={(e) =>
                onChange({ ...editor, candidateUsersExpression: e.target.value })
              }
            />
          )}
        </fieldset>

        <label className="workflow-field">
          <span>Candidate Groups</span>
          <textarea
            className="form-control"
            rows={2}
            placeholder="reviewers, approvers"
            value={editor.candidateGroupsRaw}
            onChange={(e) => onChange({ ...editor, candidateGroupsRaw: e.target.value })}
          />
          <p className="workflow-modal-note">
            Comma-separated group keys, or a single Flowable expression like{" "}
            <code>${"{currentRecord.groups}"}</code>. There is no group directory yet, so groups are
            free text.
          </p>
        </label>

        <fieldset className="workflow-field">
          <legend>Due Date</legend>
          <div className="form-check">
            <input
              type="radio"
              className="form-check-input"
              id="userTask-dueDate-mode-none"
              checked={editor.dueDateMode === "none"}
              onChange={() => onChange({ ...editor, dueDateMode: "none" })}
            />
            <label className="form-check-label" htmlFor="userTask-dueDate-mode-none">
              No due date
            </label>
          </div>
          <div className="form-check">
            <input
              type="radio"
              className="form-check-input"
              id="userTask-dueDate-mode-activation"
              checked={editor.dueDateMode === "afterActivation"}
              onChange={() => onChange({ ...editor, dueDateMode: "afterActivation" })}
            />
            <label className="form-check-label" htmlFor="userTask-dueDate-mode-activation">
              Days after task activation
            </label>
          </div>
          <div className="form-check">
            <input
              type="radio"
              className="form-check-input"
              id="userTask-dueDate-mode-start"
              checked={editor.dueDateMode === "afterProcessStart"}
              onChange={() => onChange({ ...editor, dueDateMode: "afterProcessStart" })}
            />
            <label className="form-check-label" htmlFor="userTask-dueDate-mode-start">
              Days after process start
            </label>
          </div>
          <div className="form-check">
            <input
              type="radio"
              className="form-check-input"
              id="userTask-dueDate-mode-expression"
              checked={editor.dueDateMode === "expression"}
              onChange={() => onChange({ ...editor, dueDateMode: "expression" })}
            />
            <label className="form-check-label" htmlFor="userTask-dueDate-mode-expression">
              Expression
            </label>
          </div>

          {(editor.dueDateMode === "afterActivation" ||
            editor.dueDateMode === "afterProcessStart") && (
            <div className="mt-2">
              <input
                className="form-control"
                placeholder="3 or ${slaDays}"
                value={editor.dueDateDays}
                onChange={(e) => onChange({ ...editor, dueDateDays: e.target.value })}
              />
              <p className="workflow-modal-note">
                Enter a whole number of days, or a Flowable expression like{" "}
                <code>${"{slaDays}"}</code> set by an upstream script task. The due date is
                resolved when the task is created.
              </p>
            </div>
          )}

          {editor.dueDateMode === "expression" && (
            <div className="mt-2">
              <textarea
                className="form-control"
                rows={2}
                placeholder="${customDueDate}"
                value={editor.dueDateExpression}
                onChange={(e) => onChange({ ...editor, dueDateExpression: e.target.value })}
              />
              <p className="workflow-modal-note">
                Any value Flowable accepts in <code>flowable:dueDate</code>: an ISO duration like{" "}
                <code>P3D</code>, an absolute timestamp, or an expression resolving to either. Set
                the variable from a script task to drive due dates dynamically.
              </p>
            </div>
          )}
        </fieldset>

        <fieldset className="workflow-field">
          <legend>Behaviour</legend>
          <p className="workflow-modal-note mb-2">
            <strong>Default Behavior</strong> shows a built-in modal — a single
            "Complete Task" button when this task flows into a normal node, or one
            button per outgoing path when it flows directly into an exclusive
            gateway. <strong>Form</strong> renders a custom form instead.
          </p>
          <div className="form-check">
            <input
              type="radio"
              id="userTask-behaviour-default"
              name="userTask-behaviour"
              className="form-check-input"
              checked={editor.userFormMode === "simple"}
              onChange={() =>
                onChange({ ...editor, userFormMode: "simple", userFormShortCode: "" })
              }
            />
            <label htmlFor="userTask-behaviour-default" className="form-check-label">
              Default Behavior
            </label>
          </div>
          <div className="form-check">
            <input
              type="radio"
              id="userTask-behaviour-form"
              name="userTask-behaviour"
              className="form-check-input"
              checked={formNeedsPick}
              onChange={() =>
                onChange({
                  ...editor,
                  userFormMode:
                    editor.userFormMode === "modal" || editor.userFormMode === "page"
                      ? editor.userFormMode
                      : "modal"
                })
              }
            />
            <label htmlFor="userTask-behaviour-form" className="form-check-label">
              Form
            </label>
          </div>

          {formNeedsPick && (
            <div className="mt-3">
              <label className="form-label">Render mode</label>
              <select
                className="form-select"
                value={editor.userFormMode}
                onChange={(e) =>
                  onChange({ ...editor, userFormMode: e.target.value as UserFormMode })
                }
              >
                <option value="modal">Form Modal — render the form in a modal</option>
                <option value="page">
                  Form Page — navigate to /workflow-tasks/&lt;taskId&gt;/form
                </option>
              </select>

              <label className="form-label mt-3">Form</label>
              <select
                className={`form-select${userFormError ? " is-invalid" : ""}`}
                value={editor.userFormShortCode}
                onChange={(e) =>
                  onChange({ ...editor, userFormShortCode: e.target.value })
                }
              >
                <option value="">Select form…</option>
                {sortedForms.map((f) => (
                  <option key={f.id} value={f.shortCode}>
                    {f.name} ({f.shortCode})
                    {f.publishedVersionNumber === null ? " — unpublished" : ""}
                  </option>
                ))}
              </select>
              {userFormError && <div className="invalid-feedback">{userFormError}</div>}
              <p className="workflow-modal-note mt-1">
                The form's process variables are passed in as <code>data</code>; submitting calls{" "}
                <code>POST /api/tasks/&lt;taskId&gt;/complete</code> with the payload as Flowable
                variables.
              </p>
            </div>
          )}
        </fieldset>

        <Group justify="flex-end" gap="xs">
          <Button variant="default" onClick={onClose}>
            Close
          </Button>
          <Button onClick={onApply} disabled={disabled || Boolean(userFormError)}>
            Apply
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

type BpmnTypeGroup = {
  category: string;
  items: string[];
};

const SUPPORTED_BPMN_TYPES: BpmnTypeGroup[] = [
  {
    category: "Events",
    items: [
      "Start Event (None)",
      "Signal Start Event",
      "Timer Start Event",
      "Intermediate Catch (Timer)",
      "End Event (None)",
      "End Event (Terminate)"
    ]
  },
  {
    category: "Tasks",
    items: ["Task (Generic)", "User Task", "Script Task", "Service Task (Behavior)"]
  },
  {
    category: "Gateways",
    items: ["Exclusive Gateway (XOR)", "Inclusive Gateway (OR)", "Parallel Gateway (AND)"]
  },
  {
    category: "Flows",
    items: ["Sequence Flow"]
  }
];

const COMING_SOON_BPMN_TYPES: BpmnTypeGroup[] = [
  {
    category: "Start Events",
    items: [
      "Message Start Event",
      "Conditional Start Event",
      "Error Start Event",
      "Escalation Start Event",
      "Compensation Start Event"
    ]
  },
  {
    category: "Intermediate Events",
    items: [
      "Intermediate Throw (None)",
      "Intermediate Throw (Message)",
      "Intermediate Throw (Signal)",
      "Intermediate Throw (Escalation)",
      "Intermediate Throw (Link)",
      "Intermediate Throw (Compensation)",
      "Intermediate Catch (Message)",
      "Intermediate Catch (Signal)",
      "Intermediate Catch (Conditional)",
      "Intermediate Catch (Link)"
    ]
  },
  {
    category: "Boundary Events",
    items: [
      "Message Boundary",
      "Timer Boundary",
      "Signal Boundary",
      "Conditional Boundary",
      "Error Boundary",
      "Escalation Boundary",
      "Cancel Boundary",
      "Compensation Boundary"
    ]
  },
  {
    category: "End Events",
    items: [
      "Message End",
      "Signal End",
      "Error End",
      "Escalation End",
      "Cancel End",
      "Compensation End"
    ]
  },
  {
    category: "Tasks",
    items: [
      "Send Task",
      "Receive Task",
      "Manual Task",
      "Business Rule Task",
      "Call Activity"
    ]
  },
  {
    category: "Sub-Processes",
    items: ["Sub-Process (Embedded)", "Event Sub-Process", "Transaction", "Ad-Hoc Sub-Process"]
  },
  {
    category: "Gateways",
    items: ["Event-Based Gateway", "Complex Gateway"]
  },
  {
    category: "Activity Markers",
    items: ["Loop Marker", "Multi-Instance (Parallel)", "Multi-Instance (Sequential)", "Compensation Marker"]
  },
  {
    category: "Collaboration",
    items: ["Pool / Participant", "Lane", "Message Flow"]
  },
  {
    category: "Data",
    items: ["Data Object Reference", "Data Store Reference", "Data Input", "Data Output"]
  },
  {
    category: "Artifacts",
    items: ["Text Annotation", "Group", "Association"]
  }
];

function BpmnTypesModal({ onClose }: { onClose: () => void }) {
  const supportedCount = SUPPORTED_BPMN_TYPES.reduce((n, g) => n + g.items.length, 0);
  const comingSoonCount = COMING_SOON_BPMN_TYPES.reduce((n, g) => n + g.items.length, 0);

  return (
    <Modal opened onClose={onClose} title="Supported BPMN Types" size="xl">
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          The full set of BPMN 2.0 node types the AutoNate workflow studio can model and execute
          today, alongside what is on the roadmap.
        </Text>

        <div className="workflow-bpmn-types-grid">
          <section className="workflow-bpmn-types-column workflow-bpmn-types-column-supported">
            <header className="workflow-bpmn-types-column-header">
              <h3>
                <i className="fa fa-circle-check" aria-hidden="true"></i>
                Supported
              </h3>
              <span className="workflow-bpmn-types-count">{supportedCount}</span>
            </header>
            {SUPPORTED_BPMN_TYPES.map((group) => (
              <div key={group.category} className="workflow-bpmn-types-group">
                <h4>{group.category}</h4>
                <ul>
                  {group.items.map((item) => (
                    <li key={item}>{item}</li>
                  ))}
                </ul>
              </div>
            ))}
          </section>

          <section className="workflow-bpmn-types-column workflow-bpmn-types-column-coming">
            <header className="workflow-bpmn-types-column-header">
              <h3>
                <i className="fa fa-hourglass-half" aria-hidden="true"></i>
                Coming Soon
              </h3>
              <span className="workflow-bpmn-types-count">{comingSoonCount}</span>
            </header>
            {COMING_SOON_BPMN_TYPES.map((group) => (
              <div key={group.category} className="workflow-bpmn-types-group">
                <h4>{group.category}</h4>
                <ul>
                  {group.items.map((item) => (
                    <li key={item}>{item}</li>
                  ))}
                </ul>
              </div>
            ))}
          </section>
        </div>

        <Group justify="flex-end">
          <Button onClick={onClose}>Close</Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function formatTimestamp(iso: string | null | undefined): string {
  if (!iso) return "Not available";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { message?: string } } }).response;
    return response?.data?.message ?? error.message;
  }
  return String(error);
}

// Minimal BPMN starter diagram that the server-side prepare endpoint will patch up with the
// correct process key and name via WorkflowBpmnXml.ApplyProcessMetadata.
const STARTER_DIAGRAM_PLACEHOLDER = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                  xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                  id="Definitions_1"
                  targetNamespace="http://autonate.dev/workflows">
  <bpmn:process id="workflow" name="Workflow" isExecutable="true">
    <bpmn:startEvent id="StartEvent_1" />
  </bpmn:process>
  <bpmndi:BPMNDiagram id="BPMNDiagram_1">
    <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="workflow">
      <bpmndi:BPMNShape id="_BPMNShape_StartEvent_1" bpmnElement="StartEvent_1">
        <dc:Bounds x="173" y="102" width="36" height="36" />
      </bpmndi:BPMNShape>
    </bpmndi:BPMNPlane>
  </bpmndi:BPMNDiagram>
</bpmn:definitions>`;
