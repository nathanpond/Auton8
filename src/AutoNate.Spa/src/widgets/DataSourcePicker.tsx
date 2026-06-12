import { useMemo } from "react";
import { Input, Select, Stack } from "@mantine/core";
import { useRecordTypes } from "@/hooks/useRecordTypes";
import { useWorkflows } from "@/hooks/useWorkflows";
import { useSavedQueries } from "@/hooks/useSavedQueries";
import type { SavedQuery } from "@/api/savedQueries";
import AqlEditor from "@/components/aql-editor/AqlEditor";
import {
  ALL_MODELS_LABEL,
  ALL_RECORDS_LABEL,
  DATA_SOURCE_TYPES,
  type DataSourceConfig,
  type DataSourceType
} from "./dataSource";

const ALL_VALUE = "__all__";

const TYPE_LABELS: Record<DataSourceType, string> = {
  records: "Records",
  workflows: "Workflows",
  savedQuery: "Saved Queries",
  adHocAql: "Ad-hoc AQL"
};

type Props = {
  value: DataSourceConfig;
  onChange: (next: DataSourceConfig) => void;
  // Optional error keyed by the same paths the widget's Zod issues use.
  errors?: {
    type?: string;
    recordTypeId?: string;
    workflowModelId?: string;
    savedQueryId?: string;
    adHocAqlQuery?: string;
  };
  // Limit the picker to a subset of types. Defaults to every registered
  // data source. Use this to hide a type from widgets that can't render it
  // (e.g. the data-table widget doesn't yet handle saved-query rows).
  allowedTypes?: readonly DataSourceType[];
};

// Cascading dropdown: first picks the data-source type, second narrows it.
// "All records" / "All models" map to an empty string on the config so the
// widget runtime can treat them as "no filter". For saved queries there is
// no "all" sentinel — the user picks an explicit query and the runtime
// renders a prompt when none is selected.
export function DataSourcePicker({ value, onChange, errors, allowedTypes }: Props) {
  const recordTypesQuery = useRecordTypes();
  const workflowsQuery = useWorkflows();
  const savedQueriesQuery = useSavedQueries();

  const recordOptions = useMemo(
    () => [
      { value: ALL_VALUE, label: ALL_RECORDS_LABEL },
      ...(recordTypesQuery.data ?? []).map((rt) => ({
        value: rt.id,
        label: rt.name
      }))
    ],
    [recordTypesQuery.data]
  );

  const workflowOptions = useMemo(
    () => [
      { value: ALL_VALUE, label: ALL_MODELS_LABEL },
      ...(workflowsQuery.data ?? []).map((m) => ({ value: m.id, label: m.name }))
    ],
    [workflowsQuery.data]
  );

  // Saved queries are grouped by ownership the same way QueryPage does it,
  // so dashboards feel consistent with the Query editor. "Shared" rows are
  // surfaced even when the actor doesn't own them.
  const savedQueryGroups = useMemo(
    () => buildSavedQueryGroups(savedQueriesQuery.data ?? []),
    [savedQueriesQuery.data]
  );

  const typeOptions = (allowedTypes ?? DATA_SOURCE_TYPES).map((t) => ({
    value: t,
    label: TYPE_LABELS[t]
  }));

  const handleTypeChange = (next: DataSourceType | null) => {
    if (!next) return;
    onChange({ ...value, type: next });
  };

  const handleRecordChange = (next: string | null) => {
    const id = !next || next === ALL_VALUE ? "" : next;
    onChange({ ...value, recordTypeId: id });
  };

  const handleWorkflowChange = (next: string | null) => {
    const id = !next || next === ALL_VALUE ? "" : next;
    onChange({ ...value, workflowModelId: id });
  };

  const handleSavedQueryChange = (next: string | null) => {
    onChange({ ...value, savedQueryId: next ?? "" });
  };

  const handleAdHocAqlChange = (next: string) => {
    onChange({ ...value, adHocAqlQuery: next });
  };

  return (
    <Stack gap="xs">
      <Select
        label="Data source"
        description="Where the widget pulls its rows from."
        data={typeOptions}
        value={value.type}
        onChange={(v) => handleTypeChange(v as DataSourceType | null)}
        allowDeselect={false}
        error={errors?.type}
        comboboxProps={{ zIndex: 1080 }}
      />
      {value.type === "records" && (
        <Select
          label="Record type"
          description={recordTypesQuery.isLoading ? "Loading…" : undefined}
          placeholder={ALL_RECORDS_LABEL}
          data={recordOptions}
          value={value.recordTypeId || ALL_VALUE}
          onChange={handleRecordChange}
          allowDeselect={false}
          error={errors?.recordTypeId}
          searchable={(recordOptions.length ?? 0) > 6}
          comboboxProps={{ zIndex: 1080 }}
        />
      )}
      {value.type === "workflows" && (
        <Select
          label="Workflow model"
          description={workflowsQuery.isLoading ? "Loading…" : undefined}
          placeholder={ALL_MODELS_LABEL}
          data={workflowOptions}
          value={value.workflowModelId || ALL_VALUE}
          onChange={handleWorkflowChange}
          allowDeselect={false}
          error={errors?.workflowModelId}
          searchable={(workflowOptions.length ?? 0) > 6}
          comboboxProps={{ zIndex: 1080 }}
        />
      )}
      {value.type === "savedQuery" && (
        <Select
          label="Saved query"
          description={
            savedQueriesQuery.isLoading
              ? "Loading…"
              : "Only queries you own or that are shared with you appear here."
          }
          placeholder="Pick a saved query…"
          data={savedQueryGroups}
          value={value.savedQueryId || null}
          onChange={handleSavedQueryChange}
          error={errors?.savedQueryId}
          searchable
          clearable
          nothingFoundMessage={
            savedQueriesQuery.isLoading
              ? "Loading…"
              : (savedQueriesQuery.data ?? []).length === 0
                ? "No saved queries yet."
                : "No matches."
          }
          comboboxProps={{ zIndex: 1080 }}
        />
      )}
      {value.type === "adHocAql" && (
        <Input.Wrapper
          label="Query"
          description="Runs each time the widget renders. Cmd/Ctrl+Enter to format."
          error={errors?.adHocAqlQuery}
        >
          <AqlEditor
            value={value.adHocAqlQuery}
            onChange={handleAdHocAqlChange}
            onExecute={() => undefined}
            minHeight="8em"
            maxHeight="16em"
            placeholder='FROM Records COLUMNS(Status, COUNT()) GROUP(Status)'
          />
        </Input.Wrapper>
      )}
    </Stack>
  );
}

type SavedQueryGroup = { group: string; items: { value: string; label: string }[] };

function buildSavedQueryGroups(rows: SavedQuery[]): SavedQueryGroup[] {
  const own = rows.filter((r) => r.isOwn).map((r) => ({ value: r.id, label: r.name }));
  const shared = rows
    .filter((r) => !r.isOwn && r.isShared)
    .map((r) => ({ value: r.id, label: r.name }));
  const groups: SavedQueryGroup[] = [];
  if (own.length > 0) groups.push({ group: "My Queries", items: own });
  if (shared.length > 0) groups.push({ group: "Shared Queries", items: shared });
  return groups;
}
