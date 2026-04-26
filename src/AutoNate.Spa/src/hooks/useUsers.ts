import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  CreateUserRequest,
  UpdateUserRequest,
  UserSupervisorPayload,
  createUser,
  deleteUser,
  fetchUserSupervisor,
  listUsers,
  resetUserPassword,
  setUserSupervisor,
  updateUser
} from "@/api/users";
import { LocalUser } from "@/types/flowable";

export const USERS_QUERY_KEY = ["users"] as const;

export function useUsers() {
  return useQuery<LocalUser[]>({
    queryKey: USERS_QUERY_KEY,
    queryFn: ({ signal }) => listUsers(signal)
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
