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
  workflowModelName: string | null;
  startedAtUtc: string | null;
  lastActivityAtUtc: string | null;
  status: string;
  currentStep: string | null;
};

export type WorkflowExecutionDiagramDetail = {
  executionId: string;
  bpmnXml: string;
  completedActivityIds: string[];
  currentActivityIds: string[];
  // Populated only for cancelled executions: activities that were in flight
  // when cancellation halted the process.
  cancelledActivityIds: string[];
  variables: FlowableProcessVariable[];
};

export type FlowableTaskSummary = {
  id: string;
  name: string;
  taskDefinitionKey: string | null;
  assignee: string | null;
  processInstanceId: string | null;
  processDefinitionId: string | null;
  processDefinitionName: string | null;
  createdAtUtc: string | null;
  dueDate: string | null;
};

export type FlowableProcessInstanceSummary = {
  id: string;
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

export type WorkflowModel = {
  id: string;
  name: string;
  processKey: string;
  bpmnXml: string;
  isDraft: boolean;
  draftVersionNumber: number;
  publishedVersionNumber: number | null;
  lastDeployment: WorkflowDeploymentInfo | null;
  activeProcessInstanceId: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
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
