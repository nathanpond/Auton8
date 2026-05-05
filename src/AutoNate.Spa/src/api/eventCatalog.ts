import { api } from "./client";

export type EventCatalogTransport = {
  topic: string;
  description: string;
  source: string;
};

export type EventCatalogPayloadField = {
  name: string;
  type: string;
  description: string;
};

export type EventCatalogEntry = {
  topic: string;
  eventType: string;
  summary: string;
  firesWhen: string;
  payloadHighlights: string[];
  carriesRecordType?: boolean;
};

export type EventCatalogCategory = {
  title: string;
  description: string;
  payloadFields: EventCatalogPayloadField[];
  events: EventCatalogEntry[];
};

export type EventCatalogWorkflowRegistration = {
  topic: string;
  eventType: string;
};

export type EventCatalogResponse = {
  transports: EventCatalogTransport[];
  payloadFields: EventCatalogPayloadField[];
  categories: EventCatalogCategory[];
  workflowRegistrations: EventCatalogWorkflowRegistration[];
};

export async function getEventCatalog(signal?: AbortSignal): Promise<EventCatalogResponse> {
  const { data } = await api.get<EventCatalogResponse>("/api/event-catalog", { signal });
  return data;
}
