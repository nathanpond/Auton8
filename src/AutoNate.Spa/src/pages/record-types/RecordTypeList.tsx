import { useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { ColumnDef } from "@tanstack/react-table";
import { useCreateRecordType, useRestoreRecordType } from "@/hooks/useRecordTypes";
import { listRecordTypes } from "@/api/recordTypes";
import { CreateRecordTypeRequest, RecordType } from "@/types/records";
import IconPicker from "@/components/IconPicker";
import ColorPicker from "@/components/ColorPicker";
import { DataTable } from "@/components/data-table/DataTable";

const COLUMN_WIDTHS = ["10%", "22%", "26%", "16%", "14%", "12%"];

export default function RecordTypeList() {
  const [includeArchived, setIncludeArchived] = useState(false);
  const navigate = useNavigate();
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

  const columns = useMemo<ColumnDef<RecordType>[]>(
    () => [
      {
        id: "shortCode",
        accessorKey: "shortCode",
        header: "Code",
        cell: ({ row }) => <code>{row.original.shortCode}</code>
      },
      {
        id: "name",
        accessorKey: "name",
        header: "Name",
        cell: ({ row }) => (
          <Link to={`/record-types/${row.original.id}`} className="fw-semibold">
            {row.original.name}
          </Link>
        )
      },
      {
        id: "description",
        accessorKey: "description",
        header: "Description",
        cell: ({ row }) => row.original.description ?? ""
      },
      {
        id: "updatedAtUtc",
        accessorKey: "updatedAtUtc",
        header: "Updated",
        cell: ({ row }) => formatWhen(row.original.updatedAtUtc)
      },
      {
        id: "status",
        accessorFn: (t) => (t.isArchived ? "Archived" : "Active"),
        header: "Status",
        cell: ({ row }) =>
          row.original.isArchived ? (
            <>
              <span className="badge bg-secondary me-2">Archived</span>
              <button
                type="button"
                className="btn btn-link btn-sm p-0"
                onClick={(e) => {
                  e.stopPropagation();
                  void onRestore(row.original);
                }}
                disabled={restore.isPending}
              >
                Restore
              </button>
            </>
          ) : (
            <span className="badge bg-success">Active</span>
          )
      },
      {
        id: "records",
        header: "Records",
        enableSorting: false,
        enableGlobalFilter: false,
        cell: ({ row }) => (
          <Link
            to={`/records/${row.original.shortCode}`}
            className="btn btn-outline-secondary btn-sm"
            onClick={(e) => e.stopPropagation()}
          >
            Records
          </Link>
        )
      }
    ],
    [restore.isPending]
  );

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

      <DataTable<RecordType>
        mode="client"
        loadAll={() => listRecordTypes(includeArchived)}
        queryKey={["record-types", { includeArchived }]}
        columns={columns}
        rowKey={(t) => t.id}
        columnWidths={COLUMN_WIDTHS}
        initialSort={[{ id: "shortCode", desc: false }]}
        searchPlaceholder="Search record types…"
        emptyMessage='No record types yet. Click "New record type" to create one.'
        loadingMessage="Loading record types…"
        getRowClassName={(t) => (t.isArchived ? "row-archived" : undefined)}
        onRowClick={(t) => navigate(`/record-types/${t.id}`)}
        getRowAriaLabel={(t) => `Open ${t.shortCode}`}
        globalFilterFn={(t, search) => {
          const needle = search.toLowerCase();
          return `${t.shortCode} ${t.name} ${t.description ?? ""}`.toLowerCase().includes(needle);
        }}
        toolbarLeft={
          <div className="form-check form-switch ms-2">
            <input
              type="checkbox"
              className="form-check-input"
              id="include-archived-record-types"
              checked={includeArchived}
              onChange={(e) => setIncludeArchived(e.target.checked)}
            />
            <label className="form-check-label" htmlFor="include-archived-record-types">
              Show archived
            </label>
          </div>
        }
        toolbarRight={
          <button
            type="button"
            className="btn btn-add-user"
            onClick={() => setModalOpen(true)}
          >
            <i className="fa fa-plus me-2"></i>New record type
          </button>
        }
      />

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
