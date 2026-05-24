import { api } from "@/api/client";
import type { AqlDataType } from "@/api/aql";

export type AqlAggregate = {
  name: string;
  requiresArgument: boolean;
};

export type AqlRowFunction = {
  name: string;
  acceptsArgument: boolean;
  dataType: AqlDataType;
  // Closed-set argument vocabulary, when the entity publishes one
  // (Flows.CURRENTSTEP returns Name, Assignee, ActivityId, ...).
  // Empty array means the entity hasn't enumerated args — either the
  // function is zero-arg or any identifier is allowed.
  arguments: string[];
};

export type AqlColumnMeta = {
  name: string;
  dataType: AqlDataType;
  isAggregable: boolean;
  isSystem: boolean;
};

export type AqlEntityMeta = {
  name: string;
  staticColumns: AqlColumnMeta[];
  allowedWhereFunctions: string[];
  rowFunctions: AqlRowFunction[];
  hasDynamicFields: boolean;
  recordTypeFilterField: string | null;
};

export type AqlSchema = {
  clauseKeywords: string[];
  globalAggregates: AqlAggregate[];
  whereFunctions: string[];
  operatorsByDataType: Record<AqlDataType, string[]>;
  relativeDateUnits: string[];
  entities: AqlEntityMeta[];
};

export type AqlValueCompletions = {
  values: string[];
  closedSet: boolean;
};

export type AqlEntityContext = {
  entity: string;
  resolvedRecordType: string | null;
  columns: AqlColumnMeta[];
  valueCompletions: Record<string, AqlValueCompletions>;
};

export async function fetchAqlSchema(signal?: AbortSignal): Promise<AqlSchema> {
  const { data } = await api.get<AqlSchema>("/api/aql/schema", { signal });
  return data;
}

export async function fetchAqlEntityContext(
  entity: string,
  recordType?: string | null,
  signal?: AbortSignal
): Promise<AqlEntityContext> {
  const params: Record<string, string> = { name: entity };
  if (recordType) params.recordType = recordType;
  const { data } = await api.get<AqlEntityContext>("/api/aql/schema/entity", {
    params,
    signal
  });
  return data;
}
