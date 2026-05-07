import { useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { ColumnDef } from "@tanstack/react-table";
import { useCreateEdgeType, useRestoreEdgeType } from "@/hooks/useRecordEdges";
import { listEdgeTypes } from "@/api/recordEdges";
import { CreateEdgeTypeRequest, EdgeType } from "@/types/records";
import { DataTable } from "@/components/data-table/DataTable";

const COLUMN_WIDTHS = ["12%", "40%", "14%", "18%", "16%"];

export default function EdgeTypeList() {
  const [includeArchived, setIncludeArchived] = useState(false);
  const restore = useRestoreEdgeType();
  const navigate = useNavigate();
  const [modalOpen, setModalOpen] = useState(false);
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(null);

  const onRestore = async (t: EdgeType) => {
    try {
      await restore.mutateAsync(t.id);
      setFlash({ kind: "success", message: `Restored ${t.shortCode}.` });
    } catch (err) {
      setFlash({ kind: "error", message: describeError(err) });
    }
  };

  const columns = useMemo<ColumnDef<EdgeType>[]>(
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
        header: "Forward / Inverse",
        cell: ({ row }) => (
          <>
            <Link to={`/record-edge-types/${row.original.id}`} className="fw-semibold">
              {row.original.name}
            </Link>
            {row.original.inverseName && (
              <span className="text-body text-opacity-50 ms-2">/ {row.original.inverseName}</span>
            )}
          </>
        )
      },
      {
        id: "direction",
        accessorFn: (t) => (t.isDirected ? "Directed" : "Undirected"),
        header: "Direction"
      },
      {
        id: "cardinality",
        accessorKey: "cardinality",
        header: "Cardinality"
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
      }
    ],
    [restore.isPending]
  );

  return (
    <>
      <div className="page-head">
        <h1 className="page-header mb-1">Edge Types</h1>
        <p className="page-head-copy">
          Edge types describe how records can link together (e.g. <code>Account</code> <em>has contact</em>{" "}
          <code>Contact</code>). Each edge type can carry its own configurable data fields.
        </p>
      </div>

      {flash && (
        <div
          className={`alert ${flash.kind === "success" ? "alert-success" : "alert-danger"}`}
          role={flash.kind === "success" ? "status" : "alert"}
        >
          {flash.message}
        </div>
      )}

      <DataTable<EdgeType>
        mode="client"
        loadAll={() => listEdgeTypes(includeArchived)}
        queryKey={["edge-types", { includeArchived }]}
        columns={columns}
        rowKey={(t) => t.id}
        columnWidths={COLUMN_WIDTHS}
        initialSort={[{ id: "shortCode", desc: false }]}
        searchPlaceholder="Search edge types…"
        emptyMessage="No edge types yet. Create one to let records link to each other."
        loadingMessage="Loading edge types…"
        getRowClassName={(t) => (t.isArchived ? "row-archived" : undefined)}
        onRowClick={(t) => navigate(`/record-edge-types/${t.id}`)}
        getRowAriaLabel={(t) => `Open ${t.shortCode}`}
        globalFilterFn={(t, search) => {
          const needle = search.toLowerCase();
          return `${t.shortCode} ${t.name} ${t.inverseName ?? ""}`.toLowerCase().includes(needle);
        }}
        toolbarLeft={
          <div className="form-check form-switch ms-2">
            <input
              type="checkbox"
              className="form-check-input"
              id="include-archived-edges"
              checked={includeArchived}
              onChange={(e) => setIncludeArchived(e.target.checked)}
            />
            <label className="form-check-label" htmlFor="include-archived-edges">
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
            <i className="fa fa-plus me-2"></i>New edge type
          </button>
        }
      />

      {modalOpen && (
        <CreateModal
          onClose={() => setModalOpen(false)}
          onSuccess={(t) => {
            setFlash({ kind: "success", message: `Created edge type ${t.shortCode}.` });
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
  onSuccess: (t: EdgeType) => void;
  onError: (m: string) => void;
}) {
  const [values, setValues] = useState<CreateEdgeTypeRequest>({
    shortCode: "",
    name: "",
    inverseName: null,
    isDirected: true,
    allowSelfReference: false,
    cardinality: "many_to_many",
    fromRecordTypeIds: null,
    toRecordTypeIds: null
  });
  const create = useCreateEdgeType();

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const created = await create.mutateAsync({
        ...values,
        shortCode: values.shortCode.trim().toUpperCase(),
        name: values.name.trim(),
        inverseName: values.inverseName?.trim() || null
      });
      onSuccess(created);
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
                <h5 className="modal-title">New Edge Type</h5>
                <button type="button" className="btn-close" onClick={onClose} aria-label="Close" />
              </div>
              <div className="modal-body">
                <div className="row g-3">
                  <div className="col-md-3">
                    <label className="form-label">Short code</label>
                    <input
                      className="form-control text-uppercase"
                      maxLength={8}
                      placeholder="HAS"
                      value={values.shortCode}
                      onChange={(e) => setValues({ ...values, shortCode: e.target.value })}
                      required
                    />
                  </div>
                  <div className="col-md-9">
                    <label className="form-label">Forward name</label>
                    <input
                      className="form-control"
                      placeholder="has contact"
                      value={values.name}
                      onChange={(e) => setValues({ ...values, name: e.target.value })}
                      required
                    />
                  </div>
                  <div className="col-md-9">
                    <label className="form-label">Inverse name (optional)</label>
                    <input
                      className="form-control"
                      placeholder="is contact of"
                      value={values.inverseName ?? ""}
                      onChange={(e) => setValues({ ...values, inverseName: e.target.value })}
                    />
                  </div>
                  <div className="col-md-3">
                    <label className="form-label">Cardinality</label>
                    <select
                      className="form-select"
                      value={values.cardinality}
                      onChange={(e) =>
                        setValues({ ...values, cardinality: e.target.value as CreateEdgeTypeRequest["cardinality"] })
                      }
                    >
                      <option value="many_to_many">many_to_many</option>
                      <option value="one_to_one">one_to_one</option>
                      <option value="one_to_many">one_to_many</option>
                      <option value="many_to_one">many_to_one</option>
                    </select>
                  </div>
                  <div className="col-md-6">
                    <div className="form-check form-switch">
                      <input
                        type="checkbox"
                        className="form-check-input"
                        id="edgetype-directed"
                        checked={values.isDirected}
                        onChange={(e) => setValues({ ...values, isDirected: e.target.checked })}
                      />
                      <label className="form-check-label" htmlFor="edgetype-directed">
                        Directed (with optional inverse name)
                      </label>
                    </div>
                  </div>
                  <div className="col-md-6">
                    <div className="form-check form-switch">
                      <input
                        type="checkbox"
                        className="form-check-input"
                        id="edgetype-self-ref"
                        checked={values.allowSelfReference}
                        onChange={(e) => setValues({ ...values, allowSelfReference: e.target.checked })}
                      />
                      <label className="form-check-label" htmlFor="edgetype-self-ref">
                        Allow self-references
                      </label>
                    </div>
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

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { message?: string } } }).response;
    return response?.data?.message ?? error.message;
  }
  return String(error);
}
