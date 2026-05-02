import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useBpmnModeler } from "@/hooks/useBpmnModeler";
import {
  EXECUTIONS_QUERY_KEY,
  useCompleteTask,
  useExecutionTasks,
  useExecutions
} from "@/hooks/useExecutions";
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
import { WorkflowModel } from "@/types/flowable";
import * as workflow from "@/lib/bpmn/workflow.js";
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
import { useEventCatalog } from "@/hooks/useEventCatalog";
import { useWorkflowBehaviors } from "@/hooks/useWorkflowBehaviors";
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
};

type SignalStartEventEditor = {
  id: string;
  type: string;
  name: string;
  signalName: string;
  signalTopic: string;
};

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

const DEFAULT_SIGNAL_TOPIC = "workflow.signals";

type AssignmentMode = "picker" | "expression";

type DueDateMode = "none" | "afterActivation" | "afterProcessStart" | "expression";

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
  timerCycleCron?: string | null;
  timerEndDate?: string | null;
  timerDuration?: string | null;
  timerDate?: string | null;
  serviceTaskKind?: string | null;
  behaviorKey?: string | null;
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
  const [currentModel, setCurrentModel] = useState<WorkflowModel | null>(null);
  const [loadedXml, setLoadedXml] = useState<string | null>(null);
  const [dirty, setDirty] = useState(false);
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

  const onSelectionChanged = useCallback((raw: unknown) => {
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
        signalTopic: selection.signalTopic ?? ""
      });
      setTimerStartEditor(null);
      setScriptTaskEditor(null);
      setSequenceFlowEditor(null);
      setUserTaskEditor(null);
      setTimerIntermediateEditor(null);
      setServiceTaskEditor(null);
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
    } else if (selection && selection.type === "bpmn:SequenceFlow") {
      setSequenceFlowEditor({
        id: selection.id,
        type: selection.type,
        name: selection.name ?? "",
        conditionExpression: selection.conditionExpression ?? ""
      });
      setScriptTaskEditor(null);
      setUserTaskEditor(null);
      setSignalStartEditor(null);
      setTimerStartEditor(null);
      setTimerIntermediateEditor(null);
      setServiceTaskEditor(null);
    } else if (selection && selection.type === "bpmn:UserTask") {
      const assignee = selection.assignee ?? "";
      const candidateUsers = selection.candidateUsers ?? [];
      const candidateGroups = selection.candidateGroups ?? [];
      const assigneeIsExpression = looksLikeExpression(assignee);
      const candidateUsersFirst = candidateUsers[0] ?? "";
      const candidateUsersIsExpression =
        candidateUsers.length === 1 && looksLikeExpression(candidateUsersFirst);
      const dueDate = parseDueDate(selection.dueDate);
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
        dueDateExpression: dueDate.expression
      });
      setScriptTaskEditor(null);
      setSequenceFlowEditor(null);
      setSignalStartEditor(null);
      setTimerStartEditor(null);
      setTimerIntermediateEditor(null);
      setServiceTaskEditor(null);
    } else {
      setScriptTaskEditor(null);
      setSequenceFlowEditor(null);
      setUserTaskEditor(null);
      setSignalStartEditor(null);
      setTimerStartEditor(null);
      setTimerIntermediateEditor(null);
      setServiceTaskEditor(null);
    }
  }, []);

  const callbacks = useMemo(
    () => ({
      NotifyDiagramChanged: onDiagramChanged,
      NotifySelectionChanged: onSelectionChanged
    }),
    [onDiagramChanged, onSelectionChanged]
  );

  const { containerRef, handle, loading: modelerLoading, error: modelerError } = useBpmnModeler({
    xml: loadedXml,
    callbacks
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

      await workflow.updateSignalStartEventProperties(handle, {
        id: signalStartEditor.id,
        name: signalStartEditor.name,
        signalName: signalStartEditor.signalName.trim(),
        signalTopic: signalStartEditor.signalTopic.trim()
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

      await workflow.updateUserTaskProperties(handle, {
        id: userTaskEditor.id,
        name: userTaskEditor.name,
        assignee,
        candidateUsers,
        candidateGroups,
        dueDate
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

  return (
    <>
      <div className="page-head">
        <div>
          <h1 className="page-header mb-1">Workflow Studio</h1>
          <p className="page-head-copy workflow-copy">
            Select a saved workflow model, edit it in the browser, save drafts to AutoNate, publish
            to Flowable, and start new executions from the current model.
          </p>
        </div>
        <button
          type="button"
          className="workflow-bpmn-support-badge"
          onClick={() => setShowBpmnTypesModal(true)}
          title="View supported BPMN node types"
        >
          <i className="bi bi-diagram-3-fill" aria-hidden="true"></i>
          <span>Supported BPMN Types</span>
          <i className="bi bi-arrow-right-short" aria-hidden="true"></i>
        </button>
      </div>

      {error && <div className="alert alert-danger">{error}</div>}
      {status && <div className="alert alert-success">{status}</div>}
      {warnings.length > 0 && (
        <div className="alert alert-warning" role="alert">
          <strong>Compatibility warnings</strong>
          <ul className="workflow-warning-list">
            {warnings.map((w, i) => (
              <li key={i}>{w}</li>
            ))}
          </ul>
        </div>
      )}

      <div className="workflow-toolbar">
        <div className="workflow-selector-panel">
          <label className="workflow-field">
            <span>Workflow Model</span>
            <div className="workflow-selector-inputs">
              <select
                className="form-select"
                value={currentModel?.id ?? ""}
                onChange={(e) => onSelectionChange(e.target.value)}
                disabled={!!busy}
              >
                <option value="">
                  {workflows.length === 0 ? "No workflow models yet" : "Select a workflow model"}
                </option>
                {workflows.map((w) => (
                  <option key={w.id} value={w.id}>
                    {w.name}
                  </option>
                ))}
              </select>
              <button
                type="button"
                className="btn btn-outline-secondary workflow-add-button"
                onClick={() => setShowCreateModal(true)}
                disabled={!!busy}
                aria-label="Create workflow model"
                title="Create workflow model"
              >
                <i className="bi bi-plus-lg" aria-hidden="true"></i>
              </button>
            </div>
          </label>
        </div>

        <div className="workflow-actions">
          <button
            className="btn btn-primary"
            onClick={onSave}
            disabled={!handle || !!busy || !currentModel}
            title="Save"
          >
            Save
          </button>
          <button
            className="btn btn-outline-primary"
            onClick={onPublish}
            disabled={!canPublish}
            title="Publish"
          >
            Publish
          </button>
          <button
            className="btn btn-outline-success"
            onClick={onStartInstance}
            disabled={!canStart}
            title={
              isPaused
                ? "Workflow is paused — resume to start a new instance"
                : "Start instance"
            }
          >
            Start Instance
          </button>
          {isPaused ? (
            <button
              className="btn btn-outline-success"
              onClick={onResume}
              disabled={!canTogglePause}
              title="Resume — allow new instances to start"
            >
              <i className="bi bi-play-fill" aria-hidden="true"></i> Resume
            </button>
          ) : (
            <button
              className="btn btn-outline-warning"
              onClick={onPause}
              disabled={!canTogglePause}
              title="Pause — block new instances; existing runs continue"
            >
              <i className="bi bi-pause-fill" aria-hidden="true"></i> Pause
            </button>
          )}
        </div>
      </div>

      {busy && <p className="workflow-busy">Working on {busy}...</p>}

      <div className="workflow-layout">
        <section className="workflow-main">
          {!currentModel ? (
            <div className="workflow-empty-state">
              <div className="workflow-empty-icon">
                <i className="bi bi-diagram-3" aria-hidden="true"></i>
              </div>
              <h2>Create Your First Workflow</h2>
              <p>
                Workflow models live in the application database and are loaded into the modeler
                from there. Create one to start modeling.
              </p>
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => setShowCreateModal(true)}
                title="Create workflow model"
              >
                Create Workflow Model
              </button>
            </div>
          ) : (
            <div className="workflow-shell">
              <div
                ref={containerRef}
                className="workflow-canvas"
                aria-label="BPMN modeler"
              ></div>
              {modelerLoading && (
                <p className="workflow-muted px-3 py-2">Loading BPMN modeler...</p>
              )}
              {modelerError && (
                <p className="text-danger px-3 py-2">{modelerError.message}</p>
              )}
            </div>
          )}
        </section>

        <WorkflowSidebar currentModel={currentModel} dirty={dirty} />
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

      {showBpmnTypesModal && (
        <BpmnTypesModal onClose={() => setShowBpmnTypesModal(false)} />
      )}
    </>
  );
}

function WorkflowSidebar({
  currentModel,
  dirty
}: {
  currentModel: WorkflowModel | null;
  dirty: boolean;
}) {
  const { data: executions = [] } = useExecutions();
  const { data: runtimeTasks = [] } = useExecutionTasks(currentModel?.activeProcessInstanceId ?? null);
  const completeTask = useCompleteTask();
  const [completing, setCompleting] = useState<string | null>(null);

  const activeExecution = currentModel?.activeProcessInstanceId
    ? executions.find((e) => e.id === currentModel.activeProcessInstanceId)
    : null;

  const runtimeStatus = useMemo(() => {
    if (!currentModel) return "No workflow model selected";
    if (!currentModel.activeProcessInstanceId) return "Not started";
    if (activeExecution) {
      if (activeExecution.status === "Running" && runtimeTasks.length > 0) {
        return "Waiting on user task";
      }
      return activeExecution.status;
    }
    return runtimeTasks.length > 0 ? "Waiting on user task" : "Completed";
  }, [currentModel, activeExecution, runtimeTasks]);

  const onComplete = async (taskId: string) => {
    setCompleting(taskId);
    try {
      await completeTask.mutateAsync({ taskId });
    } finally {
      setCompleting(null);
    }
  };

  const draftStatusLabel = !currentModel
    ? "No model"
    : dirty
      ? `Unsaved changes for v${currentModel.draftVersionNumber}`
      : currentModel.isDraft
        ? `Draft v${currentModel.draftVersionNumber}`
        : `Published v${currentModel.draftVersionNumber}`;

  return (
    <aside className="workflow-sidebar">
      <section className="workflow-card">
        <h2>Model</h2>
        {!currentModel ? (
          <p className="workflow-muted">No workflow model is selected.</p>
        ) : (
          <dl className="workflow-meta">
            <div>
              <dt>Model ID</dt>
              <dd>{currentModel.id}</dd>
            </div>
            <div>
              <dt>Name</dt>
              <dd>{currentModel.name}</dd>
            </div>
            <div>
              <dt>Draft Status</dt>
              <dd>{draftStatusLabel}</dd>
            </div>
            <div>
              <dt>Draft Version</dt>
              <dd>v{currentModel.draftVersionNumber}</dd>
            </div>
            <div>
              <dt>Published Version</dt>
              <dd>
                {currentModel.publishedVersionNumber === null
                  ? "Not published"
                  : `v${currentModel.publishedVersionNumber}`}
              </dd>
            </div>
            <div>
              <dt>Updated</dt>
              <dd>{formatTimestamp(currentModel.updatedAtUtc)}</dd>
            </div>
          </dl>
        )}
      </section>

      <section className="workflow-card">
        <h2>Deployment</h2>
        {!currentModel?.lastDeployment ? (
          <p className="workflow-muted">This workflow model has not been published to Flowable yet.</p>
        ) : (
          <>
            {(currentModel.isDraft || dirty) && (
              <p className="workflow-muted">
                The current workflow is in draft state. Publish it to deploy this version to
                Flowable.
              </p>
            )}
            {currentModel.isSuspended === true && (
              <div className="alert alert-warning py-2 mb-2" role="status">
                <i className="bi bi-pause-circle-fill me-1" aria-hidden="true"></i>
                Paused — new instances are blocked. Existing runs continue.
              </div>
            )}
            <dl className="workflow-meta">
              <div>
                <dt>Definition ID</dt>
                <dd>{currentModel.lastDeployment.processDefinitionId}</dd>
              </div>
              <div>
                <dt>Version</dt>
                <dd>{currentModel.lastDeployment.processDefinitionVersion}</dd>
              </div>
              <div>
                <dt>Deployment ID</dt>
                <dd>{currentModel.lastDeployment.deploymentId}</dd>
              </div>
              <div>
                <dt>Published</dt>
                <dd>{formatTimestamp(currentModel.lastDeployment.deployedAtUtc)}</dd>
              </div>
              <div>
                <dt>Status</dt>
                <dd>
                  {currentModel.isSuspended === true
                    ? "Paused"
                    : currentModel.isSuspended === false
                      ? "Active"
                      : "Unknown"}
                </dd>
              </div>
            </dl>
          </>
        )}
      </section>

      <section className="workflow-card">
        <h2>Runtime</h2>
        <dl className="workflow-meta">
          <div>
            <dt>Instance</dt>
            <dd>{currentModel?.activeProcessInstanceId ?? "Not started"}</dd>
          </div>
          <div>
            <dt>Status</dt>
            <dd>{runtimeStatus}</dd>
          </div>
          <div>
            <dt>Active Tasks</dt>
            <dd>{runtimeTasks.length}</dd>
          </div>
        </dl>

        {runtimeTasks.length > 0 ? (
          <div className="workflow-task-list">
            {runtimeTasks.map((task) => (
              <div key={task.id} className="workflow-task">
                <div>
                  <strong>{task.name}</strong>
                  <div className="workflow-task-meta">Task ID: {task.id}</div>
                </div>
                <button
                  type="button"
                  className="btn btn-sm btn-success"
                  onClick={() => onComplete(task.id)}
                  disabled={completing === task.id}
                  title={`Complete ${task.name}`}
                >
                  Complete {task.name}
                </button>
              </div>
            ))}
          </div>
        ) : currentModel?.activeProcessInstanceId ? (
          <p className="workflow-muted">
            No active tasks are currently available for the selected workflow instance.
          </p>
        ) : null}
      </section>
    </aside>
  );
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
    <div className="workflow-modal-backdrop" onClick={onClose}>
      <div
        className="workflow-modal"
        role="dialog"
        aria-modal="true"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="workflow-modal-header">
          <div>
            <h2>Create Workflow Model</h2>
            <p className="workflow-modal-copy">
              Name the new workflow model. AutoNate will create a blank draft in the database and
              load it into the modeler.
            </p>
          </div>
          <button type="button" className="btn-close" aria-label="Close" onClick={onClose}></button>
        </div>

        <label className="workflow-field">
          <span>Workflow Name</span>
          <input
            ref={inputRef}
            className="form-control"
            value={name}
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                onCreate();
              }
            }}
          />
        </label>

        <div className="workflow-modal-actions">
          <button
            type="button"
            className="btn btn-outline-secondary"
            onClick={onClose}
            disabled={busy}
          >
            Cancel
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={onCreate}
            disabled={busy || !name.trim()}
          >
            Create
          </button>
        </div>
      </div>
    </div>
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
    <div className="workflow-modal-backdrop" onClick={onClose}>
      <div
        className="workflow-modal workflow-script-task-modal"
        role="dialog"
        aria-modal="true"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="workflow-modal-header">
          <div>
            <h2>Script Task</h2>
            <p className="workflow-modal-copy">
              Edit the selected BPMN script task. AutoNate saves the JavaScript body inline in the
              BPMN XML and validates it before save or publish.
            </p>
          </div>
          <button type="button" className="btn-close" aria-label="Close" onClick={onClose}></button>
        </div>

        <div className="workflow-script-task-meta">
          <span className="workflow-script-task-pill">{editor.id}</span>
          <span className="workflow-script-task-pill">{editor.type}</span>
        </div>

        <label className="workflow-field">
          <span>Task Name</span>
          <input
            className="form-control"
            value={editor.name}
            onChange={(e) => onChange({ ...editor, name: e.target.value })}
          />
        </label>

        <label className="workflow-field">
          <span>Script Format</span>
          <input className="form-control" value="javascript" readOnly />
        </label>

        <label className="workflow-field">
          <span>Result Variable</span>
          <input
            className="form-control"
            value={editor.resultVariable}
            onChange={(e) => onChange({ ...editor, resultVariable: e.target.value })}
          />
        </label>

        <label className="workflow-field">
          <span>Script Body</span>
          <textarea
            className="form-control workflow-script-task-editor"
            rows={12}
            spellCheck={false}
            value={editor.script}
            onChange={(e) => onChange({ ...editor, script: e.target.value })}
          />
        </label>

        <div className="workflow-modal-actions">
          <button type="button" className="btn btn-outline-secondary" onClick={onClose}>
            Close
          </button>
          <button type="button" className="btn btn-primary" onClick={onApply} disabled={disabled}>
            Apply
          </button>
        </div>
      </div>
    </div>
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

  // Merge static catalog entries (events Flowable / future publishers raise)
  // with dynamic registrations (event types other workflows are listening for)
  // so the user can pick anything the system knows about, plus type free-form.
  const knownEvents = useMemo(() => {
    const entries = new Map<string, { topic: string; eventType: string; description?: string }>();
    for (const category of catalog?.categories ?? []) {
      for (const evt of category.events) {
        entries.set(`${evt.topic} ${evt.eventType}`, {
          topic: evt.topic,
          eventType: evt.eventType,
          description: evt.summary
        });
      }
    }
    for (const reg of catalog?.workflowRegistrations ?? []) {
      const key = `${reg.topic} ${reg.eventType}`;
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
    <div className="workflow-modal-backdrop" onClick={onClose}>
      <div
        className="workflow-modal"
        role="dialog"
        aria-modal="true"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="workflow-modal-header">
          <div>
            <h2>Signal Start Event</h2>
            <p className="workflow-modal-copy">
              Configure a Dapr pub/sub event that starts this workflow. AutoNate listens on the
              configured Topic and starts a new instance when an incoming message's{" "}
              <code>eventType</code> field matches the Event Type. The full payload is exposed to the
              workflow as a process variable named <code>eventData</code> (a JSON string —
              <code> JSON.parse(eventData)</code> in script tasks).
            </p>
          </div>
          <button type="button" className="btn-close" aria-label="Close" onClick={onClose}></button>
        </div>

        <div className="workflow-script-task-meta">
          <span className="workflow-script-task-pill">{editor.id}</span>
          <span className="workflow-script-task-pill">{editor.type}</span>
        </div>

        <label className="workflow-field">
          <span>Event Name (optional)</span>
          <input
            className="form-control"
            value={editor.name}
            onChange={(e) => onChange({ ...editor, name: e.target.value })}
            placeholder="Order placed"
          />
        </label>

        <label className="workflow-field">
          <span>Topic</span>
          <input
            className="form-control"
            list={topicListId}
            value={editor.signalTopic}
            onChange={(e) => onChange({ ...editor, signalTopic: e.target.value })}
            placeholder={DEFAULT_SIGNAL_TOPIC}
          />
          <datalist id={topicListId}>
            {knownTopics.map((topic) => (
              <option key={topic} value={topic} />
            ))}
          </datalist>
          <p className="workflow-modal-note">
            Dapr pub/sub topic. Defaults to <code>{DEFAULT_SIGNAL_TOPIC}</code> when blank. Adding a
            new topic requires a Dapr sidecar restart for messages to flow.
          </p>
        </label>

        <label className="workflow-field">
          <span>Event Type</span>
          <input
            className="form-control"
            list={eventTypeListId}
            value={editor.signalName}
            onChange={(e) => onChange({ ...editor, signalName: e.target.value })}
            placeholder="OrderPlaced"
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
          <p className="workflow-modal-note">
            Matched verbatim against the top-level <code>eventType</code> field of incoming
            messages. Required.
          </p>
        </label>

        <div className="workflow-modal-actions">
          <button type="button" className="btn btn-outline-secondary" onClick={onClose}>
            Close
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={onApply}
            disabled={disabled || missingEventType}
          >
            Apply
          </button>
        </div>
      </div>
    </div>
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
    <div className="workflow-modal-backdrop" onClick={onClose}>
      <div
        className="workflow-modal"
        role="dialog"
        aria-modal="true"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="workflow-modal-header">
          <div>
            <h2>Timer Start Event</h2>
            <p className="workflow-modal-copy">
              Schedule this workflow with an Outlook-style recurrence picker. Times use the Flowable
              engine's timezone (UTC by default) — pick the time as it should fire on the server.
            </p>
          </div>
          <button type="button" className="btn-close" aria-label="Close" onClick={onClose}></button>
        </div>

        <div className="workflow-script-task-meta">
          <span className="workflow-script-task-pill">{editor.id}</span>
          <span className="workflow-script-task-pill">{editor.type}</span>
        </div>

        {editor.parseError && (
          <div className="alert alert-warning" role="alert">
            {editor.parseError}
          </div>
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

        <div className="workflow-modal-actions">
          <button type="button" className="btn btn-outline-secondary" onClick={onClose}>
            Close
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={onApply}
            disabled={applyDisabled}
          >
            Apply
          </button>
        </div>
      </div>
    </div>
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
    <div className="workflow-modal-backdrop" onClick={onClose}>
      <div
        className="workflow-modal"
        role="dialog"
        aria-modal="true"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="workflow-modal-header">
          <div>
            <h2>Timer Intermediate Catch Event</h2>
            <p className="workflow-modal-copy">
              Pause the workflow until either a fixed delay has elapsed since this node was reached
              or a specific date/time arrives. Use a literal ISO 8601 value, or a Flowable
              expression like <code>${"{execution.getVariable('reminderDate')}"}</code> to compute
              it from process variables at runtime.
            </p>
          </div>
          <button type="button" className="btn-close" aria-label="Close" onClick={onClose}></button>
        </div>

        <div className="workflow-script-task-meta">
          <span className="workflow-script-task-pill">{editor.id}</span>
          <span className="workflow-script-task-pill">{editor.type}</span>
        </div>

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

        <div className="workflow-modal-actions">
          <button type="button" className="btn btn-outline-secondary" onClick={onClose}>
            Close
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={onApply}
            disabled={disabled || activeValueEmpty}
          >
            Apply
          </button>
        </div>
      </div>
    </div>
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
    <div className="workflow-modal-backdrop" onClick={onClose}>
      <div
        className="workflow-modal"
        role="dialog"
        aria-modal="true"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="workflow-modal-header">
          <div>
            <h2>Service Task</h2>
            <p className="workflow-modal-copy">
              Run a predefined AutoNate routine when the workflow reaches this step. The behavior
              receives every process variable plus execution metadata, and may write process
              variables back for downstream steps to branch on.
            </p>
          </div>
          <button type="button" className="btn-close" aria-label="Close" onClick={onClose}></button>
        </div>

        <div className="workflow-script-task-meta">
          <span className="workflow-script-task-pill">{editor.id}</span>
          <span className="workflow-script-task-pill">{editor.type}</span>
        </div>

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

        <div className="workflow-modal-actions">
          <button type="button" className="btn btn-outline-secondary" onClick={onClose}>
            Close
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={onApply}
            disabled={disabled || !editor.behaviorKey.trim()}
          >
            Apply
          </button>
        </div>
      </div>
    </div>
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
    <div className="workflow-modal-backdrop" onClick={onClose}>
      <div
        className="workflow-modal workflow-sequence-flow-modal"
        role="dialog"
        aria-modal="true"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="workflow-modal-header">
          <div>
            <h2>Sequence Flow</h2>
            <p className="workflow-modal-copy">
              Edit the selected path leaving a task or gateway. Use a Flowable expression like{" "}
              <code>${"{needsApproval}"}</code> or <code>${"{riskLevel == 'high'}"}</code> to route
              decisions based on process variables.
            </p>
          </div>
          <button type="button" className="btn-close" aria-label="Close" onClick={onClose}></button>
        </div>

        <div className="workflow-script-task-meta">
          <span className="workflow-script-task-pill">{editor.id}</span>
          <span className="workflow-script-task-pill">{editor.type}</span>
        </div>

        <label className="workflow-field">
          <span>Flow Name</span>
          <input
            className="form-control"
            value={editor.name}
            onChange={(e) => onChange({ ...editor, name: e.target.value })}
          />
        </label>

        <label className="workflow-field">
          <span>Condition Expression</span>
          <textarea
            className="form-control workflow-expression-editor"
            rows={6}
            spellCheck={false}
            value={editor.conditionExpression}
            onChange={(e) => onChange({ ...editor, conditionExpression: e.target.value })}
          />
        </label>

        <p className="workflow-modal-note">
          Leave the condition blank for an unconditional path. For exclusive gateways, put the
          condition on the outgoing branch itself, not on the gateway node.
        </p>

        <div className="workflow-modal-actions">
          <button type="button" className="btn btn-outline-secondary" onClick={onClose}>
            Close
          </button>
          <button type="button" className="btn btn-primary" onClick={onApply} disabled={disabled}>
            Apply
          </button>
        </div>
      </div>
    </div>
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
    <div className="workflow-modal-backdrop" onClick={onClose}>
      <div
        className="workflow-modal workflow-user-task-modal"
        role="dialog"
        aria-modal="true"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="workflow-modal-header">
          <div>
            <h2>User Task</h2>
            <p className="workflow-modal-copy">
              Edit the selected user task. Pick assignees from the directory or supply a Flowable
              expression like <code>${"{initiator}"}</code> that resolves at runtime.
            </p>
          </div>
          <button type="button" className="btn-close" aria-label="Close" onClick={onClose}></button>
        </div>

        <div className="workflow-script-task-meta">
          <span className="workflow-script-task-pill">{editor.id}</span>
          <span className="workflow-script-task-pill">{editor.type}</span>
        </div>

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

        <div className="workflow-modal-actions">
          <button type="button" className="btn btn-outline-secondary" onClick={onClose}>
            Close
          </button>
          <button type="button" className="btn btn-primary" onClick={onApply} disabled={disabled}>
            Apply
          </button>
        </div>
      </div>
    </div>
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
    items: ["Exclusive Gateway (XOR)"]
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
    items: [
      "Parallel Gateway (AND)",
      "Inclusive Gateway (OR)",
      "Event-Based Gateway",
      "Complex Gateway"
    ]
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
    <div className="workflow-modal-backdrop" onClick={onClose}>
      <div
        className="workflow-modal workflow-bpmn-types-modal"
        role="dialog"
        aria-modal="true"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="workflow-modal-header">
          <div>
            <h2>Supported BPMN Types</h2>
            <p className="workflow-modal-copy">
              The full set of BPMN 2.0 node types the AutoNate workflow studio can model and execute
              today, alongside what is on the roadmap.
            </p>
          </div>
          <button type="button" className="btn-close" aria-label="Close" onClick={onClose}></button>
        </div>

        <div className="workflow-bpmn-types-grid">
          <section className="workflow-bpmn-types-column workflow-bpmn-types-column-supported">
            <header className="workflow-bpmn-types-column-header">
              <h3>
                <i className="bi bi-check-circle-fill" aria-hidden="true"></i>
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
                <i className="bi bi-hourglass-split" aria-hidden="true"></i>
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

        <div className="workflow-modal-actions">
          <button type="button" className="btn btn-primary" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
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
