import { useEffect, useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import {
  useArchiveEdgeType,
  useCreateEdgeTypeField,
  useDeleteEdgeTypeField,
  useEdgeType,
  useEdgeTypeFields,
  useRestoreEdgeType,
  useUpdateEdgeType,
  useUpdateEdgeTypeField
} from "@/hooks/useRecordEdges";
import { useFieldTypes, useRecordTypes } from "@/hooks/useRecordTypes";
import { EdgeCardinality, EdgeTypeField, FieldDataType } from "@/types/records";
import FieldConfigPanel from "../record-types/FieldConfigPanel";
import { defaultFieldConfig, humanDataType } from "../record-types/fieldTypeDefaults";

type FieldModalState =
  | { kind: "none" }
  | { kind: "add" }
  | { kind: "edit"; field: EdgeTypeField };

export default function EdgeTypeEditor() {
  const { id } = useParams<{ id: string }>();
  const edgeTypeId = id ?? null;

  const { data: type, isLoading } = useEdgeType(edgeTypeId);
  const { data: fields = [] } = useEdgeTypeFields(edgeTypeId);
  const { data: fieldTypes = [] } = useFieldTypes();
  const { data: recordTypes = [] } = useRecordTypes(false);

  const update = useUpdateEdgeType(id ?? "");
  const archive = useArchiveEdgeType();
  const restore = useRestoreEdgeType();

  const [name, setName] = useState("");
  const [inverse, setInverse] = useState("");
  const [isDirected, setIsDirected] = useState(true);
  const [allowSelfRef, setAllowSelfRef] = useState(false);
  const [cardinality, setCardinality] = useState<EdgeCardinality>("many_to_many");
  const [fromTypes, setFromTypes] = useState<string[]>([]);
  const [toTypes, setToTypes] = useState<string[]>([]);
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(null);
  const [fieldModal, setFieldModal] = useState<FieldModalState>({ kind: "none" });

  useEffect(() => {
    if (!type) return;
    setName(type.name);
    setInverse(type.inverseName ?? "");
    setIsDirected(type.isDirected);
    setAllowSelfRef(type.allowSelfReference);
    setCardinality(type.cardinality);
    setFromTypes(type.fromRecordTypeIds ?? []);
    setToTypes(type.toRecordTypeIds ?? []);
  }, [type]);

  const dirty = useMemo(() => {
    if (!type) return false;
    return (
      name !== type.name ||
      (inverse || null) !== (type.inverseName ?? null) ||
      isDirected !== type.isDirected ||
      allowSelfRef !== type.allowSelfReference ||
      cardinality !== type.cardinality ||
      !arraysEqual(fromTypes, type.fromRecordTypeIds ?? []) ||
      !arraysEqual(toTypes, type.toRecordTypeIds ?? [])
    );
  }, [type, name, inverse, isDirected, allowSelfRef, cardinality, fromTypes, toTypes]);

  if (isLoading || !type) {
    return (
      <div className="panel panel-inverse">
        <div className="panel-body p-4 text-center text-body text-opacity-50">
          {isLoading ? "Loading..." : "Edge type not found."}
        </div>
      </div>
    );
  }

  const save = async () => {
    try {
      await update.mutateAsync({
        name: name.trim(),
        inverseName: inverse.trim() || null,
        isDirected,
        allowSelfReference: allowSelfRef,
        cardinality,
        fromRecordTypeIds: fromTypes.length === 0 ? null : fromTypes,
        toRecordTypeIds: toTypes.length === 0 ? null : toTypes
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
        setFlash({ kind: "success", message: "Restored." });
      } else {
        await archive.mutateAsync(type.id);
        setFlash({ kind: "success", message: "Archived." });
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
            <Link to="/record-edge-types">&larr; Back to edge types</Link>
          </p>
        </div>
        <div>
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
          <h4 className="panel-title">Settings</h4>
        </div>
        <div className="panel-body">
          <div className="row g-3">
            <div className="col-md-6">
              <label className="form-label">Forward name</label>
              <input className="form-control" value={name} onChange={(e) => setName(e.target.value)} />
            </div>
            <div className="col-md-6">
              <label className="form-label">Inverse name</label>
              <input
                className="form-control"
                value={inverse}
                onChange={(e) => setInverse(e.target.value)}
                placeholder={isDirected ? "Optional" : "Used as the symmetric label"}
              />
            </div>
            <div className="col-md-3">
              <label className="form-label">Cardinality</label>
              <select
                className="form-select"
                value={cardinality}
                onChange={(e) => setCardinality(e.target.value as EdgeCardinality)}
              >
                <option value="many_to_many">many_to_many</option>
                <option value="one_to_one">one_to_one</option>
                <option value="one_to_many">one_to_many</option>
                <option value="many_to_one">many_to_one</option>
              </select>
            </div>
            <div className="col-md-3 d-flex align-items-end">
              <div className="form-check form-switch">
                <input
                  type="checkbox"
                  className="form-check-input"
                  id="ee-directed"
                  checked={isDirected}
                  onChange={(e) => setIsDirected(e.target.checked)}
                />
                <label className="form-check-label" htmlFor="ee-directed">
                  Directed
                </label>
              </div>
            </div>
            <div className="col-md-3 d-flex align-items-end">
              <div className="form-check form-switch">
                <input
                  type="checkbox"
                  className="form-check-input"
                  id="ee-self-ref"
                  checked={allowSelfRef}
                  onChange={(e) => setAllowSelfRef(e.target.checked)}
                />
                <label className="form-check-label" htmlFor="ee-self-ref">
                  Allow self-reference
                </label>
              </div>
            </div>
            <div className="col-md-6">
              <label className="form-label">Allowed source record types</label>
              <RecordTypeMultiSelect
                value={fromTypes}
                onChange={setFromTypes}
                options={recordTypes.map((rt) => ({ id: rt.id, label: `${rt.shortCode} - ${rt.name}` }))}
              />
              <div className="form-text">Leave empty to allow any record type as source.</div>
            </div>
            <div className="col-md-6">
              <label className="form-label">Allowed target record types</label>
              <RecordTypeMultiSelect
                value={toTypes}
                onChange={setToTypes}
                options={recordTypes.map((rt) => ({ id: rt.id, label: `${rt.shortCode} - ${rt.name}` }))}
              />
              <div className="form-text">Leave empty to allow any record type as target.</div>
            </div>
          </div>
          <div className="mt-3 text-end">
            <button
              type="button"
              className="btn btn-primary"
              onClick={save}
              disabled={!dirty || update.isPending}
            >
              Save settings
            </button>
          </div>
        </div>
      </div>

      <div className="panel panel-inverse">
        <div className="panel-heading d-flex justify-content-between align-items-center">
          <h4 className="panel-title mb-0">Edge data fields</h4>
          <button
            type="button"
            className="btn btn-primary btn-sm"
            onClick={() => setFieldModal({ kind: "add" })}
            disabled={fieldTypes.length === 0}
          >
            <i className="fa fa-plus me-2"></i>Add field
          </button>
        </div>
        <div className="panel-body">
          {fields.length === 0 && (
            <p className="text-body text-opacity-50 mb-0">
              No edge data fields. Edges of this type will only carry the source/target references.
            </p>
          )}
          {fields.length > 0 && (
            <table className="table table-striped table-bordered align-middle">
              <thead>
                <tr>
                  <th style={{ width: "4rem" }}>#</th>
                  <th>Key</th>
                  <th>Display name</th>
                  <th style={{ width: "9rem" }}>Type</th>
                  <th style={{ width: "6rem" }}>Required</th>
                  <th style={{ width: "5rem" }}></th>
                </tr>
              </thead>
              <tbody>
                {fields.map((f) => (
                  <tr key={f.id}>
                    <td>{f.sortOrder}</td>
                    <td><code>{f.fieldKey}</code></td>
                    <td>{f.displayName}</td>
                    <td>{humanDataType(f.dataType)}</td>
                    <td>{f.isRequired ? "Yes" : ""}</td>
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
          )}
        </div>
      </div>

      {fieldModal.kind !== "none" && (
        <EdgeFieldModal
          edgeTypeId={type.id}
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

function RecordTypeMultiSelect({
  value,
  onChange,
  options
}: {
  value: string[];
  onChange: (next: string[]) => void;
  options: { id: string; label: string }[];
}) {
  return (
    <div className="d-flex flex-wrap gap-2 border rounded p-2">
      {options.length === 0 && (
        <span className="text-body text-opacity-50 small">No record types available.</span>
      )}
      {options.map((opt) => {
        const selected = value.includes(opt.id);
        return (
          <button
            key={opt.id}
            type="button"
            className={`btn btn-sm ${selected ? "btn-primary" : "btn-outline-secondary"}`}
            onClick={() => {
              onChange(selected ? value.filter((v) => v !== opt.id) : [...value, opt.id]);
            }}
          >
            {opt.label}
          </button>
        );
      })}
    </div>
  );
}

function EdgeFieldModal({
  edgeTypeId,
  state,
  dataTypes,
  onClose,
  onSuccess,
  onError
}: {
  edgeTypeId: string;
  state: Exclude<FieldModalState, { kind: "none" }>;
  dataTypes: FieldDataType[];
  onClose: () => void;
  onSuccess: (m: string) => void;
  onError: (m: string) => void;
}) {
  const isEdit = state.kind === "edit";
  const existing = isEdit ? state.field : null;
  const [fieldKey, setFieldKey] = useState(existing?.fieldKey ?? "");
  const [displayName, setDisplayName] = useState(existing?.displayName ?? "");
  const [dataType, setDataType] = useState<FieldDataType>(existing?.dataType ?? (dataTypes[0] ?? "text"));
  const [config, setConfig] = useState<Record<string, unknown>>(
    existing?.config ?? defaultFieldConfig(dataTypes[0] ?? "text")
  );
  const [isRequired, setIsRequired] = useState(existing?.isRequired ?? false);
  const [sortOrder, setSortOrder] = useState(existing?.sortOrder ?? 0);

  const create = useCreateEdgeTypeField(edgeTypeId);
  const update = useUpdateEdgeTypeField(edgeTypeId);
  const del = useDeleteEdgeTypeField(edgeTypeId);

  const onTypeChange = (next: FieldDataType) => {
    setDataType(next);
    if (!isEdit) setConfig(defaultFieldConfig(next));
  };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      if (isEdit && existing) {
        await update.mutateAsync({
          fieldId: existing.id,
          request: { displayName: displayName.trim(), config, isRequired, sortOrder }
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

  const remove = async () => {
    if (!existing) return;
    try {
      await del.mutateAsync(existing.id);
      onSuccess(`Removed ${existing.fieldKey}.`);
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
                <h5 className="modal-title">{isEdit ? `Edit field: ${existing?.fieldKey}` : "Add edge field"}</h5>
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
                      placeholder="weight"
                      required={!isEdit}
                      disabled={isEdit}
                    />
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
                        id="edge-field-required"
                        checked={isRequired}
                        onChange={(e) => setIsRequired(e.target.checked)}
                      />
                      <label className="form-check-label" htmlFor="edge-field-required">
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
                  {isEdit && (
                    <button type="button" className="btn btn-outline-danger" onClick={remove} disabled={del.isPending}>
                      Delete field
                    </button>
                  )}
                </div>
                <div>
                  <button type="button" className="btn btn-outline-secondary me-2" onClick={onClose}>
                    Cancel
                  </button>
                  <button type="submit" className="btn btn-primary" disabled={create.isPending || update.isPending}>
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

function arraysEqual(a: string[], b: string[]) {
  if (a.length !== b.length) return false;
  const sortedA = [...a].sort();
  const sortedB = [...b].sort();
  for (let i = 0; i < sortedA.length; i++) if (sortedA[i] !== sortedB[i]) return false;
  return true;
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { message?: string } } }).response;
    return response?.data?.message ?? error.message;
  }
  return String(error);
}
