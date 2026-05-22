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
