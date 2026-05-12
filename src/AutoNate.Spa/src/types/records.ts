export type FieldDataType =
  | "text"
  | "number"
  | "date"
  | "phone"
  | "email"
  | "option"
  | "boolean"
  | string; // string fallback so new server-side types don't break the client

export type FieldTypeMetadata = {
  dataType: FieldDataType;
};

export type RecordType = {
  id: string;
  shortCode: string;
  name: string;
  description: string | null;
  icon: string | null;
  color: string | null;
  isSystem: boolean;
  isArchived: boolean;
  nextKeyNumber: number;
  createdAtUtc: string;
  createdBy: string;
  updatedAtUtc: string;
  updatedBy: string;
  // Populated by the list endpoint only; single-resource fetches return 0.
  fieldCount: number;
};

export type RecordTypeField = {
  id: string;
  recordTypeId: string;
  fieldKey: string;
  displayName: string;
  dataType: FieldDataType;
  config: Record<string, unknown>;
  isRequired: boolean;
  isArchived: boolean;
  sortOrder: number;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type RecordTypeAuditEntry = {
  id: number;
  recordTypeId: string;
  changeKind: string;
  before: unknown | null;
  after: unknown | null;
  changedBy: string;
  changedAtUtc: string;
};

export type CreateRecordTypeRequest = {
  shortCode: string;
  name: string;
  description: string | null;
  icon: string | null;
  color: string | null;
};

export type UpdateRecordTypeRequest = {
  name: string;
  description: string | null;
  icon: string | null;
  color: string | null;
};

export type CreateFieldRequest = {
  fieldKey: string;
  displayName: string;
  dataType: FieldDataType;
  config: Record<string, unknown>;
  isRequired: boolean;
  sortOrder: number;
};

export type UpdateFieldRequest = {
  displayName: string;
  config: Record<string, unknown>;
  isRequired: boolean;
  sortOrder: number;
};

// Config shapes for built-in field types. Keep these loose — new types will
// be added server-side and the UI tolerates unknown shapes as `Record<string, unknown>`.
export type TextFieldConfig = { variant: "single" | "multi"; maxLength: number };
export type NumberFieldConfig = {
  variant: "integer" | "decimal";
  precision: number;
  min: number | null;
  max: number | null;
};
export type DateFieldConfig = { variant: "date" | "datetime" | "range" };
export type PhoneFieldConfig = { region: string };
export type OptionChoice = { value: string; label: string };
export type OptionFieldConfig = { multi: boolean; choices: OptionChoice[] };

// ---- Records ----

export type RecordModel = {
  id: string;
  recordTypeId: string;
  key: string;
  keyNumber: number;
  name: string;
  status: string | null;
  // ISO-8601 calendar date, no time component, e.g. "2026-06-15".
  dueDate: string | null;
  assigneeIds: string[];
  values: Record<string, unknown>;
  isArchived: boolean;
  createdAtUtc: string;
  createdBy: string;
  updatedAtUtc: string;
  updatedBy: string;
};

export type RecordPage = {
  items: RecordModel[];
  totalCount: number;
  page: number;
  pageSize: number;
};

export type CreateRecordRequest = {
  recordTypeId: string;
  name: string;
  status: string | null;
  dueDate: string | null;
  values: Record<string, unknown>;
  assigneeIds: string[] | null;
};

// PATCH semantics: omit a key to leave it untouched; pass `null` to clear it.
// JSON.stringify drops `undefined` keys so `status: undefined` becomes "absent"
// on the wire, which the backend treats as "don't touch".
export type UpdateRecordRequest = {
  name?: string;
  status?: string | null;
  dueDate?: string | null;
  values?: Record<string, unknown>;
  assigneeIds?: string[];
};

export type FilterOperatorWire =
  | "eq"
  | "neq"
  | "gt"
  | "gte"
  | "lt"
  | "lte"
  | "contains"
  | "in";

export type SearchFilterClause = {
  fieldKey: string;
  op: FilterOperatorWire;
  value: unknown;
};

export type SearchRecordsRequest = {
  recordTypeId: string;
  filters?: SearchFilterClause[];
  assigneeId?: string | null;
  includeArchived: boolean;
  page: number;
  pageSize: number;
  sort?: string;
  // Free-text search across the built-in key/name/status columns.
  search?: string;
};

export type RecordHistoryEntry = {
  id: number;
  recordId: string;
  changeSetId: string | null;
  changeKind: string;
  fieldKey: string | null;
  oldValue: unknown | null;
  newValue: unknown | null;
  changedBy: string;
  changedAtUtc: string;
};

// ---- Edges ----

export type EdgeCardinality =
  | "one_to_one"
  | "one_to_many"
  | "many_to_one"
  | "many_to_many";

export type EdgeType = {
  id: string;
  shortCode: string;
  name: string;
  inverseName: string | null;
  isDirected: boolean;
  allowSelfReference: boolean;
  cardinality: EdgeCardinality;
  fromRecordTypeIds: string[] | null;
  toRecordTypeIds: string[] | null;
  isArchived: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type EdgeTypeField = {
  id: string;
  edgeTypeId: string;
  fieldKey: string;
  displayName: string;
  dataType: FieldDataType;
  config: Record<string, unknown>;
  isRequired: boolean;
  sortOrder: number;
};

export type Edge = {
  id: string;
  edgeTypeId: string;
  fromRecordId: string;
  toRecordId: string;
  data: Record<string, unknown>;
  createdAtUtc: string;
  createdBy: string;
};

export type CreateEdgeTypeRequest = {
  shortCode: string;
  name: string;
  inverseName: string | null;
  isDirected: boolean;
  allowSelfReference: boolean;
  cardinality: EdgeCardinality;
  fromRecordTypeIds: string[] | null;
  toRecordTypeIds: string[] | null;
};

export type UpdateEdgeTypeRequest = Omit<CreateEdgeTypeRequest, "shortCode">;

export type CreateEdgeFieldRequest = {
  fieldKey: string;
  displayName: string;
  dataType: FieldDataType;
  config: Record<string, unknown>;
  isRequired: boolean;
  sortOrder: number;
};

export type UpdateEdgeFieldRequest = Omit<CreateEdgeFieldRequest, "fieldKey" | "dataType">;

export type CreateEdgeRequest = {
  edgeTypeId: string;
  fromRecordId: string;
  toRecordId: string;
  data: Record<string, unknown>;
};

export type EdgeDirection = "outgoing" | "incoming" | "both";

// ---- Comments ----

export type RecordCommentModel = {
  id: string;
  recordId: string;
  authorId: string;
  body: string;
  createdAtUtc: string;
  bodyUpdatedAtUtc: string;
  isEdited: boolean;
  isDeleted: boolean;
  deletedAtUtc: string | null;
  deletedBy: string | null;
};

export type RecordCommentRevisionModel = {
  id: number;
  commentId: string;
  body: string;
  replacedAtUtc: string;
  replacedBy: string;
};
