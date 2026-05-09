import { api } from "@/api/client";
import { AgentConversation, AgentConversationDetail } from "./types";

const BASE = "/api/agent";

export async function listConversations(pageKey: string | null, signal?: AbortSignal): Promise<AgentConversation[]> {
  const res = await api.get<AgentConversation[]>(`${BASE}/conversations`, {
    params: pageKey ? { pageKey } : undefined,
    signal
  });
  return res.data;
}

export async function getConversation(id: string, signal?: AbortSignal): Promise<AgentConversationDetail> {
  const res = await api.get<AgentConversationDetail>(`${BASE}/conversations/${id}`, { signal });
  return res.data;
}

export async function createConversation(pageKey: string, connectionId?: string | null): Promise<AgentConversation> {
  const res = await api.post<AgentConversation>(`${BASE}/conversations`, { pageKey, connectionId });
  return res.data;
}

export async function renameConversation(id: string, title: string): Promise<AgentConversation> {
  const res = await api.patch<AgentConversation>(`${BASE}/conversations/${id}`, { title });
  return res.data;
}

export async function deleteConversation(id: string): Promise<void> {
  await api.delete(`${BASE}/conversations/${id}`);
}

export function sendMessageUrl(conversationId: string): string {
  return `${BASE}/conversations/${conversationId}/messages`;
}

export function pageQueryResultsUrl(conversationId: string): string {
  return `${BASE}/conversations/${conversationId}/page-query-results`;
}

export function pageActionResultsUrl(conversationId: string): string {
  return `${BASE}/conversations/${conversationId}/page-action-results`;
}
