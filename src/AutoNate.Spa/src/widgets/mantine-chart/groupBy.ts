// Group-by sentinels for the records data source. Lifted out of
// MantineChartWidget.config.ts because the ConfigForm + runtime both
// consume them, and config.ts imports the ConfigForm to register the
// widget — keeping these here breaks the cycle (TDZ on
// "Cannot access 'ASSIGNEE_COUNT_GROUP_BY' before initialization").

// Sentinel value for the derived "how many assignees" bucket.
export const ASSIGNEE_COUNT_GROUP_BY = "assigneeCount";

// Prefix on the recordGroupBy value when the user picks a custom field
// off the record type (vs. a built-in RecordModel property).
export const CUSTOM_FIELD_GROUP_BY_PREFIX = "field:";

// Drill-down: translate a (groupBy key, clicked label) pair into a
// SearchFilterClause that re-runs the records search filtered to that
// value. Returns `null` for axes that don't map cleanly to a single
// equality clause — `assigneeCount` ("2 assignees") is the only such
// case today; the chart hides drill from there.
export type RecordsFilterClause = {
  fieldKey: string;
  op: "eq";
  value: unknown;
};

export function groupByToFilterClause(
  groupBy: string,
  clickedLabel: string
): RecordsFilterClause | null {
  if (groupBy === ASSIGNEE_COUNT_GROUP_BY) return null;
  if (groupBy.startsWith(CUSTOM_FIELD_GROUP_BY_PREFIX)) {
    const fieldKey = groupBy.slice(CUSTOM_FIELD_GROUP_BY_PREFIX.length);
    // The chart renders "—" for null/empty custom-field values; clicking
    // it has no sensible equivalent filter, so swallow rather than send
    // an `eq ""` that would mismatch anything but stored empty strings.
    if (clickedLabel === "—") return null;
    return { fieldKey, op: "eq", value: clickedLabel };
  }
  switch (groupBy) {
    case "status":
      // The renderer turns null status into "—"; treat the dash as
      // "filter to null" by sending an explicit null value (the backend
      // search accepts null for nullable cols).
      return { fieldKey: "status", op: "eq", value: clickedLabel === "—" ? null : clickedLabel };
    case "name":
      return { fieldKey: "name", op: "eq", value: clickedLabel === "—" ? null : clickedLabel };
    case "key":
      return { fieldKey: "key", op: "eq", value: clickedLabel === "—" ? null : clickedLabel };
    case "dueDate":
      // Labels are either an ISO date string or "No due date".
      return {
        fieldKey: "dueDate",
        op: "eq",
        value: clickedLabel === "No due date" ? null : clickedLabel
      };
    default:
      return null;
  }
}
