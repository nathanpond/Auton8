import { api } from "./client";

export type RegistryKind = {
  kind: string;
  actions: string[];
  tags: string[];
};

export async function fetchRegistry(signal?: AbortSignal): Promise<{ kinds: RegistryKind[] }> {
  const { data } = await api.get<{ kinds: RegistryKind[] }>("/api/admin/registry", { signal });
  return data;
}

export type Role = {
  id: string;
  name: string;
  description: string | null;
  isSystem: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type RoleAssignment = {
  id: string;
  roleId: string;
  principalKind: "user" | "group";
  principalId: string;
  scopeString: string | null;
  createdAtUtc: string;
};

export type Group = {
  id: string;
  name: string;
  description: string | null;
  isArchived: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type GroupMember = {
  groupId: string;
  userId: string;
  addedAtUtc: string;
  addedBy: string;
};

export type PermissionGrant = {
  id: string;
  principalKind: "user" | "group" | "role";
  principalId: string;
  action: string;
  selectorString: string;
  effect: "allow" | "deny";
  priority: number;
  createdAtUtc: string;
  updatedAtUtc: string;
};

// ---------- Roles ----------

export async function listRoles(signal?: AbortSignal): Promise<Role[]> {
  const { data } = await api.get<Role[]>("/api/admin/roles", { signal });
  return data;
}

export async function createRole(name: string, description?: string): Promise<Role> {
  const { data } = await api.post<Role>("/api/admin/roles", { name, description });
  return data;
}

export async function deleteRole(id: string): Promise<void> {
  await api.delete(`/api/admin/roles/${id}`);
}

export async function listRoleAssignments(roleId: string, signal?: AbortSignal): Promise<RoleAssignment[]> {
  const { data } = await api.get<RoleAssignment[]>(`/api/admin/roles/${roleId}/assignments`, { signal });
  return data;
}

export async function addRoleAssignment(
  roleId: string,
  body: { principalKind: "user" | "group"; principalId: string; scopeString?: string | null }
): Promise<RoleAssignment> {
  // Role assignments still target only users and groups — assigning a role
  // to another role isn't supported. Permissions on a role flow through the
  // unified permission_grants table instead.
  const { data } = await api.post<RoleAssignment>(`/api/admin/roles/${roleId}/assignments`, body);
  return data;
}

export async function revokeRoleAssignment(assignmentId: string): Promise<void> {
  await api.delete(`/api/admin/role-assignments/${assignmentId}`);
}

// ---------- Groups ----------

export async function listGroups(includeArchived = false, signal?: AbortSignal): Promise<Group[]> {
  const { data } = await api.get<Group[]>("/api/admin/groups", {
    params: { includeArchived },
    signal
  });
  return data;
}

export async function createGroup(name: string, description?: string): Promise<Group> {
  const { data } = await api.post<Group>("/api/admin/groups", { name, description });
  return data;
}

export async function deleteGroup(id: string): Promise<void> {
  await api.delete(`/api/admin/groups/${id}`);
}

export async function listGroupMembers(groupId: string, signal?: AbortSignal): Promise<GroupMember[]> {
  const { data } = await api.get<GroupMember[]>(`/api/admin/groups/${groupId}/members`, { signal });
  return data;
}

export async function addGroupMember(groupId: string, userId: string): Promise<void> {
  await api.post(`/api/admin/groups/${groupId}/members`, { userId });
}

export async function removeGroupMember(groupId: string, userId: string): Promise<void> {
  await api.delete(`/api/admin/groups/${groupId}/members/${userId}`);
}

// ---------- Direct permission grants ----------

export async function listPermissionGrants(
  filter?: { principalKind?: "user" | "group" | "role"; principalId?: string },
  signal?: AbortSignal
): Promise<PermissionGrant[]> {
  const { data } = await api.get<PermissionGrant[]>("/api/admin/grants", {
    params: filter,
    signal
  });
  return data;
}

export async function createPermissionGrant(body: {
  principalKind: "user" | "group" | "role";
  principalId: string;
  action: string;
  selectorString: string;
  effect: "allow" | "deny";
  priority: number;
}): Promise<PermissionGrant> {
  const { data } = await api.post<PermissionGrant>("/api/admin/grants", body);
  return data;
}

export async function deletePermissionGrant(id: string): Promise<void> {
  await api.delete(`/api/admin/grants/${id}`);
}

// ---------- Effective permissions debugger ----------

export type ExplainGrant = {
  principalKind: string;
  principalId: string;
  principalName: string | null;
  action: string;
  selectorString: string;
  effect: "allow" | "deny";
  matched: boolean | null;
  error: string | null;
};

export type ExplainResult = {
  effect: "allow" | "deny";
  reason: string;
  asUserId: string;
  isSuperAdmin: boolean;
  groupIds: string[];
  roleIds: string[];
  grants: ExplainGrant[];
};

export async function explainPermission(body: {
  asUserId: string;
  action: string;
  targetKind: string;
  targetId?: string | null;
}): Promise<ExplainResult> {
  const { data } = await api.post<ExplainResult>("/api/admin/explain", body);
  return data;
}
