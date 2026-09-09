import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import CodeMirror from "@uiw/react-codemirror";
import { javascript } from "@codemirror/lang-javascript";
import { python } from "@codemirror/lang-python";
import {
  ActionIcon,
  Alert,
  Box,
  Button,
  Checkbox,
  Code,
  Divider,
  Group,
  List,
  Modal,
  Radio,
  Select,
  Stack,
  Switch,
  Text,
  TextInput,
  Textarea,
  Title,
  Tooltip
} from "@mantine/core";
import { useBpmnModeler } from "@/hooks/useBpmnModeler";
import { permissionKey, usePermissionChecks } from "@/hooks/usePermissionChecks";
import { ScriptTestRunPanel } from "./ScriptTestRunPanel";
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
  ANNOTATION_ELEMENTS,
  COMING_SOON_ELEMENTS,
  EXECUTABLE_SUPPORTED_ELEMENTS,
  FLOWABLE_VERSION,
  groupByCategory,
  type BpmnSupportGroup
} from "@/lib/bpmn/support";
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
  // #153: "" (default — the preceding user task's assignee), "system", or
  // "workflowAuthor".
  runAs: string;
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

// #158. One editor for all three placements a conditional event can occupy —
// intermediate catch, boundary, and the event-subprocess start (#162) — because the
// thing being edited is the same condition in each. `interrupting` is meaningful
// only on a boundary event and is null elsewhere.
// #157. A timer boundary carries all three timer kinds; the start-event editor
// knows only cycle and the intermediate-catch editor only duration and date.
type TimerBoundaryEventEditor = {
  id: string;
  name: string;
  mode: "duration" | "date" | "cycle";
  duration: string;
  date: string;
  cycle: string;
  interrupting: boolean;
  attachedTo: string | null;
};

type ConditionalEventEditor = {
  id: string;
  type: string;
  name: string;
  conditionExpression: string;
  interrupting: boolean | null;
};

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
  // #168. Serialises to flowable:async — see the switch in the service-task
  // panel for what it buys the author.
  retryPoint: boolean;
};

type MessageElementEditor = {
  id: string;
  type: string;
  name: string;
  // "start" | "catch" | "send" — decides which fields are meaningful.
  direction: string;
  correlationKey: string;
  targetProcessKey: string;
  messageName: string;
  editableMessageName: boolean;
};

type CodedEventEditor = {
  id: string;
  type: string;
  name: string;
  // "error" | "escalation"
  kind: string;
  code: string;
  // null when the element is not a boundary event, or is an error boundary —
  // BPMN gives an error boundary no choice, it always interrupts.
  interrupting: boolean | null;
};

type SignalEventEditor = {
  id: string;
  type: string;
  name: string;
  signalName: string;
  // "instance" | "global"
  scope: string;
  interrupting: boolean | null;
};

type VariableMapping = { source: string; target: string };

type CallActivityEditor = {
  id: string;
  type: string;
  name: string;
  calledElement: string;
  inputs: VariableMapping[];
  outputs: VariableMapping[];
};

// #159/#163/#166. One editor for the three element-data shapes, discriminated by
// `kind`. Three separate editors would each have to be cleared by every branch —
// the pattern clearEditors() exists to end.
type ElementDataEditor =
  | { kind: "adhoc"; id: string; type: string; name: string; completionCondition: string; sequential: boolean }
  | { kind: "dataObject"; id: string; type: string; name: string; dataType: string }
  | {
      kind: "multiInstance";
      id: string;
      type: string;
      name: string;
      collection: string;
      elementVariable: string;
      completionCondition: string;
      sequential: boolean;
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
  runAs?: string | null;
  script?: string | null;
  resultVariable?: string | null;
  conditionExpression?: string | null;
  adhocCompletionCondition?: string | null;
  adhocOrdering?: string | null;
  dataObjectType?: string | null;
  multiInstanceCollection?: string | null;
  multiInstanceElementVariable?: string | null;
  multiInstanceCompletionCondition?: string | null;
  multiInstanceSequential?: boolean | null;
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
  // #158. Present only on elements carrying a conditionalEventDefinition — the
  // key's ABSENCE is what keeps timer intermediate catch events out of the
  // conditional editor, since onRequestConfigure routes on $type plus key presence.
  cancelActivity?: boolean | null;
  // #157. Present only on a boundary event carrying a timer definition — their
  // ABSENCE is what keeps conditional boundary events out of the timer editor,
  // since both kinds carry cancelActivity.
  boundaryTimerDuration?: string | null;
  boundaryTimerDate?: string | null;
  boundaryTimerCycle?: string | null;
  attachedTo?: string | null;
  // #168. Present only on service tasks the studio recognises, like
  // serviceTaskKind and behaviorKey above.
  retryPoint?: boolean | null;
  // #112. Present only on message-carrying elements, receive tasks and send
  // tasks. Their absence is what keeps everything else out of the message editor.
  messageDirection?: string | null;
  messageCorrelationKey?: string | null;
  messageTargetProcessKey?: string | null;
  messageName?: string | null;
  // #114. Present only on error/escalation events.
  codedEventKind?: string | null;
  codedEventCode?: string | null;
  codedEventInterrupting?: boolean | null;
  // #113. Present only on a call activity.
  calledElement?: string | null;
  callInputs?: { source: string; target: string }[] | null;
  callOutputs?: { source: string; target: string }[] | null;
  // #156. Present only on signal events.
  signalEventName?: string | null;
  signalEventScope?: string | null;
  signalEventIsNew?: boolean | null;
  signalEventInterrupting?: boolean | null;
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
  const [messageEditor, setMessageEditor] = useState<MessageElementEditor | null>(null);
  const [codedEventEditor, setCodedEventEditor] = useState<CodedEventEditor | null>(null);
  const [callActivityEditor, setCallActivityEditor] = useState<CallActivityEditor | null>(null);
  const [signalEditor, setSignalEditor] = useState<SignalEventEditor | null>(null);
  const [gatewayEditor, setGatewayEditor] = useState<GatewayEditor | null>(null);
  const [genericEditor, setGenericEditor] = useState<GenericElementEditor | null>(null);
  const [elementDataEditor, setElementDataEditor] = useState<ElementDataEditor | null>(null);
  const [conditionalEventEditor, setConditionalEventEditor] =
    useState<ConditionalEventEditor | null>(null);
  const [timerBoundaryEditor, setTimerBoundaryEditor] =
    useState<TimerBoundaryEventEditor | null>(null);
  // #167. A diagram opened with manual or generic tasks in it has been changed
  // without the author doing anything, so this is a condition belonging to the page
  // rather than feedback on an action — an in-page Alert, not a toast (CLAUDE.md).
  const [convertedTasks, setConvertedTasks] = useState<
    Array<{ id: string; name: string | null; was: string }>
  >([]);

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
    // eslint-disable-next-line react-hooks/exhaustive-deps -- one-shot initial selection; selectWorkflow would re-run it and fight the user's choice
  }, [workflowsLoaded, workflows]);

  const onDiagramChanged = useCallback(() => {
    setDirty(true);
  }, []);

  // Every element editor this component owns. Adding one means adding it
  // here and nowhere else.
  const clearEditors = useCallback(() => {
    setCallActivityEditor(null);
    setCodedEventEditor(null);
    setConditionalEventEditor(null);
    setGatewayEditor(null);
    setGenericEditor(null);
    setMessageEditor(null);
    setScriptTaskEditor(null);
    setSequenceFlowEditor(null);
    setServiceTaskEditor(null);
    setSignalEditor(null);
    setSignalStartEditor(null);
    setTimerBoundaryEditor(null);
    setTimerIntermediateEditor(null);
    setTimerStartEditor(null);
    setUserTaskEditor(null);
  }, []);

  const onRequestConfigure = useCallback((raw: unknown) => {
    // #159/#163/#166. One call, at the top, instead of every branch clearing
    // every other editor. That pattern was 148 lines inside this callback and
    // grew quadratically: each editor added had to be cleared in every branch,
    // and the one branch that forgot left two modals open at once. Clearing
    // first and letting the matching branch set its own is the same behaviour
    // with the failure mode removed.
    clearEditors();
    const selection = raw as ElementSelection;
    // #157. Before the conditional branch: both boundary shapes carry
    // cancelActivity, and only the timer one carries the boundaryTimer* keys, so
    // presence of those is what tells them apart.
    const isTimerBoundary =
      !!selection &&
      selection.type === "bpmn:BoundaryEvent" &&
      ("boundaryTimerDuration" in selection ||
        "boundaryTimerDate" in selection ||
        "boundaryTimerCycle" in selection);
    if (isTimerBoundary && selection) {
      const duration = (selection.boundaryTimerDuration ?? "").trim();
      const date = (selection.boundaryTimerDate ?? "").trim();
      const cycle = (selection.boundaryTimerCycle ?? "").trim();
      setTimerBoundaryEditor({
        id: selection.id,
        name: selection.name ?? "",
        // Default to duration for a freshly dropped event, which is the common case.
        mode: cycle ? "cycle" : date ? "date" : "duration",
        duration,
        date,
        cycle,
        interrupting: selection.cancelActivity !== false,
        attachedTo: selection.attachedTo ?? null
      });
      return;
    }
    // #158. First branch, because a conditional BOUNDARY event is the one shape no
    // other branch below claims — and routing on $type alone would send every
    // intermediate catch event here, timer ones included. describeConditionalEvent
    // omits these keys entirely unless a conditionalEventDefinition is present, so
    // presence is the discriminator.
    const isConditionalEvent =
      !!selection &&
      ("conditionExpression" in selection && "cancelActivity" in selection) &&
      (selection.type === "bpmn:IntermediateCatchEvent" ||
        selection.type === "bpmn:BoundaryEvent" ||
        selection.type === "bpmn:StartEvent");
    if (isConditionalEvent && selection) {
      setConditionalEventEditor({
        id: selection.id,
        type: selection.type,
        name: selection.name ?? "",
        conditionExpression: selection.conditionExpression ?? "",
        interrupting:
          selection.type === "bpmn:BoundaryEvent" ? selection.cancelActivity !== false : null
      });
      return;
    }
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
          ? "Auton8 doesn't recognize this cron expression. The picker is locked — edit the raw cron below or clear it to start fresh."
          : null
      });
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
      return;
    }
    // #113. A call activity carries its own configuration and would otherwise
    // reach the generic editor, where the author could only rename it.
    if (selection && selection.type === "bpmn:CallActivity") {
      setCallActivityEditor({
        id: selection.id,
        type: selection.type,
        name: selection.name ?? "",
        calledElement: selection.calledElement ?? "",
        inputs: selection.callInputs ?? [],
        outputs: selection.callOutputs ?? []
      });
      return;
    }

    // #156. Signal events carry their own definition type; without this they
    // reach the generic editor with nowhere to put a name or a scope.
    if (selection && typeof selection.signalEventName === "string") {
      setSignalEditor({
        id: selection.id,
        type: selection.type,
        name: selection.name ?? "",
        signalName: selection.signalEventName,
        // A signal that already exists keeps the scope it has — opening one must
        // not silently propose changing what a deployed process does. Only a
        // NEW signal defaults to instance.
        scope: selection.signalEventIsNew ? "instance" : (selection.signalEventScope ?? "global"),
        interrupting:
          typeof selection.signalEventInterrupting === "boolean"
            ? selection.signalEventInterrupting
            : null
      });
      return;
    }

    // #114. Error and escalation events, routed before the message branch: they
    // carry their own definition type and would otherwise reach the generic
    // editor with nowhere to put a code.
    if (selection && typeof selection.codedEventKind === "string") {
      setCodedEventEditor({
        id: selection.id,
        type: selection.type,
        name: selection.name ?? "",
        kind: selection.codedEventKind,
        code: selection.codedEventCode ?? "",
        interrupting:
          typeof selection.codedEventInterrupting === "boolean"
            ? selection.codedEventInterrupting
            : null
      });
      return;
    }

    // #112. Before the service-task branch — a send task is a message element
    // first, and a receive task would otherwise land in the generic editor with
    // nowhere to put a correlation key.
    if (selection && typeof selection.messageDirection === "string") {
      setMessageEditor({
        id: selection.id,
        type: selection.type,
        name: selection.name ?? "",
        direction: selection.messageDirection,
        correlationKey: selection.messageCorrelationKey ?? "",
        targetProcessKey: selection.messageTargetProcessKey ?? "",
        messageName: selection.messageName ?? "",
        // Only a send task names its own message; everywhere else the name comes
        // from the <bpmn:message> the diagram declares, and editing it here would
        // silently diverge from it.
        editableMessageName: selection.type === "bpmn:SendTask"
      });
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
        behaviorKey: selection.behaviorKey ?? "",
        retryPoint: selection.retryPoint === true
      });
      return;
    }
    // #218. A complex gateway routes on an author's script, and the fields it
    // needs are the script panel's fields. Routed here rather than given a
    // twentieth editor of its own — every editor added to this component has to
    // be cleared by every other branch, and that list is already the most
    // fragile thing in the file.
    // #159/#163/#166. Before the task branches, because a multi-instance marker
    // sits on an activity that also matches one of them — presence of the
    // multiInstance* keys is the discriminator, per load-bearing fact 3.
    if (selection && "multiInstanceCollection" in selection) {
      setElementDataEditor({
        kind: "multiInstance",
        id: selection.id,
        type: selection.type,
        name: selection.name ?? "",
        collection: selection.multiInstanceCollection ?? "",
        elementVariable: selection.multiInstanceElementVariable ?? "",
        completionCondition: selection.multiInstanceCompletionCondition ?? "",
        sequential: selection.multiInstanceSequential === true
      });
      return;
    }

    if (selection && "adhocCompletionCondition" in selection) {
      setElementDataEditor({
        kind: "adhoc",
        id: selection.id,
        type: selection.type,
        name: selection.name ?? "",
        completionCondition: selection.adhocCompletionCondition ?? "",
        sequential: selection.adhocOrdering === "Sequential"
      });
      return;
    }

    if (selection && "dataObjectType" in selection) {
      setElementDataEditor({
        kind: "dataObject",
        id: selection.id,
        type: selection.type,
        name: selection.name ?? "",
        dataType: selection.dataObjectType ?? ""
      });
      return;
    }

    if (selection && (selection.type === "bpmn:ScriptTask" || selection.type === "bpmn:ComplexGateway")) {
      setScriptTaskEditor({
        id: selection.id,
        type: selection.type,
        name: selection.name ?? "",
        // Read from the diagram rather than assumed (#154). A task saved as
        // Python must not silently reopen as JavaScript and then be saved back
        // that way.
        scriptFormat: selection.scriptFormat === "python" ? "python" : "javascript",
        runAs: selection.runAs ?? "",
        script: selection.script ?? "",
        resultVariable: selection.resultVariable ?? ""
      });
    } else if (selection && selection.type === "bpmn:SequenceFlow") {
      setSequenceFlowEditor({
        id: selection.id,
        type: selection.type,
        name: selection.name ?? "",
        conditionExpression: selection.conditionExpression ?? "",
        sourceType: selection.sourceType ?? null
      });
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
    } else if (selection) {
      setGenericEditor({
        id: selection.id,
        type: selection.type,
        name: selection.name ?? ""
      });
    } else {
    }
  }, [clearEditors]);

  const onTasksConverted = useCallback(
    (converted: Array<{ id: string; name: string | null; was: string }>) => {
      // Accumulate rather than replace: a drop after an import should not erase the
      // notice explaining what the import already changed.
      setConvertedTasks((previous) => [...previous, ...converted]);
    },
    []
  );

  const callbacks = useMemo(
    () => ({
      NotifyDiagramChanged: onDiagramChanged,
      RequestConfigureElement: onRequestConfigure,
      NotifyTasksConverted: onTasksConverted
    }),
    [onDiagramChanged, onRequestConfigure, onTasksConverted]
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
    setConditionalEventEditor(null);
    setTimerBoundaryEditor(null);
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

  // #153: may this author declare a script task to run as the system?
  //
  // Drives whether the option is offered. It is not the gate — the server
  // re-checks on publish, because a control that is merely hidden is not a
  // permission. Defaults to false while the check is in flight, so the option
  // does not flicker into existence for someone who cannot use it.
  const elevateChecks = useMemo(
    () =>
      currentModel
        ? [{ kind: "workflowmodel", action: "elevatescript", id: currentModel.id }]
        : [],
    [currentModel]
  );
  const { data: elevatePermissions } = usePermissionChecks(elevateChecks);
  const canElevateScript =
    currentModel !== null &&
    (elevatePermissions?.get(
      permissionKey({ kind: "workflowmodel", action: "elevatescript", id: currentModel.id })
    ) ??
      false);

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

  const applyTimerBoundary = () =>
    runBusy("applying timer boundary event changes", async () => {
      if (!handle || !timerBoundaryEditor) {
        throw new Error("Select a timer boundary event before applying changes.");
      }

      const { mode, duration, date, cycle } = timerBoundaryEditor;
      const value = (mode === "duration" ? duration : mode === "date" ? date : cycle).trim();
      if (!value) {
        // Refused here as well as at publish: a timer with no time deploys, never
        // fires, and the activity it guards waits forever.
        throw new Error(
          mode === "duration"
            ? "Enter a duration (e.g. PT15M) before applying."
            : mode === "date"
              ? "Enter a date/time (e.g. 2026-12-31T09:00:00) before applying."
              : "Enter a repeating cycle (e.g. R3/PT1H) before applying."
        );
      }

      await workflow.updateTimerBoundaryEventProperties(handle, {
        id: timerBoundaryEditor.id,
        name: timerBoundaryEditor.name,
        boundaryTimerDuration: mode === "duration" ? value : "",
        boundaryTimerDate: mode === "date" ? value : "",
        boundaryTimerCycle: mode === "cycle" ? value : "",
        cancelActivity: timerBoundaryEditor.interrupting
      });
      setTimerBoundaryEditor(null);
    });

  const applyConditionalEvent = () =>
    runBusy("applying conditional event changes", async () => {
      if (!handle || !conditionalEventEditor) {
        throw new Error("Select a conditional event before applying changes.");
      }

      const expression = conditionalEventEditor.conditionExpression.trim();
      if (!expression) {
        // Refused here as well as at publish: an empty condition is written as a
        // condition element that never evaluates, so the process waits forever.
        throw new Error("Enter a condition (e.g. ${approved == true}) before applying.");
      }

      await workflow.updateConditionalEventProperties(handle, {
        id: conditionalEventEditor.id,
        name: conditionalEventEditor.name,
        conditionExpression: expression,
        cancelActivity:
          conditionalEventEditor.interrupting === null
            ? undefined
            : conditionalEventEditor.interrupting
      });
      setConditionalEventEditor(null);
    setTimerBoundaryEditor(null);
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
        behaviorKey,
        retryPoint: serviceTaskEditor.retryPoint
      });
      setServiceTaskEditor(null);
    });

  const applyMessageElement = () =>
    runBusy("applying message settings", async () => {
      if (!handle || !messageEditor) {
        throw new Error("Select a message element before applying changes.");
      }
      await workflow.updateMessageElementProperties(handle, {
        id: messageEditor.id,
        name: messageEditor.name,
        correlationKey: messageEditor.correlationKey,
        targetProcessKey: messageEditor.targetProcessKey,
        messageName: messageEditor.messageName
      });
      setMessageEditor(null);
    });

  const applyCodedEvent = () =>
    runBusy("applying event code", async () => {
      if (!handle || !codedEventEditor) {
        throw new Error("Select an error or escalation event before applying changes.");
      }
      await workflow.updateCodedEventProperties(handle, {
        id: codedEventEditor.id,
        name: codedEventEditor.name,
        kind: codedEventEditor.kind,
        code: codedEventEditor.code,
        interrupting: codedEventEditor.interrupting
      });
      setCodedEventEditor(null);
    });

  const applyCallActivity = () =>
    runBusy("applying call activity settings", async () => {
      if (!handle || !callActivityEditor) {
        throw new Error("Select a call activity before applying changes.");
      }
      if (!callActivityEditor.calledElement.trim()) {
        throw new Error("Pick which workflow this step should run.");
      }
      await workflow.updateCallActivityProperties(handle, {
        id: callActivityEditor.id,
        name: callActivityEditor.name,
        calledElement: callActivityEditor.calledElement,
        inputs: callActivityEditor.inputs,
        outputs: callActivityEditor.outputs
      });
      setCallActivityEditor(null);
    });

  const applySignalEvent = () =>
    runBusy("applying signal settings", async () => {
      if (!handle || !signalEditor) {
        throw new Error("Select a signal event before applying changes.");
      }
      if (!signalEditor.signalName.trim()) {
        throw new Error("Give the signal a name — that is what matches one end to the other.");
      }
      await workflow.updateSignalElementProperties(handle, {
        id: signalEditor.id,
        name: signalEditor.name,
        signalName: signalEditor.signalName,
        scope: signalEditor.scope,
        interrupting: signalEditor.interrupting
      });
      setSignalEditor(null);
    });

  const applyElementData = () =>
    runBusy("applying element changes", async () => {
      if (!handle || !elementDataEditor) {
        throw new Error("Select an element before applying changes.");
      }
      await workflow.updateElementDataProperties(handle, elementDataEditor);
      setElementDataEditor(null);
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
    setConditionalEventEditor(null);
    setTimerBoundaryEditor(null);
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
            Select a saved workflow model, edit it in the browser, save drafts to Auton8, publish
            to Flowable, and start new executions from the current model.
          </Text>
        </Stack>
        {/* #107: was "Supported BPMN Types", which the panel outgrew — it now also
            says what is coming, what the engine cannot run, and what never
            executes by design. The native `title` went with it; tooltips in this
            app are Mantine's, which screen readers announce. */}
        <Tooltip label="What the studio offers, and what Flowable will run">
          <Button
            variant="gradient"
            gradient={{ from: "#2680c2", to: "#0f609b", deg: 135 }}
            radius="xl"
            size="sm"
            onClick={() => setShowBpmnTypesModal(true)}
            leftSection={<i className="fa fa-sitemap" aria-hidden="true" />}
            rightSection={<i className="fa fa-arrow-right" aria-hidden="true" />}
          >
            BPMN element support
          </Button>
        </Tooltip>
      </Group>

      {error && (
        <Alert color="red" variant="light" mb="sm">
          {error}
        </Alert>
      )}
      {convertedTasks.length > 0 && (
        <Alert
          color="blue"
          variant="light"
          mb="sm"
          withCloseButton
          onClose={() => setConvertedTasks([])}
          title={
            convertedTasks.length === 1
              ? "Converted to a user task"
              : `Converted ${convertedTasks.length} steps to user tasks`
          }
        >
          Auton8 runs work through user tasks. A manual task or a plain task looks
          like a step somebody performs, but the engine passes straight through it
          without waiting for anyone — so the process would finish having skipped it.
          {convertedTasks.some((task) => task.name) && (
            <>
              {" "}
              Converted:{" "}
              {convertedTasks.map((task) => task.name ?? task.id).join(", ")}.
            </>
          )}{" "}
          Give each one an assignee, or candidate users or groups, before publishing.
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
          canElevate={canElevateScript}
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

      {timerBoundaryEditor && (
        <TimerBoundaryEventModal
          editor={timerBoundaryEditor}
          onChange={setTimerBoundaryEditor}
          onClose={() => {
            if (busy) return;
            setTimerBoundaryEditor(null);
          }}
          onApply={applyTimerBoundary}
          disabled={!!busy || !handle}
        />
      )}

      {conditionalEventEditor && (
        <ConditionalEventModal
          editor={conditionalEventEditor}
          onChange={setConditionalEventEditor}
          onClose={() => {
            if (busy) return;
            setConditionalEventEditor(null);
    setTimerBoundaryEditor(null);
          }}
          onApply={applyConditionalEvent}
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

      {messageEditor && (
        <MessageElementModal
          editor={messageEditor}
          onChange={setMessageEditor}
          onClose={() => {
            if (busy) return;
            setMessageEditor(null);
          }}
          onApply={applyMessageElement}
          disabled={!!busy || !handle}
        />
      )}

      {signalEditor && (
        <SignalEventModal
          editor={signalEditor}
          onChange={setSignalEditor}
          onClose={() => {
            if (busy) return;
            setSignalEditor(null);
          }}
          onApply={applySignalEvent}
          disabled={!!busy || !handle}
        />
      )}

      {callActivityEditor && (
        <CallActivityModal
          editor={callActivityEditor}
          currentProcessKey={currentModel?.processKey ?? null}
          onChange={setCallActivityEditor}
          onClose={() => {
            if (busy) return;
            setCallActivityEditor(null);
          }}
          onApply={applyCallActivity}
          disabled={!!busy || !handle}
        />
      )}

      {codedEventEditor && (
        <CodedEventModal
          editor={codedEventEditor}
          onChange={setCodedEventEditor}
          onClose={() => {
            if (busy) return;
            setCodedEventEditor(null);
          }}
          onApply={applyCodedEvent}
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

      {elementDataEditor && (
        <ElementDataModal
          editor={elementDataEditor}
          onChange={setElementDataEditor}
          onClose={() => {
            if (busy) return;
            setElementDataEditor(null);
          }}
          onApply={applyElementData}
          disabled={!!busy}
        />
      )}

      {genericEditor && (
        <GenericElementModal
          editor={genericEditor}
          onChange={setGenericEditor}
          onClose={() => {
            if (busy) return;
            setGenericEditor(null);
    setConditionalEventEditor(null);
    setTimerBoundaryEditor(null);
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
          Name the new workflow model. Auton8 will create a blank draft in the database and load
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

// #159/#163/#166. The panel behind the three element-data shapes.
//
// Each field is a real label bound to its control, and the copy says what the
// value DOES rather than naming the BPMN attribute — an author setting a
// completion condition is deciding when the case is finished, not editing
// `completionCondition`.
function ElementDataModal({
  editor,
  onChange,
  onClose,
  onApply,
  disabled
}: {
  editor: ElementDataEditor;
  onChange: (next: ElementDataEditor) => void;
  onClose: () => void;
  onApply: () => void;
  disabled: boolean;
}) {
  const title =
    editor.kind === "adhoc"
      ? "Case Work"
      : editor.kind === "dataObject"
        ? "Data"
        : "Repeat For Each";

  return (
    <Modal opened onClose={onClose} title={title} size="lg">
      <Stack gap="md">
        <Group gap="xs" wrap="wrap">
          <Code>{editor.id}</Code>
          <Code>{editor.type}</Code>
        </Group>

        <TextInput
          label="Name"
          value={editor.name}
          onChange={(e) => onChange({ ...editor, name: e.currentTarget.value })}
        />

        {editor.kind === "adhoc" && (
          <>
            <Text size="sm" c="dimmed">
              The steps inside run in no fixed order — a person picks what happens next,
              as often as they need, until this condition is true.
            </Text>
            <TextInput
              label="Finished when"
              placeholder="${approved == true}"
              description="Without this the section can never finish, and publishing is refused."
              value={editor.completionCondition}
              onChange={(e) => onChange({ ...editor, completionCondition: e.currentTarget.value })}
            />
            <Switch
              label="Run the steps one at a time"
              checked={editor.sequential}
              onChange={(e) => onChange({ ...editor, sequential: e.currentTarget.checked })}
            />
          </>
        )}

        {editor.kind === "dataObject" && (
          <>
            <Text size="sm" c="dimmed">
              Declares a process variable by this name. Conditions can then use it without
              being warned that nothing sets it.
            </Text>
            <Select
              label="Type"
              data={[
                { value: "", label: "Not specified" },
                { value: "xsd:string", label: "Text" },
                { value: "xsd:double", label: "Number" },
                { value: "xsd:boolean", label: "Yes / no" },
                { value: "xsd:dateTime", label: "Date and time" }
              ]}
              description="Sets the variable's starting type. It is not enforced once the process is running."
              value={editor.dataType}
              onChange={(value) => onChange({ ...editor, dataType: value ?? "" })}
            />
          </>
        )}

        {editor.kind === "multiInstance" && (
          <>
            <Text size="sm" c="dimmed">
              Runs this step once per item in a list. Each run sees its own item under the
              name below.
            </Text>
            <TextInput
              label="List to repeat over"
              placeholder="${items}"
              value={editor.collection}
              onChange={(e) => onChange({ ...editor, collection: e.currentTarget.value })}
            />
            <TextInput
              label="Name for each item"
              placeholder="item"
              description="Scripts read it with variables.get('item')."
              value={editor.elementVariable}
              onChange={(e) => onChange({ ...editor, elementVariable: e.currentTarget.value })}
            />
            <TextInput
              label="Stop early when"
              placeholder="${nrOfCompletedInstances >= 2}"
              description="Optional. Remaining runs are cancelled when this becomes true."
              value={editor.completionCondition}
              onChange={(e) => onChange({ ...editor, completionCondition: e.currentTarget.value })}
            />
            <Switch
              label="Run them one at a time"
              description="Off means every item runs at once."
              checked={editor.sequential}
              onChange={(e) => onChange({ ...editor, sequential: e.currentTarget.checked })}
            />
          </>
        )}

        <Group justify="flex-end">
          <Button variant="default" onClick={onClose} disabled={disabled}>
            Cancel
          </Button>
          <Button onClick={onApply} loading={disabled}>
            Apply
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
  disabled,
  canElevate
}: {
  editor: ScriptTaskEditor;
  onChange: (next: ScriptTaskEditor) => void;
  onClose: () => void;
  onApply: () => void;
  disabled: boolean;
  canElevate: boolean;
}) {
  // #218. The same panel edits a complex gateway's routing script — the fields
  // are the fields — but the copy must not tell an author they are editing a
  // script task, and a gateway's result variable is generated, not theirs.
  const isRoutingGateway = editor.type === "bpmn:ComplexGateway";

  return (
    <Modal
      opened
      onClose={onClose}
      title={isRoutingGateway ? "Complex Gateway" : "Script Task"}
      size="xl"
    >
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          {isRoutingGateway
            ? "This gateway routes on your script. Return the id of one of its outgoing " +
              "sequence flows; returning anything else fails the step rather than quietly " +
              "taking a branch. The ids are available to the script as autonateRoutes."
            : "Edit the selected BPMN script task. Auton8 saves the JavaScript body inline in the " +
              "BPMN XML and validates it before save or publish."}
        </Text>

        <Group gap="xs" wrap="wrap">
          <Code>{editor.id}</Code>
          <Code>{editor.type}</Code>
        </Group>

        <Group gap="md" grow align="flex-start" wrap="wrap">
          <TextInput
            label={isRoutingGateway ? "Gateway Name" : "Task Name"}
            value={editor.name}
            onChange={(e) => onChange({ ...editor, name: e.currentTarget.value })}
          />
          <Select
            label="Language"
            data={[
              { value: "javascript", label: "JavaScript" },
              { value: "python", label: "Python" }
            ]}
            value={editor.scriptFormat === "python" ? "python" : "javascript"}
            onChange={(value) =>
              onChange({ ...editor, scriptFormat: value === "python" ? "python" : "javascript" })
            }
            allowDeselect={false}
          />
          {/* A routing gateway's result variable is generated by the publish-time
              expansion and bound to the outgoing flows' conditions. Offering it
              here would let an author break the binding with no way to tell. */}
          {!isRoutingGateway && (
            <TextInput
              label="Result Variable"
              value={editor.resultVariable}
              onChange={(e) => onChange({ ...editor, resultVariable: e.currentTarget.value })}
            />
          )}
        </Group>

        {/* #153. Unset is the default and the common case; the two explicit
            options exist for the diagrams where "the last user task" has no
            single answer. `system` is hidden from an author who lacks the
            permission — and the server checks it again on publish, because a
            hidden control is not a gate. */}
        <Select
          label="Run as"
          description="Whose permissions this script runs with."
          data={[
            { value: "", label: "The last user task's assignee (default)" },
            { value: "workflowAuthor", label: "The workflow author" },
            ...(canElevate
              ? [{ value: "system", label: "System — bypasses individual permission checks" }]
              : [])
          ]}
          value={editor.runAs}
          onChange={(value) => onChange({ ...editor, runAs: value ?? "" })}
          allowDeselect={false}
        />

        {editor.runAs === "system" && (
          <Alert color="yellow" title="This step runs as the system">
            It bypasses individual permission checks. It does not leave the sandbox — process
            variables and the host API remain the only things a script can reach.
          </Alert>
        )}

        {!canElevate && editor.runAs === "system" && (
          <Alert color="orange" title="You cannot publish this setting">
            This script task is set to run as the system, but you do not have permission to
            author that. Publishing will be refused until it is changed or the permission is
            granted.
          </Alert>
        )}

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
              extensions={[editor.scriptFormat === "python" ? python() : javascript()]}
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

        <Divider />

        {/* #152. Keyed on the script so editing it clears a stale result —
            otherwise the panel would show output from code that is no longer
            in the editor, which is worse than showing none. */}
        <ScriptTestRunPanel
          key={`${editor.scriptFormat}:${editor.script}`}
          script={editor.script}
          scriptFormat={editor.scriptFormat}
        />

        <Divider />

        {/* #168. Script tasks are always retry points — WorkflowBpmnXml's
            ForceAsyncScriptTasks sets flowable:async on every one at publish,
            so a thrown error becomes a job failure instead of a 500 on the
            start call. Shown as fixed rather than as a switch that silently
            does nothing. */}
        <Switch
          label="Retry this step on its own if it fails"
          description={
            "Always on for script tasks. Auton8 saves the workflow's progress just before a " +
            "script runs, so a script that fails is retried by itself and its error is reported " +
            "rather than failing the whole start."
          }
          checked
          disabled
          readOnly
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
    // made grep classify all 3,900 lines as binary and skip them (archived-116); the
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
          Configure a Dapr pub/sub event that starts this workflow. Auton8 listens on the
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
                  <code>YYYY-MM-DDTHH:mm:ss</code>. Times use the Flowable engine&apos;s timezone (UTC by
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
          Run a predefined Auton8 routine when the workflow reaches this step. The behavior
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
            Behavior runs a curated routine inside Auton8. More service-task types (HTTP webhook,
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
                key, but the workflow can&apos;t run until a matching behavior is registered.
              </p>
            )}
          </label>
        )}

        <Divider />

        {/* #168. Worded as what it does, not as "async". The trade-off is
            stated because an author choosing this should know what they are
            buying: a checkpoint costs a brief pause and saves redoing the work
            in front of it. */}
        <Switch
          label="Retry this step on its own if it fails"
          description={
            "Auton8 saves the workflow's progress just before this step. If the step fails it is " +
            "retried by itself, instead of redoing everything since the last save. The trade-off " +
            "is that the workflow pauses here briefly even when nothing goes wrong."
          }
          checked={editor.retryPoint}
          onChange={(e) => onChange({ ...editor, retryPoint: e.currentTarget.checked })}
        />

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

// #112. One modal for every message element, because they differ only in which
// fields mean anything:
//
//   start — nothing to correlate to; no instance exists yet, so a key written
//           here would look like a filter that silently matches everything.
//   catch — a correlation key: which process variable identifies THIS instance
//           to a sender.
//   send  — which workflow to address, and the variable here whose value picks
//           the instance over there.
function MessageElementModal({
  editor,
  onChange,
  onClose,
  onApply,
  disabled
}: {
  editor: MessageElementEditor;
  onChange: (next: MessageElementEditor) => void;
  onClose: () => void;
  onApply: () => void;
  disabled: boolean;
}) {
  const isSend = editor.direction === "send";
  const isStart = editor.direction === "start";

  return (
    <Modal opened onClose={onClose} title={`${humanizeBpmnType(editor.type)} (Message)`} size="lg">
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          {isSend
            ? "Tell another workflow that something happened here. Auton8 finds the one waiting " +
              "process instance whose correlation value matches, and delivers to it."
            : isStart
              ? "Starts a new run of this workflow when this message arrives. Nothing is waiting " +
                "yet, so there is nothing to correlate against."
              : "Waits here until this message arrives. The correlation key is how a sender says " +
                "which run of this workflow it means."}
        </Text>

        <Group gap="xs" wrap="wrap">
          <Code>{editor.id}</Code>
          <Code>{editor.type}</Code>
        </Group>

        <label className="workflow-field">
          <span>Name (optional)</span>
          <input
            className="form-control"
            aria-label="Message element name"
            value={editor.name}
            onChange={(e) => onChange({ ...editor, name: e.target.value })}
            placeholder="Await payment"
          />
        </label>

        <label className="workflow-field">
          <span>Message</span>
          <input
            className="form-control"
            aria-label="Message"
            value={editor.messageName}
            disabled={!editor.editableMessageName}
            onChange={(e) => onChange({ ...editor, messageName: e.target.value })}
            placeholder="paymentCleared"
          />
          <p className="workflow-modal-note">
            {editor.editableMessageName
              ? "The name a sender uses to address this."
              : "Comes from the message declared on the diagram, so it always matches what the " +
                "engine subscribes to."}
          </p>
        </label>

        {isSend && (
          <label className="workflow-field">
            <span>Send to workflow</span>
            <input
              className="form-control"
              aria-label="Send to workflow"
              value={editor.targetProcessKey}
              onChange={(e) => onChange({ ...editor, targetProcessKey: e.target.value })}
              placeholder="orders"
            />
            <p className="workflow-modal-note">
              The process key of the workflow to notify. Auton8 never broadcasts &mdash; a message
              goes to exactly one waiting run, or the send reports that it found none.
            </p>
          </label>
        )}

        {!isStart && (
          <label className="workflow-field">
            <span>Correlation key</span>
            <input
              className="form-control"
              aria-label="Correlation key"
              value={editor.correlationKey}
              onChange={(e) => onChange({ ...editor, correlationKey: e.target.value })}
              placeholder="orderId"
            />
            <p className="workflow-modal-note">
              {isSend
                ? "The process variable here whose value identifies the run to notify."
                : "The process variable that identifies this run. A sender supplies its value."}{" "}
              It must be unique among waiting runs: if two match, Auton8 refuses and tells the
              sender how many, rather than picking one.
            </p>
          </label>
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

// #114. Error and escalation events share one editor because they share one
// mechanism: a code thrown at one point and caught at another. The whole risk in
// that mechanism is that the two codes do not match, in which case nothing
// happens and nothing says so — which is why the code is the only required field
// and why the note says out loud what a mismatch costs.
function CodedEventModal({
  editor,
  onChange,
  onClose,
  onApply,
  disabled
}: {
  editor: CodedEventEditor;
  onChange: (next: CodedEventEditor) => void;
  onClose: () => void;
  onApply: () => void;
  disabled: boolean;
}) {
  const isError = editor.kind === "error";
  const noun = isError ? "Error" : "Escalation";
  const isThrowing =
    editor.type === "bpmn:EndEvent" || editor.type === "bpmn:IntermediateThrowEvent";

  return (
    <Modal opened onClose={onClose} title={`${humanizeBpmnType(editor.type)} (${noun})`} size="lg">
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          {isError
            ? isThrowing
              ? "Stops this stretch of the process and hands control to whichever boundary event " +
                "carries the same code."
              : "Catches an error raised inside the activity this is attached to, and takes the " +
                "process down this path instead. An error boundary always interrupts."
            : isThrowing
              ? "Raises a flag for something further out to handle. Unlike an error, the process " +
                "carries on from here."
              : "Handles an escalation raised inside the activity this is attached to."}
        </Text>

        <Group gap="xs" wrap="wrap">
          <Code>{editor.id}</Code>
          <Code>{editor.type}</Code>
        </Group>

        <label className="workflow-field">
          <span>Name (optional)</span>
          <input
            className="form-control"
            aria-label="Event name"
            value={editor.name}
            onChange={(e) => onChange({ ...editor, name: e.target.value })}
            placeholder={isError ? "Payment declined" : "Needs a manager"}
          />
        </label>

        <label className="workflow-field">
          <span>{noun} code</span>
          <input
            className="form-control"
            aria-label={`${noun} code`}
            value={editor.code}
            onChange={(e) => onChange({ ...editor, code: e.target.value })}
            placeholder={isError ? "PAYMENT_DECLINED" : "NEEDS_MANAGER"}
          />
          <p className="workflow-modal-note">
            This is what matches one end to the other, character for character. A code that
            nothing catches is not a warning at publish for escalations &mdash; it just means
            nobody was listening. For errors it <strong>is</strong> refused at publish, because
            an error nobody catches destroys the whole run.
          </p>
        </label>

        {editor.interrupting !== null && (
          <Switch
            label="Stop the attached step while this is handled"
            description={
              "On, the step is cancelled and only this path continues. Off, the step keeps " +
              "running and this path runs alongside it."
            }
            checked={editor.interrupting}
            onChange={(e) => onChange({ ...editor, interrupting: e.currentTarget.checked })}
          />
        )}

        <Group justify="flex-end" gap="xs">
          <Button variant="default" onClick={onClose}>
            Close
          </Button>
          <Button onClick={onApply} disabled={disabled || !editor.code.trim()}>
            Apply
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

// #113. Choosing, not typing. The list is the published workflows, which is what
// makes the publish-time check meaningful: a key picked from here resolves, and
// publish pins the exact version it resolved to.
//
// The current workflow is excluded. A first version calling itself has nothing to
// resolve and is refused at publish anyway; leaving it in the list would offer a
// choice that cannot work.
function CallActivityModal({
  editor,
  currentProcessKey,
  onChange,
  onClose,
  onApply,
  disabled
}: {
  editor: CallActivityEditor;
  currentProcessKey: string | null;
  onChange: (next: CallActivityEditor) => void;
  onClose: () => void;
  onApply: () => void;
  disabled: boolean;
}) {
  const { data: workflows = [], isLoading } = useWorkflows();

  const choices = workflows.filter(
    (w) => w.publishedVersionNumber != null && w.processKey !== currentProcessKey
  );
  const chosenIsMissing =
    editor.calledElement.length > 0 &&
    !choices.some((w) => w.processKey === editor.calledElement);

  const renderMappings = (
    label: string,
    hint: string,
    rows: VariableMapping[],
    onRows: (next: VariableMapping[]) => void
  ) => (
    <Box>
      <Text size="sm" fw={500}>{label}</Text>
      <Text size="xs" c="dimmed" mb="xs">{hint}</Text>
      <Stack gap="xs">
        {rows.map((row, index) => (
          <Group key={index} gap="xs" wrap="nowrap">
            <input
              className="form-control"
              aria-label={`${label} source ${index + 1}`}
              value={row.source}
              placeholder="from"
              onChange={(e) =>
                onRows(rows.map((r, i) => (i === index ? { ...r, source: e.target.value } : r)))
              }
            />
            <Text size="sm" c="dimmed">→</Text>
            <input
              className="form-control"
              aria-label={`${label} target ${index + 1}`}
              value={row.target}
              placeholder="to"
              onChange={(e) =>
                onRows(rows.map((r, i) => (i === index ? { ...r, target: e.target.value } : r)))
              }
            />
            <Button
              variant="subtle"
              size="compact-sm"
              aria-label={`Remove ${label} row ${index + 1}`}
              onClick={() => onRows(rows.filter((_, i) => i !== index))}
            >
              Remove
            </Button>
          </Group>
        ))}
        <Button
          variant="default"
          size="compact-sm"
          onClick={() => onRows([...rows, { source: "", target: "" }])}
        >
          Add {label.toLowerCase()}
        </Button>
      </Stack>
    </Box>
  );

  return (
    <Modal opened onClose={onClose} title="Call Activity" size="lg">
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          Runs another workflow as a step here and waits for it to finish. The version running
          now is locked in when you publish this workflow &mdash; republishing the other one
          will not change what this step calls, so a process already running cannot change
          behaviour underneath you.
        </Text>

        {/* #113. Both of these are consequences of pinning that an author has to
            be told, because neither is guessable from the diagram. */}
        <Alert color="blue" variant="light" title="Two things to know">
          <Text size="sm">
            <strong>To pick up a newer version of the other workflow, publish this one again.</strong>{" "}
            That is the only way to move a call activity forward &mdash; which is deliberate, but it
            does mean a fix to a shared workflow reaches callers only as each is republished.
          </Text>
          <Text size="sm" mt="xs">
            <strong>Cancelling a run of this workflow also cancels the run it started here.</strong>{" "}
            The other workflow&apos;s run ends with the same reason; it is not left going on its own.
          </Text>
        </Alert>

        <Group gap="xs" wrap="wrap">
          <Code>{editor.id}</Code>
          <Code>{editor.type}</Code>
        </Group>

        <label className="workflow-field">
          <span>Step name (optional)</span>
          <input
            className="form-control"
            aria-label="Call activity name"
            value={editor.name}
            onChange={(e) => onChange({ ...editor, name: e.target.value })}
            placeholder="Run credit check"
          />
        </label>

        <label className="workflow-field">
          <span>Workflow to run</span>
          <select
            className="form-select"
            aria-label="Workflow to run"
            value={editor.calledElement}
            disabled={isLoading}
            onChange={(e) => onChange({ ...editor, calledElement: e.target.value })}
          >
            <option value="">{isLoading ? "Loading…" : "Select a workflow…"}</option>
            {choices.map((w) => (
              <option key={w.id} value={w.processKey}>
                {w.name}
              </option>
            ))}
            {/* A key saved earlier whose workflow is gone or unpublished stays
                visible, so an author can see what is wired up before changing it
                rather than finding the field mysteriously blank. */}
            {chosenIsMissing && (
              <option value={editor.calledElement}>
                {editor.calledElement} (not published on this server)
              </option>
            )}
          </select>
          {chosenIsMissing && (
            <p className="workflow-modal-note text-warning">
              Nothing published has that key. Publishing this workflow will be refused until it
              exists &mdash; which is deliberate: otherwise this step fails when someone runs it.
            </p>
          )}
        </label>

        {renderMappings(
          "Send in",
          "Variables from this workflow, and the name each arrives under in the other one.",
          editor.inputs,
          (inputs) => onChange({ ...editor, inputs })
        )}

        {renderMappings(
          "Bring back",
          "Variables from the other workflow, and the name each returns under here.",
          editor.outputs,
          (outputs) => onChange({ ...editor, outputs })
        )}

        <Group justify="flex-end" gap="xs">
          <Button variant="default" onClick={onClose}>
            Close
          </Button>
          <Button onClick={onApply} disabled={disabled || !editor.calledElement.trim()}>
            Apply
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

// #156. One editor for every signal event — throw, catch, boundary, end — because
// they share one mechanism: a name raised at one point and caught at another.
//
// The failure mode worth designing against is a mistyped name, which produces
// silence rather than an error, so the note says that out loud.
function SignalEventModal({
  editor,
  onChange,
  onClose,
  onApply,
  disabled
}: {
  editor: SignalEventEditor;
  onChange: (next: SignalEventEditor) => void;
  onClose: () => void;
  onApply: () => void;
  disabled: boolean;
}) {
  const isThrowing =
    editor.type === "bpmn:EndEvent" || editor.type === "bpmn:IntermediateThrowEvent";

  return (
    <Modal opened onClose={onClose} title={`${humanizeBpmnType(editor.type)} (Signal)`} size="lg">
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          {isThrowing
            ? "Raises a signal. Everything listening for that name reacts — a signal is a broadcast, unlike a message, which goes to exactly one waiting run."
            : "Waits for a signal with this name to be raised."}
        </Text>

        <Group gap="xs" wrap="wrap">
          <Code>{editor.id}</Code>
          <Code>{editor.type}</Code>
        </Group>

        <label className="workflow-field">
          <span>Event name (optional)</span>
          <input
            className="form-control"
            aria-label="Signal event name"
            value={editor.name}
            onChange={(e) => onChange({ ...editor, name: e.target.value })}
            placeholder="Approved"
          />
        </label>

        <label className="workflow-field">
          <span>Signal name</span>
          <input
            className="form-control"
            aria-label="Signal name"
            value={editor.signalName}
            onChange={(e) => onChange({ ...editor, signalName: e.target.value })}
            placeholder="approved"
          />
          <p className="workflow-modal-note">
            This is what matches one end to the other, character for character. A name nothing
            listens for is not an error &mdash; a signal is a broadcast, so it simply reaches
            nobody, which looks exactly like a mistyped name.
          </p>
        </label>

        <label className="workflow-field">
          <span>Who hears it</span>
          <select
            className="form-select"
            aria-label="Who hears it"
            value={editor.scope}
            onChange={(e) => onChange({ ...editor, scope: e.target.value })}
          >
            <option value="instance">Only this run of this workflow</option>
            <option value="global">Any workflow listening for this name</option>
          </select>
          <p className="workflow-modal-note">
            {editor.scope === "instance"
              ? "The safe default. Another run of this same workflow will not react, and neither will anything else."
              : "Careful: every workflow listening for this name reacts, including ones you did not write. Two unrelated workflows both using a name like “approved” will couple to each other, and neither diagram will show it."}
          </p>
        </label>

        {editor.interrupting !== null && (
          <Switch
            label="Stop the attached step while this is handled"
            description={
              "On, the step is cancelled and only this path continues. Off, the step keeps " +
              "running and this path runs alongside it."
            }
            checked={editor.interrupting}
            onChange={(e) => onChange({ ...editor, interrupting: e.currentTarget.checked })}
          />
        )}

        <Group justify="flex-end" gap="xs">
          <Button variant="default" onClick={onClose}>
            Close
          </Button>
          <Button onClick={onApply} disabled={disabled || !editor.signalName.trim()}>
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
            &quot;Complete Task&quot; button when this task flows into a normal node, or one
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
              <label className="form-label" htmlFor="user-form-render-mode">
                Render mode
              </label>
              <select
                id="user-form-render-mode"
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

              <label className="form-label mt-3" htmlFor="user-form-short-code">
                Form
              </label>
              <select
                id="user-form-short-code"
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
                The form&apos;s process variables are passed in as <code>data</code>; submitting calls{" "}
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

// #107: the BPMN types panel is derived from the support manifest, not from two
// hand-kept arrays beside it.
//
// It used to be `SUPPORTED_BPMN_TYPES` and `COMING_SOON_BPMN_TYPES` declared here,
// maintained in parallel with the palette and with the backend's `UnsupportedRuntime*`
// deny-lists. #103 deployed all 68 to a running Flowable and found the three lists
// disagreeing on 47 of them — 22 shown as "coming soon" that deployed unhindered, and
// 25 refused at runtime that the engine runs. Adding support for an element is now
// one edit to `src/shared/bpmn-support.json`, and this panel follows.
// #157. The timer boundary editor.
//
// Three kinds, one at a time — Flowable rejects a definition carrying two, so the
// picker is a radio rather than three independent fields. What the editor adds over
// three text boxes is the interrupting choice, worded as what it does to the work
// rather than as the BPMN attribute name.
function TimerBoundaryEventModal({
  editor,
  onChange,
  onClose,
  onApply,
  disabled
}: {
  editor: TimerBoundaryEventEditor;
  onChange: (next: TimerBoundaryEventEditor) => void;
  onClose: () => void;
  onApply: () => void;
  disabled: boolean;
}) {
  const value =
    editor.mode === "duration" ? editor.duration : editor.mode === "date" ? editor.date : editor.cycle;

  const field = {
    duration: {
      label: "Duration",
      description: "An ISO-8601 duration measured from when the activity starts.",
      placeholder: "PT15M"
    },
    date: {
      label: "Date and time",
      description: "A fixed moment. The timer fires then, whatever the activity is doing.",
      placeholder: "2026-12-31T09:00:00"
    },
    cycle: {
      label: "Repeating cycle",
      description: "An ISO-8601 repeating interval. Bound the repeats, or it fires forever.",
      placeholder: "R3/PT1H"
    }
  }[editor.mode];

  return (
    <Modal opened onClose={onClose} title="Timer Boundary Event" size="lg">
      <Stack gap="md">
        <TextInput
          label="Name"
          value={editor.name}
          onChange={(event) => onChange({ ...editor, name: event.currentTarget.value })}
          placeholder="Escalate after 15 minutes"
        />

        <Radio.Group
          label="When it fires"
          value={editor.mode}
          onChange={(mode) =>
            onChange({ ...editor, mode: mode as TimerBoundaryEventEditor["mode"] })
          }
        >
          <Stack gap="xs" mt="xs">
            <Radio value="duration" label="After a period of time" />
            <Radio value="date" label="At a specific date and time" />
            <Radio value="cycle" label="Repeatedly, on a cycle" />
          </Stack>
        </Radio.Group>

        <TextInput
          label={field.label}
          description={field.description}
          value={value}
          placeholder={field.placeholder}
          onChange={(event) => {
            const next = event.currentTarget.value;
            onChange({
              ...editor,
              duration: editor.mode === "duration" ? next : editor.duration,
              date: editor.mode === "date" ? next : editor.date,
              cycle: editor.mode === "cycle" ? next : editor.cycle
            });
          }}
        />

        <Radio.Group
          label="When it fires, what happens to the activity?"
          value={editor.interrupting ? "interrupt" : "continue"}
          onChange={(choice) => onChange({ ...editor, interrupting: choice === "interrupt" })}
        >
          <Stack gap="xs" mt="xs">
            <Radio value="interrupt" label="Cancel it and take the timer's path instead" />
            <Radio value="continue" label="Leave it running and take the timer's path as well" />
          </Stack>
        </Radio.Group>

        {editor.mode === "cycle" && editor.interrupting && (
          <Alert color="yellow" variant="light" title="A repeating timer that interrupts fires once">
            Cancelling the activity removes the timer with it, so the remaining repeats
            never happen. Repeating timers are usually left non-interrupting.
          </Alert>
        )}

        <Group justify="flex-end" gap="xs">
          <Button variant="default" onClick={onClose}>
            Close
          </Button>
          <Button onClick={onApply} disabled={disabled || value.trim().length === 0}>
            Apply
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

// #158. One editor for every conditional event placement.
//
// The condition is a raw expression, matching how exclusive gateways already work
// — consistency with what ships, and a builder would need a raw escape hatch
// anyway. What the editor adds over a bare text box is the two things an author
// cannot infer: that the condition is not re-checked continuously, and (on a
// boundary event) what interrupting actually does to the attached activity.
function ConditionalEventModal({
  editor,
  onChange,
  onClose,
  onApply,
  disabled
}: {
  editor: ConditionalEventEditor;
  onChange: (next: ConditionalEventEditor) => void;
  onClose: () => void;
  onApply: () => void;
  disabled: boolean;
}) {
  const isBoundary = editor.interrupting !== null;
  const expression = editor.conditionExpression.trim();

  return (
    <Modal opened onClose={onClose} title="Conditional Event" size="lg">
      <Stack gap="md">
        <TextInput
          label="Name"
          value={editor.name}
          onChange={(event) => onChange({ ...editor, name: event.currentTarget.value })}
          placeholder="When approved"
        />

        <TextInput
          label="Condition"
          description="A Flowable expression. The process continues when it evaluates to true."
          value={editor.conditionExpression}
          onChange={(event) =>
            onChange({ ...editor, conditionExpression: event.currentTarget.value })
          }
          placeholder="${approved == true}"
          error={
            expression.length > 0 && !expression.startsWith("${")
              ? "Wrap the condition in ${ } so Flowable evaluates it."
              : null
          }
        />

        {isBoundary && (
          <Radio.Group
            label="When the condition becomes true"
            value={editor.interrupting ? "interrupt" : "continue"}
            onChange={(value) => onChange({ ...editor, interrupting: value === "interrupt" })}
          >
            <Stack gap="xs" mt="xs">
              <Radio
                value="interrupt"
                label="Cancel the attached activity and take this path"
              />
              <Radio
                value="continue"
                label="Take this path as well, and let the attached activity carry on"
              />
            </Stack>
          </Radio.Group>
        )}

        {/* The one thing an author cannot discover by trying it, because trying it
            looks like the feature is broken. Established by running it against
            Flowable 8.0.0, not read from documentation. */}
        <Alert color="blue" variant="light" title="Conditions are checked when something changes">
          Flowable does not watch this condition continuously. Auton8 asks it to
          re-check after a process variable is set or a user task is completed, which
          covers the usual ways a condition becomes true. A condition that is already
          true when the process arrives here still waits for the next such change.
        </Alert>

        <Group justify="flex-end" gap="xs">
          <Button variant="default" onClick={onClose}>
            Close
          </Button>
          <Button onClick={onApply} disabled={disabled || expression.length === 0}>
            Apply
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function BpmnTypesModal({ onClose }: { onClose: () => void }) {
  // Three display buckets from two manifest axes. "Coming soon" and "not available"
  // both read as unsupported to an author, but they are opposite problems — one is
  // work we have not done, the other is work the engine cannot do — and an author
  // deciding whether to wait or to redraw needs to know which.
  const supported = groupByCategory(EXECUTABLE_SUPPORTED_ELEMENTS);
  const comingSoon = groupByCategory(
    COMING_SOON_ELEMENTS.filter((element) => element.engine !== "cannot-execute")
  );
  const unavailable = COMING_SOON_ELEMENTS.filter(
    (element) => element.engine === "cannot-execute"
  );
  const annotations = ANNOTATION_ELEMENTS;

  const count = (groups: BpmnSupportGroup[]) =>
    groups.reduce((n, group) => n + group.items.length, 0);

  return (
    <Modal
      opened
      onClose={onClose}
      title="BPMN element support"
      size="xl"
      classNames={{ content: "workflow-bpmn-types-modal" }}
    >
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          What the Auton8 workflow studio can model and execute today, and what is still
          to come. Every verdict here was established by deploying the element to
          Flowable {FLOWABLE_VERSION} and starting it. An element the engine cannot run
          is refused when you publish, rather than deploying and quietly doing nothing.
        </Text>

        <div className="workflow-bpmn-types-grid">
          <section className="workflow-bpmn-types-column workflow-bpmn-types-column-supported">
            <header className="workflow-bpmn-types-column-header">
              <h3>
                <i className="fa fa-circle-check" aria-hidden="true"></i>
                Supported
              </h3>
              <span className="workflow-bpmn-types-count">{count(supported)}</span>
            </header>
            {supported.map((group) => (
              <div key={group.category} className="workflow-bpmn-types-group">
                <h4>{group.category}</h4>
                <ul>
                  {group.items.map((item) => (
                    <li key={item.name}>{item.name}</li>
                  ))}
                </ul>
              </div>
            ))}
          </section>

          <section className="workflow-bpmn-types-column workflow-bpmn-types-column-coming">
            <header className="workflow-bpmn-types-column-header">
              <h3>
                <i className="fa fa-hourglass-half" aria-hidden="true"></i>
                Coming soon
              </h3>
              <span className="workflow-bpmn-types-count">{count(comingSoon)}</span>
            </header>
            <p className="workflow-bpmn-types-note">
              Flowable runs these. The studio has no property editor for them yet, so
              publishing one is refused until its story lands.
            </p>
            {comingSoon.map((group) => (
              <div key={group.category} className="workflow-bpmn-types-group">
                <h4>{group.category}</h4>
                <ul>
                  {group.items.map((item) => (
                    <li key={item.name}>{item.name}</li>
                  ))}
                </ul>
              </div>
            ))}
          </section>
        </div>

        {unavailable.length > 0 && (
          <section className="workflow-bpmn-types-column workflow-bpmn-types-column-unavailable">
            <header className="workflow-bpmn-types-column-header">
              <h3>
                <i className="fa fa-circle-exclamation" aria-hidden="true"></i>
                Not available
              </h3>
              <span className="workflow-bpmn-types-count">{unavailable.length}</span>
            </header>
            <p className="workflow-bpmn-types-note">
              Flowable {FLOWABLE_VERSION} cannot run these, so publishing a diagram that
              uses one is refused with the reason below.
            </p>
            <ul className="workflow-bpmn-types-reasons">
              {unavailable.map((item) => (
                <li key={item.name}>
                  <span className="workflow-bpmn-types-reason-name">{item.name}</span>
                  <span className="workflow-bpmn-types-reason-text">{item.reason}</span>
                </li>
              ))}
            </ul>
          </section>
        )}

        <section className="workflow-bpmn-types-column workflow-bpmn-types-column-annotations">
          <header className="workflow-bpmn-types-column-header">
            <h3>
              <i className="fa fa-note-sticky" aria-hidden="true"></i>
              Annotations
            </h3>
            <span className="workflow-bpmn-types-count">{annotations.length}</span>
          </header>
          <p className="workflow-bpmn-types-note">
            Draw these freely — BPMN defines them as documentation. They never execute,
            and that is not a gap in Auton8.
          </p>
          <div className="workflow-bpmn-types-group">
            <ul>
              {annotations.map((item) => (
                <li key={item.name}>{item.name}</li>
              ))}
            </ul>
          </div>
        </section>

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
                  xmlns:autonate="http://autonate.dev/workflows"
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
