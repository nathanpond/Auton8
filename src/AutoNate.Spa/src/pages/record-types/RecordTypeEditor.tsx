import { useEffect, useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import {
  useArchiveField,
  useArchiveRecordType,
  useCreateField,
  useFieldTypes,
  useRecordType,
  useRecordTypeFields,
  useRestoreField,
  useRestoreRecordType,
  useUpdateField,
  useUpdateRecordType
} from "@/hooks/useRecordTypes";
import { FieldDataType, RecordTypeField } from "@/types/records";
import IconPicker from "@/components/IconPicker";
import ColorPicker from "@/components/ColorPicker";
import FieldConfigPanel from "./FieldConfigPanel";
import SchemaAuditPanel from "./SchemaAuditPanel";
import { defaultFieldConfig, humanDataType } from "./fieldTypeDefaults";

type FieldModalState =
  | { kind: "none" }
  | { kind: "add" }
  | { kind: "edit"; field: RecordTypeField };

export default function RecordTypeEditor() {
  const { id } = useParams<{ id: string }>();
  const recordTypeId = id ?? null;

  const { data: type, isLoading } = useRecordType(recordTypeId);
  const [includeArchivedFields, setIncludeArchivedFields] = useState(false);
  const { data: fields = [] } = useRecordTypeFields(recordTypeId, includeArchivedFields);
  const { data: fieldTypes = [] } = useFieldTypes();

  const update = useUpdateRecordType(id ?? "");
  const archive = useArchiveRecordType();
  const restore = useRestoreRecordType();

  const [nameDraft, setNameDraft] = useState("");
  const [descDraft, setDescDraft] = useState("");
  const [iconDraft, setIconDraft] = useState("");
  const [colorDraft, setColorDraft] = useState("");
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(null);
  const [fieldModal, setFieldModal] = useState<FieldModalState>({ kind: "none" });

  useEffect(() => {
    if (type) {
      setNameDraft(type.name);
      setDescDraft(type.description ?? "");
      setIconDraft(type.icon ?? "");
      setColorDraft(type.color ?? "");
    }
  }, [type]);

  const dirty = useMemo(() => {
    if (!type) return false;
    return (
      nameDraft !== type.name ||
      (descDraft || null) !== (type.description ?? null) ||
      (iconDraft || null) !== (type.icon ?? null) ||
      (colorDraft || null) !== (type.color ?? null)
    );
  }, [type, nameDraft, descDraft, iconDraft, colorDraft]);

  if (isLoading || !type) {
    return (
      <>
        <div className="page-head">
          <h1 className="page-header mb-1">Record Type</h1>
        </div>
        <div className="panel panel-inverse">
          <div className="panel-body text-center text-body text-opacity-50 p-4">
            {isLoading ? "Loading..." : "Record type not found."}
          </div>
        </div>
      </>
    );
  }

  const saveDetails = async () => {
    try {
      await update.mutateAsync({
        name: nameDraft.trim(),
        description: descDraft.trim() || null,
        icon: iconDraft.trim() || null,
        color: colorDraft.trim() || null
      });
      setFlash({ kind: "success", message: "Saved." });
    } catch (err) {
      setFlash({ kind: "error", message: describeError(err) });
    }
  };

  const toggleArchived = async () => {
    try {
      if (type.isArchived) {
        await restore.mutateAsync(type.id);
        setFlash({ kind: "success", message: `Restored ${type.shortCode}.` });
      } else {
        await archive.mutateAsync(type.id);
        setFlash({ kind: "success", message: `Archived ${type.shortCode}.` });
      }
    } catch (err) {
      setFlash({ kind: "error", message: describeError(err) });
    }
  };

  return (
    <>
      <div className="page-head d-flex justify-content-between align-items-start">
        <div>
          <h1 className="page-header mb-1">
            <code className="me-2">{type.shortCode}</code>
            {type.name}
            {type.isArchived && <span className="badge bg-secondary ms-2">Archived</span>}
          </h1>
          <p className="page-head-copy mb-0">
            <Link to="/record-types">&larr; Back to record types</Link>
          </p>
        </div>
        <div className="d-flex gap-2">
          <Link to={`/records/${type.shortCode}`} className="btn btn-outline-secondary">
            <i className="fa fa-list me-2"></i>View records
          </Link>
          <button
            type="button"
            className={`btn ${type.isArchived ? "btn-outline-success" : "btn-outline-warning"}`}
            onClick={toggleArchived}
            disabled={archive.isPending || restore.isPending}
          >
            <i className={`fa ${type.isArchived ? "fa-box-open" : "fa-box-archive"} me-2`}></i>
            {type.isArchived ? "Restore" : "Archive"}
          </button>
        </div>
      </div>

      {flash && (
        <div
          className={`alert ${flash.kind === "success" ? "alert-success" : "alert-danger"}`}
          role={flash.kind === "success" ? "status" : "alert"}
        >
          {flash.message}
        </div>
      )}

      <div className="panel panel-inverse mb-3">
        <div className="panel-heading">
          <h4 className="panel-title">Details</h4>
        </div>
        <div className="panel-body">
          <div className="row g-3">
            <div className="col-md-6">
              <label className="form-label">Name</label>
              <input
                className="form-control"
                value={nameDraft}
                onChange={(e) => setNameDraft(e.target.value)}
              />
            </div>
            <div className="col-md-3">
              <label className="form-label">Icon</label>
              <IconPicker value={iconDraft} onChange={setIconDraft} />
            </div>
            <div className="col-md-3">
              <label className="form-label">Color</label>
              <ColorPicker value={colorDraft} onChange={setColorDraft} />
            </div>
            <div className="col-12">
              <label className="form-label">Description</label>
              <textarea
                className="form-control"
                rows={3}
                value={descDraft}
                onChange={(e) => setDescDraft(e.target.value)}
              />
            </div>
          </div>
          <div className="mt-3 text-end">
            <button
              type="button"
              className="btn btn-primary"
              onClick={saveDetails}
              disabled={!dirty || update.isPending}
            >
              Save details
            </button>
          </div>
        </div>
      </div>

      <div className="panel panel-inverse mb-3">
        <div className="panel-heading d-flex justify-content-between align-items-center">
          <h4 className="panel-title mb-0">Fields</h4>
          <div className="d-flex align-items-center gap-3">
            <div className="form-check form-switch mb-0">
              <input
                type="checkbox"
                className="form-check-input"
                id="include-archived-fields"
                checked={includeArchivedFields}
                onChange={(e) => setIncludeArchivedFields(e.target.checked)}
              />
              <label className="form-check-label small" htmlFor="include-archived-fields">
                Show archived
              </label>
            </div>
            <button
              type="button"
              className="btn btn-primary btn-sm"
              onClick={() => setFieldModal({ kind: "add" })}
              disabled={fieldTypes.length === 0}
            >
              <i className="fa fa-plus me-2"></i>Add field
            </button>
          </div>
        </div>
        <div className="panel-body">
          <table className="table table-striped table-bordered align-middle">
            <thead>
              <tr>
                <th style={{ width: "4rem" }}>#</th>
                <th>Field key</th>
                <th>Display name</th>
                <th style={{ width: "9rem" }}>Type</th>
                <th style={{ width: "6rem" }}>Required</th>
                <th style={{ width: "9rem" }}>Status</th>
                <th style={{ width: "6rem" }}></th>
              </tr>
            </thead>
            <tbody>
              {fields.length === 0 && (
                <tr>
                  <td colSpan={7} className="text-center text-body text-opacity-50 p-4">
                    No fields yet. Add one to start capturing data.
                  </td>
                </tr>
              )}
              {fields.map((f) => (
                <tr key={f.id} className={f.isArchived ? "text-body text-opacity-50" : undefined}>
                  <td>{f.sortOrder}</td>
                  <td>
                    <code>{f.fieldKey}</code>
                  </td>
                  <td>{f.displayName}</td>
                  <td>{humanDataType(f.dataType)}</td>
                  <td>{f.isRequired ? "Yes" : ""}</td>
                  <td>
                    {f.isArchived ? (
                      <span className="badge bg-secondary">Archived</span>
                    ) : (
                      <span className="badge bg-success">Active</span>
                    )}
                  </td>
                  <td className="text-end">
                    <button
                      type="button"
                      className="btn btn-outline-secondary btn-sm"
                      onClick={() => setFieldModal({ kind: "edit", field: f })}
                    >
                      Edit
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <div className="panel panel-inverse">
        <div className="panel-heading">
          <h4 className="panel-title">Schema change history</h4>
        </div>
        <div className="panel-body">
          <SchemaAuditPanel recordTypeId={type.id} />
        </div>
      </div>

      {fieldModal.kind !== "none" && (
        <FieldModal
          recordTypeId={type.id}
          state={fieldModal}
          dataTypes={fieldTypes.map((ft) => ft.dataType)}
          onClose={() => setFieldModal({ kind: "none" })}
          onSuccess={(message) => {
            setFlash({ kind: "success", message });
            setFieldModal({ kind: "none" });
          }}
          onError={(m) => setFlash({ kind: "error", message: m })}
        />
      )}

    </>
  );
}

function FieldModal({
  recordTypeId,
  state,
  dataTypes,
  onClose,
  onSuccess,
  onError
}: {
  recordTypeId: string;
  state: Exclude<FieldModalState, { kind: "none" }>;
  dataTypes: FieldDataType[];
  onClose: () => void;
  onSuccess: (message: string) => void;
  onError: (m: string) => void;
}) {
  const isEdit = state.kind === "edit";
  const existing = isEdit ? state.field : null;

  const [fieldKey, setFieldKey] = useState(existing?.fieldKey ?? "");
  const [displayName, setDisplayName] = useState(existing?.displayName ?? "");
  const [dataType, setDataType] = useState<FieldDataType>(
    existing?.dataType ?? (dataTypes[0] ?? "text")
  );
  const [config, setConfig] = useState<Record<string, unknown>>(
    existing?.config ?? defaultFieldConfig(dataTypes[0] ?? "text")
  );
  const [isRequired, setIsRequired] = useState(existing?.isRequired ?? false);
  const [sortOrder, setSortOrder] = useState(existing?.sortOrder ?? 0);

  const create = useCreateField(recordTypeId);
  const update = useUpdateField(recordTypeId);
  const archive = useArchiveField(recordTypeId);
  const restore = useRestoreField(recordTypeId);

  const onTypeChange = (next: FieldDataType) => {
    setDataType(next);
    if (!isEdit) {
      setConfig(defaultFieldConfig(next));
    }
  };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      if (isEdit && existing) {
        await update.mutateAsync({
          fieldId: existing.id,
          request: {
            displayName: displayName.trim(),
            config,
            isRequired,
            sortOrder
          }
        });
        onSuccess(`Updated ${existing.fieldKey}.`);
      } else {
        await create.mutateAsync({
          fieldKey: fieldKey.trim().toLowerCase(),
          displayName: displayName.trim(),
          dataType,
          config,
          isRequired,
          sortOrder
        });
        onSuccess(`Added ${fieldKey}.`);
      }
    } catch (err) {
      onError(describeError(err));
    }
  };

  const toggleArchived = async () => {
    if (!existing) return;
    try {
      if (existing.isArchived) {
        await restore.mutateAsync(existing.id);
        onSuccess(`Restored ${existing.fieldKey}.`);
      } else {
        await archive.mutateAsync(existing.id);
        onSuccess(`Archived ${existing.fieldKey}.`);
      }
    } catch (err) {
      onError(describeError(err));
    }
  };

  return (
    <>
      <div className="modal fade show d-block" role="dialog" aria-modal="true" tabIndex={-1}>
        <div className="modal-dialog modal-lg">
          <div className="modal-content">
            <form onSubmit={submit}>
              <div className="modal-header">
                <h5 className="modal-title">{isEdit ? `Edit field: ${existing?.fieldKey}` : "Add field"}</h5>
                <button type="button" className="btn-close" onClick={onClose} aria-label="Close" />
              </div>
              <div className="modal-body">
                <div className="row g-3">
                  <div className="col-md-6">
                    <label className="form-label">Field key</label>
                    <input
                      className="form-control"
                      value={fieldKey}
                      onChange={(e) => setFieldKey(e.target.value)}
                      placeholder="status"
                      required={!isEdit}
                      disabled={isEdit}
                    />
                    <div className="form-text">
                      Lowercase snake_case. Used as the stable identifier in data and filters. Cannot be changed later.
                    </div>
                  </div>
                  <div className="col-md-6">
                    <label className="form-label">Display name</label>
                    <input
                      className="form-control"
                      value={displayName}
                      onChange={(e) => setDisplayName(e.target.value)}
                      required
                    />
                  </div>
                  <div className="col-md-6">
                    <label className="form-label">Data type</label>
                    <select
                      className="form-select"
                      value={dataType}
                      onChange={(e) => onTypeChange(e.target.value as FieldDataType)}
                      disabled={isEdit}
                    >
                      {dataTypes.map((dt) => (
                        <option key={dt} value={dt}>
                          {humanDataType(dt)}
                        </option>
                      ))}
                    </select>
                    {isEdit && (
                      <div className="form-text">
                        Data type cannot change. Archive this field and add a new one instead.
                      </div>
                    )}
                  </div>
                  <div className="col-md-3">
                    <label className="form-label">Sort order</label>
                    <input
                      type="number"
                      className="form-control"
                      value={sortOrder}
                      onChange={(e) => setSortOrder(Number(e.target.value))}
                    />
                  </div>
                  <div className="col-md-3 d-flex align-items-end">
                    <div className="form-check form-switch">
                      <input
                        type="checkbox"
                        className="form-check-input"
                        id="field-required"
                        checked={isRequired}
                        onChange={(e) => setIsRequired(e.target.checked)}
                      />
                      <label className="form-check-label" htmlFor="field-required">
                        Required
                      </label>
                    </div>
                  </div>
                </div>
                <hr />
                <h6 className="mb-3">Configuration</h6>
                <FieldConfigPanel dataType={dataType} config={config} onChange={setConfig} />
              </div>
              <div className="modal-footer d-flex justify-content-between">
                <div>
                  {isEdit && existing && (
                    <button
                      type="button"
                      className={`btn ${existing.isArchived ? "btn-outline-success" : "btn-outline-warning"}`}
                      onClick={toggleArchived}
                      disabled={archive.isPending || restore.isPending}
                    >
                      {existing.isArchived ? "Restore field" : "Archive field"}
                    </button>
                  )}
                </div>
                <div>
                  <button type="button" className="btn btn-outline-secondary me-2" onClick={onClose}>
                    Cancel
                  </button>
                  <button
                    type="submit"
                    className="btn btn-primary"
                    disabled={create.isPending || update.isPending}
                  >
                    {isEdit ? "Save" : "Add field"}
                  </button>
                </div>
              </div>
            </form>
          </div>
        </div>
      </div>
      <div className="modal-backdrop fade show" />
    </>
  );
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { message?: string } } }).response;
    return response?.data?.message ?? error.message;
  }
  return String(error);
}
