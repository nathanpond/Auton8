import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ExplainResult,
  Group,
  GroupMember,
  PermissionGrant,
  RegistryKind,
  Role,
  RoleAssignment,
  addGroupMember,
  addRoleAssignment,
  createGroup,
  createPermissionGrant,
  createRole,
  deleteGroup,
  deletePermissionGrant,
  deleteRole,
  explainPermission,
  fetchRegistry,
  listGroupMembers,
  listGroups,
  listPermissionGrants,
  listRoleAssignments,
  listRoles,
  removeGroupMember,
  revokeRoleAssignment
} from "@/api/admin";

export const ROLES_KEY = ["admin", "roles"] as const;
export const GROUPS_KEY = ["admin", "groups"] as const;
export const REGISTRY_KEY = ["admin", "registry"] as const;

export function useRegistry() {
  return useQuery<{ kinds: RegistryKind[] }>({
    queryKey: REGISTRY_KEY,
    queryFn: ({ signal }) => fetchRegistry(signal),
    staleTime: 5 * 60 * 1000
  });
}

export function useRoles() {
  return useQuery<Role[]>({
    queryKey: ROLES_KEY,
    queryFn: ({ signal }) => listRoles(signal)
  });
}

export function useCreateRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ name, description }: { name: string; description?: string }) =>
      createRole(name, description),
    onSuccess: () => qc.invalidateQueries({ queryKey: ROLES_KEY })
  });
}

export function useDeleteRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteRole(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ROLES_KEY })
  });
}

export function useRoleAssignments(roleId: string | null) {
  return useQuery<RoleAssignment[]>({
    queryKey: ["admin", "roles", roleId, "assignments"],
    queryFn: ({ signal }) => listRoleAssignments(roleId!, signal),
    enabled: !!roleId
  });
}

export function useAddRoleAssignment() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: {
      roleId: string;
      principalKind: "user" | "group";
      principalId: string;
      scopeString?: string;
    }) =>
      addRoleAssignment(vars.roleId, {
        principalKind: vars.principalKind,
        principalId: vars.principalId,
        scopeString: vars.scopeString
      }),
    onSuccess: (_d, vars) =>
      qc.invalidateQueries({ queryKey: ["admin", "roles", vars.roleId, "assignments"] })
  });
}

export function useRevokeRoleAssignment() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ assignmentId }: { assignmentId: string; roleId: string }) =>
      revokeRoleAssignment(assignmentId),
    onSuccess: (_d, vars) =>
      qc.invalidateQueries({ queryKey: ["admin", "roles", vars.roleId, "assignments"] })
  });
}

export function useGroups(includeArchived = false) {
  return useQuery<Group[]>({
    queryKey: [...GROUPS_KEY, { includeArchived }],
    queryFn: ({ signal }) => listGroups(includeArchived, signal)
  });
}

export function useCreateGroup() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ name, description }: { name: string; description?: string }) =>
      createGroup(name, description),
    onSuccess: () => qc.invalidateQueries({ queryKey: GROUPS_KEY })
  });
}

export function useDeleteGroup() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteGroup(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: GROUPS_KEY })
  });
}

export function useGroupMembers(groupId: string | null) {
  return useQuery<GroupMember[]>({
    queryKey: ["admin", "groups", groupId, "members"],
    queryFn: ({ signal }) => listGroupMembers(groupId!, signal),
    enabled: !!groupId
  });
}

export function useAddGroupMember() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ groupId, userId }: { groupId: string; userId: string }) =>
      addGroupMember(groupId, userId),
    onSuccess: (_d, vars) =>
      qc.invalidateQueries({ queryKey: ["admin", "groups", vars.groupId, "members"] })
  });
}

export function useRemoveGroupMember() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ groupId, userId }: { groupId: string; userId: string }) =>
      removeGroupMember(groupId, userId),
    onSuccess: (_d, vars) =>
      qc.invalidateQueries({ queryKey: ["admin", "groups", vars.groupId, "members"] })
  });
}

export const GRANTS_KEY = ["admin", "grants"] as const;

export function usePermissionGrants(filter?: { principalKind?: "user" | "group" | "role"; principalId?: string }) {
  return useQuery<PermissionGrant[]>({
    queryKey: [...GRANTS_KEY, filter],
    queryFn: ({ signal }) => listPermissionGrants(filter, signal)
  });
}

export function useCreatePermissionGrant() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: {
      principalKind: "user" | "group" | "role";
      principalId: string;
      action: string;
      selectorString: string;
      effect: "allow" | "deny";
      priority: number;
    }) => createPermissionGrant(body),
    onSuccess: () => qc.invalidateQueries({ queryKey: GRANTS_KEY })
  });
}

export function useDeletePermissionGrant() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deletePermissionGrant(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: GRANTS_KEY })
  });
}

export function useExplainPermission() {
  return useMutation<
    ExplainResult,
    Error,
    { asUserId: string; action: string; targetKind: string; targetId?: string | null }
  >({
    mutationFn: (body) => explainPermission(body)
  });
}
