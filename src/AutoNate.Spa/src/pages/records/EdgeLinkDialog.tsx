import { useEffect, useMemo, useState } from "react";
import { useCreateEdge, useEdgeTypeFields, useEdgeTypes } from "@/hooks/useRecordEdges";
import { useRecords } from "@/hooks/useRecords";
import { useRecordTypes } from "@/hooks/useRecordTypes";
import { EdgeType, RecordModel, RecordType } from "@/types/records";
import "./fields/renderers";
import { defaultFieldConfig } from "../record-types/fieldTypeDefaults";
import { getRenderer } from "./fields/registry";

type Props = {
  thisRecord: RecordModel;
  thisRecordType: RecordType;
  onClose: () => void;
  onSuccess: (message: string) => void;
  onError: (message: string) => void;
};

type LinkDirection = "outgoing" | "incoming";

export default function EdgeLinkDialog({ thisRecord, thisRecordType, onClose, onSuccess, onError }: Props) {
  const { data: edgeTypes = [] } = useEdgeTypes(false);
  const { data: recordTypes = [] } = useRecordTypes(false);

  const candidateEdgeTypes = useMemo(
    () =>
      edgeTypes.filter((et) => {
        const fromOk = !et.fromRecordTypeIds || et.fromRecordTypeIds.includes(thisRecordType.id);
        const toOk = !et.toRecordTypeIds || et.toRecordTypeIds.includes(thisRecordType.id);
        return fromOk || toOk;
      }),
    [edgeTypes, thisRecordType.id]
  );

  const [edgeTypeId, setEdgeTypeId] = useState<string>(candidateEdgeTypes[0]?.id ?? "");
  useEffect(() => {
    if (!edgeTypeId && candidateEdgeTypes.length > 0) {
      setEdgeTypeId(candidateEdgeTypes[0].id);
    }
  }, [candidateEdgeTypes, edgeTypeId]);

  const edgeType = candidateEdgeTypes.find((et) => et.id === edgeTypeId);

  const fromAllowed = edgeType
    ? !edgeType.fromRecordTypeIds || edgeType.fromRecordTypeIds.includes(thisRecordType.id)
    : false;
  const toAllowed = edgeType
    ? !edgeType.toRecordTypeIds || edgeType.toRecordTypeIds.includes(thisRecordType.id)
    : false;

  // If only one direction is valid, lock to it. Otherwise default to outgoing.
  const [direction, setDirection] = useState<LinkDirection>(
    fromAllowed ? "outgoing" : "incoming"
  );
  useEffect(() => {
    if (!fromAllowed && toAllowed) setDirection("incoming");
    else if (fromAllowed && !toAllowed) setDirection("outgoing");
  }, [fromAllowed, toAllowed]);

  // Allowed record types for the OTHER side, given the current direction.
  const otherSideAllowedTypeIds = useMemo<string[] | null>(() => {
    if (!edgeType) return null;
    return direction === "outgoing"
      ? edgeType.toRecordTypeIds ?? null
      : edgeType.fromRecordTypeIds ?? null;
  }, [edgeType, direction]);

  const otherTypeOptions = useMemo<RecordType[]>(() => {
    if (otherSideAllowedTypeIds === null) return recordTypes;
    return recordTypes.filter((rt) => otherSideAllowedTypeIds.includes(rt.id));
  }, [recordTypes, otherSideAllowedTypeIds]);

  const [otherTypeId, setOtherTypeId] = useState<string>("");
  useEffect(() => {
    if (otherTypeOptions.length > 0 && !otherTypeOptions.find((t) => t.id === otherTypeId)) {
      setOtherTypeId(otherTypeOptions[0].id);
    }
  }, [otherTypeOptions, otherTypeId]);

  const { data: candidates } = useRecords(
    {
      recordTypeId: otherTypeId,
      page: 0,
      pageSize: 200,
      includeArchived: false,
      sort: "updated_desc"
    },
    Boolean(otherTypeId)
  );

  const [otherRecordId, setOtherRecordId] = useState<string>("");

  // Field data for the edge.
  const { data: edgeFields = [] } = useEdgeTypeFields(edgeTypeId || null);
  const [data, setData] = useState<Record<string, unknown>>({});
  useEffect(() => {
    // Reset data shape when fields change.
    const next: Record<string, unknown> = {};
    for (const f of edgeFields) {
      next[f.fieldKey] = (defaultFieldConfig(f.dataType) as unknown) === f.config ? "" : "";
    }
    setData(next);
  }, [edgeFields]);

  const create = useCreateEdge(thisRecord.id);

  if (candidateEdgeTypes.length === 0) {
    return (
      <DialogShell title="Link to another record" onClose={onClose}>
        <div className="modal-body">
          <p className="mb-0">
            No edge types are configured for record type{" "}
            <code>{thisRecordType.shortCode}</code>. Define one under{" "}
            <strong>Edge Types</strong> first.
          </p>
        </div>
        <div className="modal-footer">
          <button type="button" className="btn btn-outline-secondary" onClick={onClose}>
            Close
          </button>
        </div>
      </DialogShell>
    );
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!edgeType || !otherRecordId) return;
    const fromId = direction === "outgoing" ? thisRecord.id : otherRecordId;
    const toId = direction === "outgoing" ? otherRecordId : thisRecord.id;
    try {
      await create.mutateAsync({
        edgeTypeId: edgeType.id,
        fromRecordId: fromId,
        toRecordId: toId,
        data
      });
      onSuccess("Linked.");
    } catch (err) {
      onError(describeError(err));
    }
  };

  return (
    <DialogShell title="Link to another record" onClose={onClose}>
      <form onSubmit={submit}>
        <div className="modal-body">
          <div className="row g-3">
            <div className="col-md-6">
              <label className="form-label">Edge type</label>
              <select
                className="form-select"
                value={edgeTypeId}
                onChange={(e) => setEdgeTypeId(e.target.value)}
              >
                {candidateEdgeTypes.map((et) => (
                  <option key={et.id} value={et.id}>
                    {et.shortCode} - {et.name}
                  </option>
                ))}
              </select>
            </div>
            <div className="col-md-6">
              <label className="form-label">Direction</label>
              <select
                className="form-select"
                value={direction}
                onChange={(e) => setDirection(e.target.value as LinkDirection)}
                disabled={!fromAllowed || !toAllowed || !edgeType?.isDirected}
              >
                <option value="outgoing">
                  This record {labelFor(edgeType, "forward")} other
                </option>
                <option value="incoming">
                  Other {labelFor(edgeType, "forward")} this record
                </option>
              </select>
            </div>
            <div className="col-md-5">
              <label className="form-label">Other record type</label>
              <select
                className="form-select"
                value={otherTypeId}
                onChange={(e) => setOtherTypeId(e.target.value)}
                disabled={otherTypeOptions.length === 0}
              >
                {otherTypeOptions.map((rt) => (
                  <option key={rt.id} value={rt.id}>
                    {rt.shortCode} - {rt.name}
                  </option>
                ))}
              </select>
            </div>
            <div className="col-md-7">
              <label className="form-label">Other record</label>
              <select
                className="form-select"
                value={otherRecordId}
                onChange={(e) => setOtherRecordId(e.target.value)}
                disabled={!candidates || candidates.items.length === 0}
              >
                <option value="">Select...</option>
                {(candidates?.items ?? [])
                  .filter((r) => r.id !== thisRecord.id || edgeType?.allowSelfReference)
                  .map((r) => (
                    <option key={r.id} value={r.id}>
                      {r.key} — {r.name}
                    </option>
                  ))}
              </select>
              {candidates && candidates.totalCount > candidates.items.length && (
                <div className="form-text">
                  Showing {candidates.items.length} of {candidates.totalCount}.
                </div>
              )}
            </div>
            {edgeFields.length > 0 && (
              <div className="col-12">
                <hr />
                <h6 className="mb-3">Edge data</h6>
                <div className="row g-3">
                  {edgeFields.map((field) => {
                    const renderer = getRenderer(field.dataType);
                    if (!renderer) {
                      return (
                        <div key={field.id} className="col-md-6">
                          <label className="form-label">{field.displayName}</label>
                          <input
                            className="form-control"
                            placeholder={`(${field.dataType})`}
                            value={String(data[field.fieldKey] ?? "")}
                            onChange={(e) =>
                              setData((d) => ({ ...d, [field.fieldKey]: e.target.value }))
                            }
                          />
                        </div>
                      );
                    }
                    return (
                      <div key={field.id} className="col-md-6">
                        <label className="form-label">
                          {field.displayName}
                          {field.isRequired && <span className="text-danger ms-1">*</span>}
                        </label>
                        <SimpleFieldInput
                          fieldKey={field.fieldKey}
                          dataType={field.dataType}
                          value={data[field.fieldKey]}
                          onChange={(v) => setData((d) => ({ ...d, [field.fieldKey]: v }))}
                        />
                      </div>
                    );
                  })}
                </div>
              </div>
            )}
          </div>
        </div>
        <div className="modal-footer">
          <button type="button" className="btn btn-outline-secondary" onClick={onClose}>
            Cancel
          </button>
          <button
            type="submit"
            className="btn btn-primary"
            disabled={!edgeType || !otherRecordId || create.isPending}
          >
            Create link
          </button>
        </div>
      </form>
    </DialogShell>
  );
}

function labelFor(edgeType: EdgeType | undefined, dir: "forward" | "inverse") {
  if (!edgeType) return "→";
  if (dir === "forward") return edgeType.name;
  return edgeType.inverseName ?? `← ${edgeType.name}`;
}

function DialogShell({
  title,
  children,
  onClose
}: {
  title: string;
  children: React.ReactNode;
  onClose: () => void;
}) {
  return (
    <>
      <div className="modal fade show d-block" role="dialog" aria-modal="true" tabIndex={-1}>
        <div className="modal-dialog modal-lg">
          <div className="modal-content">
            <div className="modal-header">
              <h5 className="modal-title">{title}</h5>
              <button type="button" className="btn-close" onClick={onClose} aria-label="Close" />
            </div>
            {children}
          </div>
        </div>
      </div>
      <div className="modal-backdrop fade show" />
    </>
  );
}

// Simple controlled input for the EdgeLinkDialog. We don't use react-hook-form
// here because the field set is small and changes when edge type changes.
function SimpleFieldInput({
  fieldKey,
  dataType,
  value,
  onChange
}: {
  fieldKey: string;
  dataType: string;
  value: unknown;
  onChange: (v: unknown) => void;
}) {
  switch (dataType) {
    case "boolean":
      return (
        <div className="form-check form-switch">
          <input
            type="checkbox"
            className="form-check-input"
            id={`edge-${fieldKey}`}
            checked={Boolean(value)}
            onChange={(e) => onChange(e.target.checked)}
          />
          <label className="form-check-label" htmlFor={`edge-${fieldKey}`}>
            {Boolean(value) ? "Yes" : "No"}
          </label>
        </div>
      );
    case "number":
      return (
        <input
          type="number"
          className="form-control"
          value={value === null || value === undefined ? "" : String(value)}
          onChange={(e) => onChange(e.target.value === "" ? null : Number(e.target.value))}
        />
      );
    case "date":
      return (
        <input
          type="date"
          className="form-control"
          value={(value as string | null) ?? ""}
          onChange={(e) => onChange(e.target.value || null)}
        />
      );
    default:
      return (
        <input
          type="text"
          className="form-control"
          value={(value as string | null) ?? ""}
          onChange={(e) => onChange(e.target.value)}
        />
      );
  }
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { message?: string } } }).response;
    return response?.data?.message ?? error.message;
  }
  return String(error);
}
