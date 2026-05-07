import { useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { ColumnDef } from "@tanstack/react-table";
import { CreateFormRequest, FormSummary, listForms } from "@/api/forms";
import { useCreateForm, useDeleteForm } from "@/hooks/useForms";
import { DataTable } from "@/components/data-table/DataTable";

const COLUMN_WIDTHS = ["14%", "30%", "16%", "13%", "17%", "10%"];

export default function FormsList() {
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

  const columns = useMemo<ColumnDef<FormSummary>[]>(
    () => [
      {
        id: "shortCode",
        accessorKey: "shortCode",
        header: "Short code",
        cell: ({ row }) => <code>{row.original.shortCode}</code>
      },
      {
        id: "name",
        accessorKey: "name",
        header: "Name",
        cell: ({ row }) => (
          <Link to={`/admin/config/forms/${row.original.id}`} className="fw-semibold">
            {row.original.name}
          </Link>
        )
      },
      {
        id: "status",
        accessorFn: (f) =>
          f.publishedVersionNumber === null
            ? "Draft"
            : f.isDraft
              ? "Has changes"
              : f.siteAvailable
                ? "Live"
                : "Published",
        header: "Status",
        cell: ({ row }) => <StatusBadges form={row.original} />
      },
      {
        id: "versions",
        accessorFn: (f) => f.draftVersionNumber,
        header: "Versions",
        cell: ({ row }) => (
          <>
            <span className="text-body text-opacity-75">
              Draft v{row.original.draftVersionNumber}
            </span>
            {row.original.publishedVersionNumber !== null && (
              <>
                <br />
                <span className="text-body text-opacity-50">
                  Pub v{row.original.publishedVersionNumber}
                </span>
              </>
            )}
          </>
        )
      },
      {
        id: "updatedAtUtc",
        accessorKey: "updatedAtUtc",
        header: "Updated",
        cell: ({ row }) => formatWhen(row.original.updatedAtUtc)
      },
      {
        id: "actions",
        header: "Actions",
        enableSorting: false,
        enableGlobalFilter: false,
        cell: ({ row }) => (
          <div className="data-table-row-actions">
            <button
              type="button"
              className="btn btn-icon btn-icon-danger"
              title="Delete form"
              aria-label={`Delete ${row.original.shortCode}`}
              disabled={deleteForm.isPending}
              onClick={(e) => {
                e.stopPropagation();
                void onDelete(row.original);
              }}
            >
              <i className="fa fa-trash"></i>
            </button>
          </div>
        )
      }
    ],
    [deleteForm.isPending]
  );

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

      <DataTable<FormSummary>
        mode="client"
        loadAll={() => listForms()}
        queryKey={["forms"]}
        columns={columns}
        rowKey={(f) => f.id}
        columnWidths={COLUMN_WIDTHS}
        initialSort={[{ id: "shortCode", desc: false }]}
        searchPlaceholder="Search forms…"
        emptyMessage="No forms yet. Click 'New form' to create one."
        loadingMessage="Loading forms…"
        globalFilterFn={(f, search) => {
          const needle = search.toLowerCase();
          return `${f.shortCode} ${f.name}`.toLowerCase().includes(needle);
        }}
        toolbarRight={
          <button
            type="button"
            className="btn btn-add-user"
            onClick={() => setModalOpen(true)}
          >
            <i className="fa fa-plus me-2"></i>New form
          </button>
        }
      />

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
