import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { CreateFormRequest, FormSummary } from "@/api/forms";
import { useCreateForm, useDeleteForm, useForms } from "@/hooks/useForms";

export default function FormsList() {
  const { data: forms = [], isLoading } = useForms();
  const [modalOpen, setModalOpen] = useState(false);
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(
    null
  );
  const deleteForm = useDeleteForm();

  const onDelete = async (form: FormSummary) => {
    if (!window.confirm(`Delete form "${form.name}" (${form.shortCode})?`)) return;
    try {
      await deleteForm.mutateAsync(form.id);
      setFlash({ kind: "success", message: `Deleted ${form.shortCode}.` });
    } catch (err) {
      setFlash({ kind: "error", message: describeError(err) });
    }
  };

  return (
    <>
      <div className="page-head">
        <div>
          <h1 className="page-header mb-1">Forms</h1>
          <p className="page-head-copy">
            Author JSX forms that can be bound to records, workflow tasks, or any other data
            source. Each save snapshots a version; publishing makes the form live at{" "}
            <code>/form/&lt;shortcode&gt;</code> when Site-available is on.
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
          <h4 className="panel-title">All Forms</h4>
        </div>
        <div className="panel-body">
          <div className="d-flex justify-content-between align-items-center mb-3">
            <button type="button" className="btn btn-primary" onClick={() => setModalOpen(true)}>
              <i className="fa fa-plus me-2"></i>New Form
            </button>
          </div>

          <div className="table-responsive">
            <table className="table table-striped table-bordered align-middle">
              <thead>
                <tr>
                  <th style={{ width: "10rem" }}>Short code</th>
                  <th>Name</th>
                  <th style={{ width: "10rem" }}>Status</th>
                  <th style={{ width: "8rem" }}>Versions</th>
                  <th style={{ width: "12rem" }}>Updated</th>
                  <th style={{ width: "10rem" }}></th>
                </tr>
              </thead>
              <tbody>
                {isLoading && (
                  <tr>
                    <td colSpan={6} className="text-center text-body text-opacity-50 p-4">
                      Loading…
                    </td>
                  </tr>
                )}
                {!isLoading && forms.length === 0 && (
                  <tr>
                    <td colSpan={6} className="text-center text-body text-opacity-50 p-4">
                      No forms yet. Click "New Form" to create one.
                    </td>
                  </tr>
                )}
                {forms.map((f) => (
                  <tr key={f.id}>
                    <td>
                      <code>{f.shortCode}</code>
                    </td>
                    <td>
                      <Link to={`/admin/config/forms/${f.id}`} className="fw-semibold">
                        {f.name}
                      </Link>
                    </td>
                    <td>
                      <StatusBadges form={f} />
                    </td>
                    <td>
                      <span className="text-body text-opacity-75">
                        Draft v{f.draftVersionNumber}
                      </span>
                      {f.publishedVersionNumber !== null && (
                        <>
                          <br />
                          <span className="text-body text-opacity-50">
                            Pub v{f.publishedVersionNumber}
                          </span>
                        </>
                      )}
                    </td>
                    <td>{formatWhen(f.updatedAtUtc)}</td>
                    <td>
                      <button
                        type="button"
                        className="btn btn-outline-danger btn-sm"
                        onClick={() => onDelete(f)}
                        disabled={deleteForm.isPending}
                      >
                        Delete
                      </button>
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
          onError={(m) => setFlash({ kind: "error", message: m })}
        />
      )}
    </>
  );
}

function StatusBadges({ form }: { form: FormSummary }) {
  if (form.publishedVersionNumber === null) {
    return <span className="badge bg-secondary">Draft</span>;
  }
  if (form.isDraft) {
    return (
      <>
        <span className="badge bg-success me-1">Published</span>
        <span className="badge bg-warning text-dark">Has changes</span>
      </>
    );
  }
  return form.siteAvailable ? (
    <span className="badge bg-success">Live</span>
  ) : (
    <span className="badge bg-success">Published</span>
  );
}

function CreateModal({
  onClose,
  onError
}: {
  onClose: () => void;
  onError: (m: string) => void;
}) {
  const [values, setValues] = useState<CreateFormRequest>({
    name: "",
    shortCode: "",
    siteAvailable: false
  });
  const create = useCreateForm();
  const navigate = useNavigate();

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const created = await create.mutateAsync({
        name: values.name.trim(),
        shortCode: values.shortCode.trim().toLowerCase(),
        siteAvailable: values.siteAvailable
      });
      navigate(`/admin/config/forms/${created.id}`);
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
                <h5 className="modal-title">New Form</h5>
                <button
                  type="button"
                  className="btn-close"
                  onClick={onClose}
                  aria-label="Close"
                />
              </div>
              <div className="modal-body">
                <div className="mb-3">
                  <label className="form-label">Name</label>
                  <input
                    className="form-control"
                    value={values.name}
                    onChange={(e) => setValues({ ...values, name: e.target.value })}
                    required
                    autoFocus
                  />
                </div>
                <div className="mb-3">
                  <label className="form-label">Short code</label>
                  <input
                    className="form-control text-lowercase"
                    placeholder="contact-form"
                    value={values.shortCode}
                    onChange={(e) => setValues({ ...values, shortCode: e.target.value })}
                    required
                  />
                  <div className="form-text">
                    Used in <code>/form/&lt;short-code&gt;</code> and{" "}
                    <code>/formdev/&lt;short-code&gt;</code>.
                  </div>
                </div>
                <div className="form-check form-switch">
                  <input
                    type="checkbox"
                    className="form-check-input"
                    id="create-site-available"
                    checked={values.siteAvailable}
                    onChange={(e) =>
                      setValues({ ...values, siteAvailable: e.target.checked })
                    }
                  />
                  <label className="form-check-label" htmlFor="create-site-available">
                    Site-available (can be loaded at /form/&lt;short-code&gt; once published)
                  </label>
                </div>
              </div>
              <div className="modal-footer">
                <button
                  type="button"
                  className="btn btn-outline-secondary"
                  onClick={onClose}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="btn btn-primary"
                  disabled={create.isPending}
                >
                  Create &amp; edit
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
    const response = (error as { response?: { data?: { reason?: string } } }).response;
    return response?.data?.reason ?? error.message;
  }
  return String(error);
}
