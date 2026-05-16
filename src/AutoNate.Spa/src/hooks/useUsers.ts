import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  CreateUserRequest,
  UpdateUserRequest,
  UserSupervisorPayload,
  createUser,
  deleteUser,
  fetchUserSupervisor,
  listUserDirectory,
  resetUserPassword,
  setUserSupervisor,
  unlockUser,
  updateUser
} from "@/api/users";
import { LocalUser } from "@/types/flowable";

export const USERS_QUERY_KEY = ["users"] as const;

// Calls the authenticated-only /api/users/directory variant so collab
// surfaces (Yjs cursor names, comment authors, project-member pickers) work
// for any project member, not just admins with User.View. Admin tables that
// need email / lock state / last-login keep calling listUsers / listUsersPage
// directly — those still hit /api/users and require User.View.
export function useUsers() {
  return useQuery<LocalUser[]>({
    queryKey: USERS_QUERY_KEY,
    queryFn: ({ signal }) => listUserDirectory(signal)
  });
}

export function useCreateUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (request: CreateUserRequest) => createUser(request),
    onSuccess: () => qc.invalidateQueries({ queryKey: USERS_QUERY_KEY })
  });
}

export function useUpdateUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, request }: { id: number; request: UpdateUserRequest }) =>
      updateUser(id, request),
    onSuccess: () => qc.invalidateQueries({ queryKey: USERS_QUERY_KEY })
  });
}

export function useResetUserPassword() {
  return useMutation({
    mutationFn: ({ id, password }: { id: number; password: string }) =>
      resetUserPassword(id, password)
  });
}

export function useDeleteUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => deleteUser(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: USERS_QUERY_KEY })
  });
}

export function useUnlockUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => unlockUser(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: USERS_QUERY_KEY })
  });
}

export const USER_SUPERVISOR_KEY = (userId: string) => ["users", userId, "supervisor"] as const;

export function useUserSupervisor(userId: string | null) {
  return useQuery<UserSupervisorPayload>({
    queryKey: USER_SUPERVISOR_KEY(userId ?? "unset"),
    queryFn: ({ signal }) => fetchUserSupervisor(userId!, signal),
    enabled: !!userId
  });
}

export function useSetUserSupervisor() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ userId, supervisorUserId }: { userId: string; supervisorUserId: string | null }) =>
      setUserSupervisor(userId, supervisorUserId),
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({ queryKey: USER_SUPERVISOR_KEY(vars.userId) });
      qc.invalidateQueries({ queryKey: ["hierarchy"] });
    }
  });
}
