import { z } from "zod";

// Shared shape for "what data is this widget showing". v1 supports records
// and workflows; future iterations can add more types without breaking the
// stored config (the discriminant is `type`, and unknown values are treated
// as a misconfigured widget by the runtime).
//
// `recordTypeId` is the empty string when the user picked "All records";
// same convention for `workflowModelId`. The empty string is preferred over
// `undefined` because Zod's optional handling + Mantine's Select-clear flow
// both produce "" cleanly and we never need to distinguish "missing" from
// "all".
export const DATA_SOURCE_TYPES = ["records", "workflows"] as const;
export type DataSourceType = (typeof DATA_SOURCE_TYPES)[number];

export const dataSourceSchema = z.object({
  type: z.enum(DATA_SOURCE_TYPES),
  // Only meaningful when type === "records". Empty string = "All records".
  recordTypeId: z.string().default(""),
  // Only meaningful when type === "workflows". Empty string = "All models".
  workflowModelId: z.string().default("")
});

export type DataSourceConfig = z.infer<typeof dataSourceSchema>;

export const DEFAULT_DATA_SOURCE: DataSourceConfig = {
  type: "records",
  recordTypeId: "",
  workflowModelId: ""
};

// Human label for the "all" sentinel. Used inside DataSourcePicker and the
// runtime "what am I showing" badge.
export const ALL_RECORDS_LABEL = "All records";
export const ALL_MODELS_LABEL = "All models";
