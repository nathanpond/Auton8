import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  CreateUserRequest,
  UpdateUserRequest,
  createUser,
  deleteUser,
  listUsers,
  resetUserPassword,
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
