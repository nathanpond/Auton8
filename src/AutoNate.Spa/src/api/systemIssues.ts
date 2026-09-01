import { api } from "./client";

export type SystemIssueState = "open" | "acknowledged" | "auto_resolved" | "resolved";

export type SystemIssueSeverity = "info" | "warning" | "error" | "critical";

export type SystemIssueCategory =
  | "data_integrity"
  | "workflow"
  | "bus"
  | "auth"
  | "config"
  | "resource"
  | "plugin"
  | "unhandled";

export type SystemIssueModel = {
  id: string;
  detectorId: string;
  category: SystemIssueCategory | string;
  severity: SystemIssueSeverity | string;
  fingerprint: string;
  title: string;
  summary: string | null;
  relatedEntityKind: string | null;
  relatedEntityId: string | null;
  factsJson: string;
  state: SystemIssueState | string;
  firstSeenAtUtc: string;
  lastSeenAtUtc: string;
  occurrenceCount: number;
  acknowledgedAtUtc: string | null;
  acknowledgedBy: string | null;
  resolvedAtUtc: string | null;
  resolutionKind: string | null;
  resolutionNotes: string | null;
  autoRemediationAttemptCount: number;
  autoRemediationLastError: string | null;
  nextRemediationAfterUtc: string | null;
};

export type SystemIssueListResponse = { items: SystemIssueModel[] };

export type SystemIssueListOptions = {
  state?: SystemIssueState | "";
  severity?: SystemIssueSeverity;
  category?: SystemIssueCategory;
  skip?: number;
  take?: number;
};

const base = "/api/system-issues";

export async function listSystemIssues(
  options: SystemIssueListOptions = {},
  signal?: AbortSignal
): Promise<SystemIssueListResponse> {
  const params: Record<string, string | number> = {};
  if (options.state !== undefined) params.state = options.state;
  if (options.severity) params.severity = options.severity;
  if (options.category) params.category = options.category;
  if (typeof options.skip === "number") params.skip = options.skip;
  if (typeof options.take === "number") params.take = options.take;
  const { data } = await api.get<SystemIssueListResponse>(base, { params, signal });
  return data;
}

export async function getSystemIssue(id: string, signal?: AbortSignal): Promise<SystemIssueModel> {
  const { data } = await api.get<SystemIssueModel>(`${base}/${id}`, { signal });
  return data;
}

export async function acknowledgeSystemIssue(id: string): Promise<SystemIssueModel> {
  const { data } = await api.post<SystemIssueModel>(`${base}/${id}/acknowledge`);
  return data;
}

export async function resolveSystemIssue(id: string, notes?: string): Promise<SystemIssueModel> {
  const { data } = await api.post<SystemIssueModel>(`${base}/${id}/resolve`, { notes: notes ?? null });
  return data;
}

export type MenuRenderFailureResponse = {
  issuesOpened: number;
  problems: string[];
};

// Per-tab dedup so the same broken row doesn't trigger a POST on every
// re-render of the nav. Backend's fingerprint dedup handles cross-session
// collisions; this set is purely a client-side throttle.
const reportedMenuItemIds = new Set<string>();

// Fire-and-forget: render path can't await, and a failed report must never
// break the page. The backend re-validates the row before opening an issue,
// so a stale render against a since-fixed row is a no-op (issuesOpened: 0).
export function reportMenuRenderFailure(menuItemId: string): void {
  if (!menuItemId || reportedMenuItemIds.has(menuItemId)) return;
  reportedMenuItemIds.add(menuItemId);
  api
    .post<MenuRenderFailureResponse>(`${base}/menu-render-failure`, { menuItemId })
    .catch(() => {
      // If the report fails (network, 401, server down), drop the dedup
      // entry so a subsequent render gets to retry.
      reportedMenuItemIds.delete(menuItemId);
    });
}

// Audit dead letters (#44). AuditOutboxDeadLetterParkRemediator moves an
// abandoned audit_outbox row here so its payload survives, but nothing read
// the table — an operator could only reach a dropped audit event with psql.
export type AuditDeadLetter = {
  id: number;
  originalOutboxId: number;
  topic: string;
  eventType: string;
  payloadJson: string;
  originalCreatedAtUtc: string;
  attemptCount: number;
  lastError: string | null;
  parkedAtUtc: string;
  parkedReason: string;
};

export type AuditDeadLetterListResponse = { items: AuditDeadLetter[]; total: number };

export async function listAuditDeadLetters(
  signal?: AbortSignal
): Promise<AuditDeadLetterListResponse> {
  const { data } = await api.get<AuditDeadLetterListResponse>(
    "/api/system-issues/dead-letters", { signal });
  return data;
}

export async function replayAuditDeadLetter(
  id: number
): Promise<{ ok: boolean; message: string; newOutboxId: number | null }> {
  const { data } = await api.post<{ ok: boolean; message: string; newOutboxId: number | null }>(
    `/api/system-issues/dead-letters/${id}/replay`);
  return data;
}
