/**
 * Hand-kept mirror of AutoNate.Web/Models/FlowableModels.cs and WorkflowModel.cs.
 * When the C# records change, update here.
 */

export type FlowableProcessVariable = {
  name: string;
  type: string | null;
  value: string | null;
};

// Mirror of AutoNate.Web.Models.ProcessVariableUpdate — write payload for the
// override path (PUT /api/executions/{id}/variables). The diagram-detail GET
// flattens typed values to a string; the SPA parses back to the runtime type
// using the GET'd `type` as a hint before sending one of these.
export type ProcessVariableUpdate = {
  name: string;
  value: unknown;
  type?: string | null;
};

export type WorkflowExecutionSummary = {
  id: string;
  // Display name set when the execution was started (e.g. "Lead Qualification (3)").
  // Null when no name was set; SPA falls back to the id.
  name: string | null;
  workflowModelName: string | null;
  startedAtUtc: string | null;
  lastActivityAtUtc: string | null;
  status: string;
  currentStep: string | null;
};

export type WorkflowExecutionDiagramDetail = {
  executionId: string;
  // Same display name as on the summary; null if the run wasn't named.
  name: string | null;
  bpmnXml: string;
  completedActivityIds: string[];
  currentActivityIds: string[];
  // Populated only for cancelled executions: activities that were in flight
  // when cancellation halted the process.
  cancelledActivityIds: string[];
  // Activities that produced a job.execution.failed event for this run.
  failedActivityIds: string[];
  variables: FlowableProcessVariable[];
};

// One row in an execution's chronological history. Mirror of
// AutoNate.Web.Models.WorkflowExecutionHistoryEvent. Sourced from Flowable's
// historic-activity-instances endpoint sorted ascending by start time.
export type WorkflowExecutionHistoryEvent = {
  activityId: string;
  activityName: string | null;
  activityType: string | null;
  startedAtUtc: string | null;
  endedAtUtc: string | null;
  durationMs: number | null;
  assignee: string | null;
  taskId: string | null;
  deleteReason: string | null;
  // Set on userTask rows when AutoNate recorded who triggered completion
  // (workflow_task_completions). Null when Flowable was completed out of
  // band (no AutoNate audit row for the task id).
  completedByUserId: string | null;
  // True when CompletedByUserId came from the override endpoint.
  isOverride: boolean | null;
  // True when at least one workflow_execution_errors row exists for this
  // activityId in this process.
  isErrored: boolean | null;
  // Latest captured error message (may be null while the Java extension
  // doesn't capture exception messages).
  errorMessage: string | null;
  // Number of recorded failures for this activity in this process.
  errorCount: number | null;
};

// One row in the Execution Log tab. Mirror of
// AutoNate.Web.Models.WorkflowExecutionLogEntry. Discriminator-driven —
// `kind` picks which of the nested objects is populated.
export type WorkflowExecutionLogKind =
  | "variable-update"
  | "task-created"
  | "task-claimed"
  | "task-completed"
  | "task-cancelled"
  | "error";

export type WorkflowExecutionLogVariableUpdate = {
  name: string;
  type: string | null;
  value: string | null;
  revision: number | null;
  taskId: string | null;
  activityInstanceId: string | null;
};

export type WorkflowExecutionLogTask = {
  taskId: string;
  name: string | null;
  taskDefinitionKey: string | null;
  assignee: string | null;
  owner: string | null;
  formKey: string | null;
  priority: number | null;
  dueAtUtc: string | null;
  deleteReason: string | null;
  // Set on task-completed entries when AutoNate recorded who actually
  // triggered the completion. Null when Flowable was completed out of
  // band (no AutoNate audit row for the task id).
  completedByUserId: string | null;
  // True when CompletedByUserId came from the override endpoint.
  isOverride: boolean | null;
};

export type WorkflowExecutionLogError = {
  activityId: string;
  activityName: string | null;
  errorMessage: string | null;
  rawFlowableEventType: string | null;
};

export type WorkflowExecutionLogEntry = {
  kind: WorkflowExecutionLogKind;
  occurredAtUtc: string | null;
  variableUpdate: WorkflowExecutionLogVariableUpdate | null;
  task: WorkflowExecutionLogTask | null;
  error: WorkflowExecutionLogError | null;
};

export type FlowableTaskSummary = {
  id: string;
  name: string;
  taskDefinitionKey: string | null;
  assignee: string | null;
  processInstanceId: string | null;
  // Per-execution display name. Null when the run wasn't named.
  processInstanceName: string | null;
  processDefinitionId: string | null;
  processDefinitionName: string | null;
  createdAtUtc: string | null;
  dueDate: string | null;
};

export type FlowableProcessInstanceSummary = {
  id: string;
  // Display name set on the run, if any.
  name: string | null;
  processDefinitionId: string;
  activityId: string | null;
  suspended: boolean;
};

export type WorkflowDeploymentInfo = {
  deploymentId: string;
  processDefinitionId: string;
  processDefinitionKey: string;
  processDefinitionVersion: number;
  deployedAtUtc: string;
};

export type WorkflowDefaultVariableType = "string" | "number" | "boolean" | "json";

export type WorkflowDefaultVariable = {
  name: string;
  type: WorkflowDefaultVariableType;
  // Stored as a JSON-typed value: string for "string"/"json", number for
  // "number", boolean for "boolean". Null when the user hasn't entered one.
  value: string | number | boolean | null;
};

export type WorkflowModel = {
  id: string;
  name: string;
  processKey: string;
  bpmnXml: string;
  isDraft: boolean;
  draftVersionNumber: number;
  publishedVersionNumber: number | null;
  lastDeployment: WorkflowDeploymentInfo | null;
  // Flowable's suspension flag for the latest published process definition.
  // Null when the workflow has not been published yet (or Flowable was
  // unreachable when the model was fetched).
  isSuspended: boolean | null;
  activeProcessInstanceId: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  defaultVariables: WorkflowDefaultVariable[] | null;
};

export type WorkflowModelVersion = {
  id: string;
  workflowModelId: string;
  versionNumber: number;
  name: string;
  processKey: string;
  bpmnXml: string;
  deployment: WorkflowDeploymentInfo;
  publishedAtUtc: string;
};

export type LocalUser = {
  id: number;
  userId: string;
  username: string;
  email: string;
  firstName: string;
  lastName: string;
  createdDate: string;
  lastLoginDate: string | null;
  idpKey: string;
  failedLoginAttempts: number;
  isLocked: boolean;
  lockedAtUtc: string | null;
};

export type AuthenticatedUser = {
  authenticated: true;
  userId: string;
  username: string;
  firstName: string;
  lastName: string;
  email: string;
  authSource: string;
  idpKey: string | null;
  isSuperAdmin: boolean;
  roles: { id: string; name: string; isSystem: boolean }[];
  groups: { id: string; name: string }[];
};

export type AnonymousUser = {
  authenticated: false;
};

export type CurrentUser = AuthenticatedUser | AnonymousUser;
