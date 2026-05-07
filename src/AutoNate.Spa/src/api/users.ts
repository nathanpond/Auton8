import { api } from "./client";
import { LocalUser } from "@/types/flowable";

export type CreateUserRequest = {
  username: string;
  firstName: string;
  lastName: string;
  password: string;
  email?: string;
};

export type UpdateUserRequest = {
  username: string;
  firstName: string;
  lastName: string;
  email: string;
};

export async function listUsers(signal?: AbortSignal): Promise<LocalUser[]> {
  const { data } = await api.get<LocalUser[]>("/api/users", { signal });
  return data;
}

export type ListUsersPageRequest = {
  page: number;
  pageSize: number;
  search?: string;
  sort?: string;
  sortDir?: "asc" | "desc";
  status?: string;
};

export type ListUsersPageResult = {
  items: LocalUser[];
  totalCount: number;
};

export async function listUsersPage(
  req: ListUsersPageRequest,
  signal?: AbortSignal
): Promise<ListUsersPageResult> {
  const params: Record<string, string | number> = {
    page: req.page,
    pageSize: req.pageSize
  };
  if (req.search) params.q = req.search;
  if (req.sort) params.sort = req.sort;
  if (req.sortDir) params.sortDir = req.sortDir;
  if (req.status) params.status = req.status;
  const { data } = await api.get<ListUsersPageResult>("/api/users/page", { params, signal });
  return data;
}

export async function createUser(request: CreateUserRequest): Promise<LocalUser> {
  const { data } = await api.post<LocalUser>("/api/users", request);
  return data;
}

export async function updateUser(id: number, request: UpdateUserRequest): Promise<LocalUser> {
  const { data } = await api.put<LocalUser>(`/api/users/${id}`, request);
  return data;
}

export async function resetUserPassword(id: number, password: string): Promise<void> {
  await api.post(`/api/users/${id}/password`, { password });
}

export async function deleteUser(id: number): Promise<void> {
  await api.delete(`/api/users/${id}`);
}

export async function unlockUser(id: number): Promise<LocalUser> {
  const { data } = await api.post<LocalUser>(`/api/users/${id}/unlock`);
  return data;
}

export type UserSupervisorPayload = {
  userId: string;
  supervisorUserId: string | null;
};

export async function fetchUserSupervisor(userId: string, signal?: AbortSignal): Promise<UserSupervisorPayload> {
  const { data } = await api.get<UserSupervisorPayload>(`/api/users/${userId}/supervisor`, { signal });
  return data;
}

export type SupervisorPair = {
  userId: string;
  supervisorUserId: string;
};

export async function fetchSupervisorHierarchy(signal?: AbortSignal): Promise<SupervisorPair[]> {
  const { data } = await api.get<SupervisorPair[]>("/api/users/supervisors", { signal });
  return data;
}

export async function setUserSupervisor(
  userId: string,
  supervisorUserId: string | null
): Promise<void> {
  await api.put(`/api/users/${userId}/supervisor`, { supervisorUserId });
}
