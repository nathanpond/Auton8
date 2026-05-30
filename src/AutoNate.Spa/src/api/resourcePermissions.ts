import { api } from "./client";

// Resource-scoped permission overrides for documents + folders (Phase 9).
// Talks to /api/content/{documents|folders}/{id}/permissions — the
// self-service grant endpoints gated by Document.Edit / Folder.Edit. The
// backend forces selector = /{kind}/{id} and effect = "allow"; the caller
// only chooses a principal + action.

export type ResourceKind = "documents" | "folders";

export type PrincipalKind = "user" | "group" | "role";

// Mirrors the server PermissionGrant DTO (Models/Authorization/PermissionGrant).
export type PermissionGrantDto = {
  id: string;
  principalKind: PrincipalKind;
  principalId: string;
  action: string;
  selectorString: string;
  selectorAst: string;
  effect: string;
  priority: number;
  createdAtUtc: string;
  createdBy: string;
  updatedAtUtc: string;
  updatedBy: string;
};

export type PermissionOverrideListResponse = {
  items: PermissionGrantDto[];
  // Actions this caller may hand out on this resource (per-kind allowlist,
  // intersected server-side with what the caller actually holds).
  grantableActions: string[];
};

export async function listResourcePermissions(
  kind: ResourceKind,
  resourceId: string,
  signal?: AbortSignal
): Promise<PermissionOverrideListResponse> {
  const { data } = await api.get<PermissionOverrideListResponse>(
    `/api/content/${kind}/${resourceId}/permissions`,
    { signal }
  );
  return data;
}

export async function createResourcePermission(
  kind: ResourceKind,
  resourceId: string,
  req: { principalKind: PrincipalKind; principalId: string; action: string }
): Promise<PermissionGrantDto> {
  const { data } = await api.post<PermissionGrantDto>(
    `/api/content/${kind}/${resourceId}/permissions`,
    req
  );
  return data;
}

export async function deleteResourcePermission(
  kind: ResourceKind,
  resourceId: string,
  grantId: string
): Promise<void> {
  await api.delete(`/api/content/${kind}/${resourceId}/permissions/${grantId}`);
}
