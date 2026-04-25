import { useState } from "react";
import { Link } from "react-router-dom";
import {
  useCreateRecordType,
  useRecordTypes,
  useRestoreRecordType
} from "@/hooks/useRecordTypes";
import { CreateRecordTypeRequest, RecordType } from "@/types/records";
import IconPicker from "@/components/IconPicker";
import ColorPicker from "@/components/ColorPicker";

export default function RecordTypeList() {
  const [includeArchived, setIncludeArchived] = useState(false);
  const { data: types = [], isLoading } = useRecordTypes(includeArchived);
  const [modalOpen, setModalOpen] = useState(false);
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(null);
  const restore = useRestoreRecordType();

  const onRestore = async (type: RecordType) => {
    try {
      await restore.mutateAsync(type.id);
      setFlash({ kind: "success", message: `Restored ${type.shortCode}.` });
    } catch (err) {
      setFlash({ kind: "error", message: describeError(err) });
    }
  };

  return (
    <>
      <div className="page-head">
        <div>
          <h1 className="page-header mb-1">Record Types</h1>
          <p className="page-head-copy">
            Define the records your app manages. Each record type has a short code (used as the key prefix)
            and a set of fields that every record of that type will have.
          </p>
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

      <div className="panel panel-inverse">
        <div className="panel-heading">
          <h4 className="panel-title">All Record Types</h4>
        </div>
        <div className="panel-body">
          <div className="d-flex justify-content-between align-items-center mb-3">
            <div>
              <button type="button" className="btn btn-primary" onClick={() => setModalOpen(true)}>
                <i className="fa fa-plus me-2"></i>New Record Type
              </button>
            </div>
            <div className="form-check form-switch">
              <input
                type="checkbox"
                className="form-check-input"
                id="include-archived"
                checked={includeArchived}
                onChange={(e) => setIncludeArchived(e.target.checked)}
              />
              <label className="form-check-label" htmlFor="include-archived">
                Show archived
              </label>
            </div>
          </div>

          <div className="table-responsive">
            <table className="table table-striped table-bordered align-middle">
              <thead>
                <tr>
                  <th style={{ width: "6rem" }}>Code</th>
                  <th>Name</th>
                  <th>Description</th>
                  <th style={{ width: "10rem" }}>Updated</th>
                  <th style={{ width: "9rem" }}>Status</th>
                  <th style={{ width: "8rem" }}></th>
                </tr>
              </thead>
              <tbody>
                {isLoading && (
                  <tr>
                    <td colSpan={6} className="text-center text-body text-opacity-50 p-4">
                      Loading...
                    </td>
                  </tr>
                )}
                {!isLoading && types.length === 0 && (
                  <tr>
                    <td colSpan={6} className="text-center text-body text-opacity-50 p-4">
                      No record types yet. Click "New Record Type" to create one.
                    </td>
                  </tr>
                )}
                {types.map((t) => (
                  <tr key={t.id} className={t.isArchived ? "text-body text-opacity-50" : undefined}>
                    <td>
                      <code>{t.shortCode}</code>
                    </td>
                    <td>
                      <Link to={`/record-types/${t.id}`} className="fw-semibold">
                        {t.name}
                      </Link>
                    </td>
                    <td>{t.description ?? ""}</td>
                    <td>{formatWhen(t.updatedAtUtc)}</td>
                    <td>
                      {t.isArchived ? (
                        <>
                          <span className="badge bg-secondary me-2">Archived</span>
                          <button
                            type="button"
                            className="btn btn-link btn-sm p-0"
                            onClick={() => onRestore(t)}
                            disabled={restore.isPending}
                          >
                            Restore
                          </button>
                        </>
                      ) : (
                        <span className="badge bg-success">Active</span>
                      )}
                    </td>
                    <td>
                      <Link to={`/records/${t.shortCode}`} className="btn btn-outline-secondary btn-sm">
                        Records
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>

      {modalOpen && (
        <CreateModal
          onClose={() => setModalOpen(false)}
          onSuccess={(t) => {
            setFlash({ kind: "success", message: `Created record type ${t.shortCode}.` });
            setModalOpen(false);
          }}
          onError={(m) => setFlash({ kind: "error", message: m })}
        />
      )}
    </>
  );
}

function CreateModal({
  onClose,
  onSuccess,
  onError
}: {
  onClose: () => void;
  onSuccess: (t: RecordType) => void;
  onError: (m: string) => void;
}) {
  const [values, setValues] = useState<CreateRecordTypeRequest>({
    shortCode: "",
    name: "",
    description: null,
    icon: null,
    color: null
  });
  const create = useCreateRecordType();

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const created = await create.mutateAsync({
        shortCode: values.shortCode.trim().toUpperCase(),
        name: values.name.trim(),
        description: values.description?.trim() || null,
        icon: values.icon?.trim() || null,
        color: values.color?.trim() || null
      });
      onSuccess(created);
    } catch (err) {
      onError(describeError(err));
    }
  };

  return (
    <>
      <div className="modal fade show d-block" role="dialog" aria-modal="true" tabIndex={-1}>
        <div className="modal-dialog">
          <div className="modal-content">
            <form onSubmit={submit}>
              <div className="modal-header">
                <h5 className="modal-title">New Record Type</h5>
                <button type="button" className="btn-close" onClick={onClose} aria-label="Close" />
              </div>
              <div className="modal-body">
                <div className="mb-3">
                  <label className="form-label">Short code</label>
                  <input
                    className="form-control text-uppercase"
                    maxLength={8}
                    placeholder="ACC"
                    value={values.shortCode}
                    onChange={(e) => setValues({ ...values, shortCode: e.target.value })}
                    required
                  />
                  <div className="form-text">
                    2-8 characters, used as the record-key prefix (e.g. <code>ACC-142</code>).
                  </div>
                </div>
                <div className="mb-3">
                  <label className="form-label">Name</label>
                  <input
                    className="form-control"
                    value={values.name}
                    onChange={(e) => setValues({ ...values, name: e.target.value })}
                    required
                  />
                </div>
                <div className="mb-3">
                  <label className="form-label">Description</label>
                  <textarea
                    className="form-control"
                    rows={3}
                    value={values.description ?? ""}
                    onChange={(e) => setValues({ ...values, description: e.target.value })}
                  />
                </div>
                <div className="row g-2">
                  <div className="col">
                    <label className="form-label">Icon (FontAwesome)</label>
                    <IconPicker
                      value={values.icon ?? ""}
                      onChange={(v) => setValues({ ...values, icon: v })}
                    />
                  </div>
                  <div className="col">
                    <label className="form-label">Color</label>
                    <ColorPicker
                      value={values.color ?? ""}
                      onChange={(v) => setValues({ ...values, color: v })}
                    />
                  </div>
                </div>
              </div>
              <div className="modal-footer">
                <button type="button" className="btn btn-outline-secondary" onClick={onClose}>
                  Cancel
                </button>
                <button type="submit" className="btn btn-primary" disabled={create.isPending}>
                  Create
                </button>
              </div>
            </form>
          </div>
        </div>
      </div>
      <div className="modal-backdrop fade show" />
    </>
  );
}

function formatWhen(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { message?: string } } }).response;
    return response?.data?.message ?? error.message;
  }
  return String(error);
}
