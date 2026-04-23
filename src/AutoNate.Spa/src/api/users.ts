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
