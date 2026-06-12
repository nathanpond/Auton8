import { useEffect, useMemo } from "react";
import { Accordion, Alert, NumberInput, Select, Stack, Text, TextInput } from "@mantine/core";
import { useQuery } from "@tanstack/react-query";
import { useRecordTypeFields } from "@/hooks/useRecordTypes";
import { useSavedQueries } from "@/hooks/useSavedQueries";
import { executeQuery, type AqlQueryResponse } from "@/api/aql";
import { DataSourcePicker } from "@/widgets/DataSourcePicker";
import type { WidgetConfigFormProps } from "@/widgets/registry";
import type { QuadrantChartWidgetConfig } from "./QuadrantChartWidget.config";
import {
  RECORD_BUILTIN_CATEGORY_OPTIONS,
  RECORD_BUILTIN_KEY_NUMBER,
  RECORD_CUSTOM_FIELD_PREFIX
} from "./QuadrantChartWidget";

const NO_SELECTION_SENTINEL = "__none__";

export function QuadrantChartConfigForm({
  value,
  onChange,
  errors
}: WidgetConfigFormProps<QuadrantChartWidgetConfig>) {
  const sourceType = value.dataSource.type;
  const isRecords = sourceType === "records";
  const isWorkflows = sourceType === "workflows";
  const isSavedQuery = sourceType === "savedQuery";
  const isAdHocAql = sourceType === "adHocAql";
  const isAqlSource = isSavedQuery || isAdHocAql;

  const recordTypeId = isRecords ? value.dataSource.recordTypeId : "";
  const fieldsQuery = useRecordTypeFields(recordTypeId || null);

  // Records: build numeric and categorical option lists from the record
  // type's fields. The keyNumber built-in is always numeric. Custom
  // fields with dataType === "number" join the numeric list; everything
  // else joins the categorical list.
  const recordNumericOptions = useMemo(() => {
    const groups: { group: string; items: { value: string; label: string }[] }[] = [
      {
        group: "Built-in",
        items: [{ value: RECORD_BUILTIN_KEY_NUMBER, label: "Key number" }]
      }
    ];
    const fields = (fieldsQuery.data ?? [])
      .filter((f) => !f.isArchived && f.dataType === "number")
      .sort((a, b) => a.sortOrder - b.sortOrder || a.displayName.localeCompare(b.displayName));
    if (fields.length > 0) {
      groups.push({
        group: "Custom numeric fields",
        items: fields.map((f) => ({
          value: `${RECORD_CUSTOM_FIELD_PREFIX}${f.fieldKey}`,
          label: f.displayName
        }))
      });
    }
    return groups;
  }, [fieldsQuery.data]);

  const recordCategoryOptions = useMemo(() => {
    const groups: { group: string; items: { value: string; label: string }[] }[] = [
      { group: "Built-in", items: RECORD_BUILTIN_CATEGORY_OPTIONS.map((o) => ({ ...o })) }
    ];
    const fields = (fieldsQuery.data ?? [])
      .filter((f) => !f.isArchived && f.dataType !== "number")
      .sort((a, b) => a.sortOrder - b.sortOrder || a.displayName.localeCompare(b.displayName));
    if (fields.length > 0) {
      groups.push({
        group: "Custom fields",
        items: fields.map((f) => ({
          value: `${RECORD_CUSTOM_FIELD_PREFIX}${f.fieldKey}`,
          label: f.displayName
        }))
      });
    }
    return groups;
  }, [fieldsQuery.data]);

  // AQL column probing — same hook used by MantineChartConfigForm so the
  // result cache is shared. Keys distinguish the quadrant probe from the
  // bar/line/etc probe because the user might be configuring multiple
  // chart widgets against the same saved query in one session.
  const savedQueriesQuery = useSavedQueries();
  const savedQueryId = isSavedQuery ? value.dataSource.savedQueryId : "";
  const savedQuery = useMemo(
    () => (savedQueriesQuery.data ?? []).find((q) => q.id === savedQueryId) ?? null,
    [savedQueriesQuery.data, savedQueryId]
  );
  const aqlQueryText = isSavedQuery
    ? (savedQuery?.queryText ?? "")
    : isAdHocAql
      ? value.dataSource.adHocAqlQuery
      : "";
  const probeQueryKey = isSavedQuery
    ? ["widget", "saved-query-quadrant", savedQueryId, aqlQueryText]
    : ["widget", "ad-hoc-aql-quadrant", aqlQueryText];
  const probeQuery = useQuery<AqlQueryResponse>({
    queryKey: probeQueryKey,
    queryFn: ({ signal }) => executeQuery(aqlQueryText, signal),
    enabled: isAqlSource && Boolean(aqlQueryText) && (isAdHocAql || Boolean(savedQueryId)),
    staleTime: 30_000
  });

  const aqlColumns = probeQuery.data?.columns ?? [];
  const aqlNumericOptions = useMemo(
    () => aqlColumns.filter((c) => c.dataType === "number").map((c) => ({ value: c.name, label: c.name })),
    [aqlColumns]
  );
  const aqlAllColumnOptions = useMemo(
    () => aqlColumns.map((c) => ({ value: c.name, label: c.name })),
    [aqlColumns]
  );

  // Auto-seed X/Y when the AQL probe first resolves and the user hasn't
  // chosen anything yet. Picks the first two numeric columns. Mirrors the
  // label-column seed pattern in MantineChartConfigForm.
  useEffect(() => {
    if (!isAqlSource) return;
    if (aqlNumericOptions.length < 2) return;
    if (value.xAxisColumn || value.yAxisColumn) return;
    onChange({
      ...value,
      xAxisColumn: aqlNumericOptions[0].value,
      yAxisColumn: aqlNumericOptions[1].value
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isAqlSource, aqlNumericOptions]);

  // Render the selected value for a Select that uses our "none" sentinel
  // so optional-empty round-trips through Mantine's controlled state.
  const selectedOr = (v: string) => v || NO_SELECTION_SENTINEL;

  // Pick the right option lists based on the current source. For records
  // with no record type chosen, all the option lists are still safe (just
  // the built-in slot) so we render the selects either way.
  const numericOptions = isRecords ? recordNumericOptions : isAqlSource ? aqlNumericOptions : [];
  const categoryOptions = isRecords ? recordCategoryOptions : isAqlSource ? aqlAllColumnOptions : [];

  const numericDescription = isRecords
    ? !recordTypeId
      ? "Pick a record type to see numeric fields."
      : fieldsQuery.isLoading
        ? "Loading fields…"
        : "Numeric record field or the built-in key number."
    : isAqlSource
      ? !aqlQueryText
        ? isSavedQuery
          ? "Pick a saved query first."
          : "Write an AQL query first."
        : probeQuery.isLoading
          ? "Loading query columns…"
          : "Numeric column from the query result."
      : undefined;

  const numericDisabled =
    (isRecords && (!recordTypeId || fieldsQuery.isLoading)) ||
    (isAqlSource && (!aqlQueryText || probeQuery.isLoading));

  return (
    <Stack gap="sm">
      <DataSourcePicker
        value={value.dataSource}
        onChange={(next) => onChange({ ...value, dataSource: next })}
        errors={{
          type: errors["dataSource.type"],
          recordTypeId: errors["dataSource.recordTypeId"],
          workflowModelId: errors["dataSource.workflowModelId"],
          savedQueryId: errors["dataSource.savedQueryId"],
          adHocAqlQuery: errors["dataSource.adHocAqlQuery"]
        }}
      />

      {isWorkflows && (
        <Alert color="yellow" variant="light" title="Quadrant chart needs numeric data">
          Workflow executions don’t expose numeric fields directly. Use the <strong>Saved Query</strong> or
          <strong> Ad-hoc AQL</strong> source with a query that aggregates per workflow (e.g. average duration,
          success rate) and plot those columns.
        </Alert>
      )}

      {!isWorkflows && (
        <>
          <Select
            label="X axis column"
            description={numericDescription}
            data={numericOptions as never}
            value={value.xAxisColumn || null}
            onChange={(v) => onChange({ ...value, xAxisColumn: v ?? "" })}
            allowDeselect={false}
            searchable
            disabled={numericDisabled}
            error={errors.xAxisColumn}
            comboboxProps={{ zIndex: 1080 }}
          />
          <Select
            label="Y axis column"
            description={numericDescription}
            data={numericOptions as never}
            value={value.yAxisColumn || null}
            onChange={(v) => onChange({ ...value, yAxisColumn: v ?? "" })}
            allowDeselect={false}
            searchable
            disabled={numericDisabled}
            error={errors.yAxisColumn}
            comboboxProps={{ zIndex: 1080 }}
          />
          <Select
            label="Size column (optional)"
            description="Numeric column used to scale point radius (3rd dimension)."
            data={
              [
                { value: NO_SELECTION_SENTINEL, label: "None — uniform points" },
                ...((numericOptions as unknown) as Array<
                  { value: string; label: string } | { group: string; items: { value: string; label: string }[] }
                >)
              ] as never
            }
            value={selectedOr(value.sizeColumn)}
            onChange={(v) =>
              onChange({ ...value, sizeColumn: v === NO_SELECTION_SENTINEL ? "" : (v ?? "") })
            }
            allowDeselect={false}
            searchable
            disabled={numericDisabled}
            comboboxProps={{ zIndex: 1080 }}
          />
          <Select
            label="Category column (optional)"
            description="Categorical column used to color points and show a legend."
            data={
              [
                { value: NO_SELECTION_SENTINEL, label: "None — single color" },
                ...((categoryOptions as unknown) as Array<
                  { value: string; label: string } | { group: string; items: { value: string; label: string }[] }
                >)
              ] as never
            }
            value={selectedOr(value.categoryColumn)}
            onChange={(v) =>
              onChange({ ...value, categoryColumn: v === NO_SELECTION_SENTINEL ? "" : (v ?? "") })
            }
            allowDeselect={false}
            searchable
            disabled={numericDisabled}
            comboboxProps={{ zIndex: 1080 }}
          />
          {isAqlSource && probeQuery.isError && (
            <Text c="red" size="xs">
              Could not load query columns — the query failed to execute.
            </Text>
          )}
        </>
      )}

      <Accordion variant="separated" defaultValue={null}>
        <Accordion.Item value="quadrant-labels">
          <Accordion.Control>Quadrant labels & midpoints</Accordion.Control>
          <Accordion.Panel>
            <Stack gap="sm">
              <TextInput
                label="Top-right label"
                value={value.quadrantLabelTopRight}
                onChange={(e) => onChange({ ...value, quadrantLabelTopRight: e.currentTarget.value })}
              />
              <TextInput
                label="Top-left label"
                value={value.quadrantLabelTopLeft}
                onChange={(e) => onChange({ ...value, quadrantLabelTopLeft: e.currentTarget.value })}
              />
              <TextInput
                label="Bottom-left label"
                value={value.quadrantLabelBottomLeft}
                onChange={(e) => onChange({ ...value, quadrantLabelBottomLeft: e.currentTarget.value })}
              />
              <TextInput
                label="Bottom-right label"
                value={value.quadrantLabelBottomRight}
                onChange={(e) => onChange({ ...value, quadrantLabelBottomRight: e.currentTarget.value })}
              />
              <NumberInput
                label="X midpoint"
                description="Leave blank for median of the data."
                value={value.xMidpoint ?? ""}
                onChange={(v) =>
                  onChange({ ...value, xMidpoint: typeof v === "number" ? v : null })
                }
              />
              <NumberInput
                label="Y midpoint"
                description="Leave blank for median of the data."
                value={value.yMidpoint ?? ""}
                onChange={(v) =>
                  onChange({ ...value, yMidpoint: typeof v === "number" ? v : null })
                }
              />
              <TextInput
                label="X axis label"
                value={value.xAxisLabel}
                onChange={(e) => onChange({ ...value, xAxisLabel: e.currentTarget.value })}
              />
              <TextInput
                label="Y axis label"
                value={value.yAxisLabel}
                onChange={(e) => onChange({ ...value, yAxisLabel: e.currentTarget.value })}
              />
            </Stack>
          </Accordion.Panel>
        </Accordion.Item>
      </Accordion>

      <TextInput
        label="Series color"
        description="Mantine color token (e.g. 'teal.6', 'blue.5'). Used when no category column is set."
        value={value.seriesColor}
        onChange={(e) => onChange({ ...value, seriesColor: e.currentTarget.value })}
        error={errors.seriesColor}
      />
    </Stack>
  );
}
