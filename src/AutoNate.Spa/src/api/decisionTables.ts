import { api } from "./client";

/**
 * Decision tables (#110).
 *
 * The rules are structured data, not DMN. The XML is generated server-side at
 * publish, which is what makes a bad cell refusable at save — you cannot validate
 * cells you did not model.
 */

export type DecisionColumn = {
  id: string;
  label: string;
  /** The process variable this column reads (input) or writes (output). */
  name: string;
  typeRef: DecisionTypeRef;
};

export type DecisionTypeRef = "string" | "number" | "boolean" | "date";

export type DecisionRule = {
  id: string;
  /** One cell per input column, positionally. */
  inputEntries: string[];
  /** One cell per output column, positionally. */
  outputEntries: string[];
};

export type DecisionHitPolicy = "FIRST" | "UNIQUE" | "ANY" | "COLLECT";

export type DecisionDeploymentSummary = {
  deploymentId: string;
  decisionId: string;
  decisionKey: string;
  decisionVersion: number;
  deployedAtUtc: string;
};

export type DecisionTable = {
  id: string;
  decisionKey: string;
  name: string;
  description: string | null;
  hitPolicy: DecisionHitPolicy;
  inputs: DecisionColumn[];
  outputs: DecisionColumn[];
  rules: DecisionRule[];
  isDraft: boolean;
  draftVersionNumber: number;
  publishedVersionNumber: number | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  lastDeployment: DecisionDeploymentSummary | null;
};

export type DecisionTableVersion = {
  id: string;
  decisionTableId: string;
  versionNumber: number;
  name: string;
  decisionKey: string;
  decisionId: string;
  decisionVersion: number;
  publishedAtUtc: string;
};

export type DecisionTestResult = {
  matched: boolean;
  outputs: Record<string, unknown>[];
};

/** What the hit policies mean, in the words the editor shows an author. */
export const HIT_POLICY_HELP: Record<DecisionHitPolicy, string> = {
  FIRST: "The first matching rule wins. Later matches are ignored.",
  UNIQUE: "Exactly one rule may match. The engine fails if two do.",
  ANY: "Several rules may match, and they must all give the same output.",
  COLLECT: "Every matching rule contributes an output."
};

export const TYPE_REFS: DecisionTypeRef[] = ["string", "number", "boolean", "date"];

export async function listDecisionTables(signal?: AbortSignal): Promise<DecisionTable[]> {
  const { data } = await api.get<DecisionTable[]>("/api/decision-tables/", { signal });
  return data;
}

export async function getDecisionTable(
  id: string,
  signal?: AbortSignal
): Promise<DecisionTable> {
  const { data } = await api.get<DecisionTable>(
    `/api/decision-tables/${encodeURIComponent(id)}`,
    { signal }
  );
  return data;
}

export async function listDecisionTableVersions(
  id: string,
  signal?: AbortSignal
): Promise<DecisionTableVersion[]> {
  const { data } = await api.get<DecisionTableVersion[]>(
    `/api/decision-tables/${encodeURIComponent(id)}/versions`,
    { signal }
  );
  return data;
}

export async function createDecisionTable(table: Partial<DecisionTable>): Promise<DecisionTable> {
  const { data } = await api.post<DecisionTable>("/api/decision-tables/", table);
  return data;
}

export async function saveDecisionTable(table: DecisionTable): Promise<DecisionTable> {
  const { data } = await api.put<DecisionTable>(
    `/api/decision-tables/${encodeURIComponent(table.id)}`,
    table
  );
  return data;
}

export async function deleteDecisionTable(id: string): Promise<void> {
  await api.delete(`/api/decision-tables/${encodeURIComponent(id)}`);
}

export async function publishDecisionTable(id: string): Promise<DecisionTable> {
  const { data } = await api.post<DecisionTable>(
    `/api/decision-tables/${encodeURIComponent(id)}/publish`
  );
  return data;
}

/**
 * Evaluates the PUBLISHED table through the same path a process uses.
 *
 * Not the draft. A separate preview evaluator would let a table test correctly and
 * behave differently in a process, which is the one thing a try-it panel exists to
 * rule out — so an unpublished table is refused here rather than approximated.
 */
export async function testDecisionTable(
  id: string,
  inputs: Record<string, unknown>
): Promise<DecisionTestResult> {
  const { data } = await api.post<DecisionTestResult>(
    `/api/decision-tables/${encodeURIComponent(id)}/test`,
    { inputs }
  );
  return data;
}
