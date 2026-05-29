import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  PermissionOverrideListResponse,
  PrincipalKind,
  ResourceKind,
  createResourcePermission,
  deleteResourcePermission,
  listResourcePermissions
} from "@/api/resourcePermissions";

// React Query layer for resource-scoped permission overrides (Phase 9).

export const resourcePermissionsKey = (kind: ResourceKind, resourceId: string | null) =>
  ["content", "permissions", kind, resourceId] as const;

export function useResourcePermissions(kind: ResourceKind, resourceId: string | null) {
  return useQuery<PermissionOverrideListResponse>({
    queryKey: resourcePermissionsKey(kind, resourceId),
    enabled: resourceId != null,
    queryFn: ({ signal }) => listResourcePermissions(kind, resourceId!, signal)
  });
}

export function useCreateResourcePermission() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: {
      kind: ResourceKind;
      resourceId: string;
      principalKind: PrincipalKind;
      principalId: string;
      action: string;
    }) =>
      createResourcePermission(vars.kind, vars.resourceId, {
        principalKind: vars.principalKind,
        principalId: vars.principalId,
        action: vars.action
      }),
    onSuccess: (_grant, vars) => {
      qc.invalidateQueries({
        queryKey: resourcePermissionsKey(vars.kind, vars.resourceId)
      });
    }
  });
}

export function useDeleteResourcePermission() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { kind: ResourceKind; resourceId: string; grantId: string }) =>
      deleteResourcePermission(vars.kind, vars.resourceId, vars.grantId),
    onSuccess: (_void, vars) => {
      qc.invalidateQueries({
        queryKey: resourcePermissionsKey(vars.kind, vars.resourceId)
      });
    }
  });
}
