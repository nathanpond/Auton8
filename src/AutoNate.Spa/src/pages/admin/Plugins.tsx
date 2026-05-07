import { useMemo, useState } from "react";
import { ColumnDef } from "@tanstack/react-table";
import {
  useDeletePlugin,
  useDisablePlugin,
  useEnablePlugin,
} from "@/hooks/usePlugins";
import { Plugin, listPlugins } from "@/api/plugins";
import { DataTable } from "@/components/data-table/DataTable";
import UploadPluginModal from "./UploadPluginModal";

const COLUMN_WIDTHS = ["32%", "14%", "14%", "22%", "18%"];

export default function Plugins() {
  const enable = useEnablePlugin();
  const disable = useDisablePlugin();
  const remove = useDeletePlugin();
  const [showUpload, setShowUpload] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const runAction = async (id: string, action: () => Promise<unknown>) => {
    setError(null);
    setBusyId(id);
    try {
      await action();
    } catch (err) {
      setError(describeError(err));
    } finally {
      setBusyId(null);
    }
  };

  const columns = useMemo<ColumnDef<Plugin>[]>(
    () => [
      {
        id: "name",
        accessorKey: "name",
        header: "Name",
        cell: ({ row }) => (
          <>
            <strong>{row.original.name}</strong>
            {row.original.lastError && (
              <div className="small text-danger" title={row.original.lastError}>
                Last error:{" "}
                {row.original.lastError.length > 80
                  ? row.original.lastError.slice(0, 80) + "…"
                  : row.original.lastError}
              </div>
            )}
          </>
        )
      },
      {
        id: "version",
        accessorKey: "version",
        header: "Version",
        cell: ({ row }) => <span className="font-monospace small">{row.original.version}</span>
      },
      {
        id: "status",
        accessorKey: "status",
        header: "Status",
        cell: ({ row }) => renderStatusBadge(row.original)
      },
      {
        id: "uploadedAt",
        header: "Uploaded",
        accessorFn: (p) => p.uploadedAt,
        cell: ({ row }) => (
          <span className="small text-muted">{new Date(row.original.uploadedAt).toLocaleString()}</span>
        )
      },
      {
        id: "actions",
        header: "Actions",
        enableSorting: false,
        enableGlobalFilter: false,
        cell: ({ row }) => {
          const p = row.original;
          return (
            <div className="data-table-row-actions">
              {p.status === "Disabled" && (
                <button
                  type="button"
                  className="btn btn-sm btn-success"
                  disabled={busyId === p.id}
                  onClick={(e) => {
                    e.stopPropagation();
                    void runAction(p.id, () => enable.mutateAsync(p.id));
                  }}
                >
                  Enable
                </button>
              )}
              {p.status === "Enabled" && (
                <button
                  type="button"
                  className="btn btn-sm btn-warning"
                  disabled={busyId === p.id}
                  onClick={(e) => {
                    e.stopPropagation();
                    void runAction(p.id, () => disable.mutateAsync(p.id));
                  }}
                >
                  Disable
                </button>
              )}
              <button
                type="button"
                className="btn btn-sm btn-outline-danger"
                disabled={busyId === p.id}
                onClick={(e) => {
                  e.stopPropagation();
                  if (
                    confirm(
                      `Delete plugin '${p.name}'? This removes the row and all uploaded files.`
                    )
                  ) {
                    void runAction(p.id, () => remove.mutateAsync(p.id));
                  }
                }}
              >
                Delete
              </button>
            </div>
          );
        }
      }
    ],
    [busyId, enable, disable, remove]
  );

  return (
    <>
      <div className="page-head">
        <h1 className="page-header mb-1">Plugins</h1>
        <p className="page-head-copy">
          Runtime-loaded plugins that extend AutoNate via hooks. Upload, enable, disable, or delete here.
        </p>
      </div>

      {error && <div className="alert alert-danger">{error}</div>}

      <DataTable<Plugin>
        mode="client"
        loadAll={() => listPlugins()}
        queryKey={["admin", "plugins"]}
        columns={columns}
        rowKey={(p) => p.id}
        columnWidths={COLUMN_WIDTHS}
        initialSort={[{ id: "name", desc: false }]}
        searchPlaceholder="Search plugins…"
        emptyMessage="No plugins installed yet."
        loadingMessage="Loading plugins…"
        globalFilterFn={(p, search) => {
          const needle = search.toLowerCase();
          return `${p.name} ${p.version}`.toLowerCase().includes(needle);
        }}
        toolbarRight={
          <button
            type="button"
            className="btn btn-add-user"
            onClick={() => setShowUpload(true)}
          >
            <i className="fa fa-plus me-2"></i>Upload plugin
          </button>
        }
      />

      {showUpload && <UploadPluginModal onClose={() => setShowUpload(false)} />}
    </>
  );
}

function renderStatusBadge(p: Plugin) {
  if (p.lastError) {
    return <span className="badge bg-danger">Error</span>;
  }
  switch (p.status) {
    case "Enabled":
      return <span className="badge bg-success">Enabled</span>;
    case "Disabled":
      return <span className="badge bg-secondary">Disabled</span>;
    case "DeletedPending":
      return <span className="badge bg-warning text-dark">Deleted (pending)</span>;
  }
}

function describeError(err: unknown): string {
  if (typeof err === "object" && err && "response" in err) {
    const resp = (err as { response?: { data?: { error?: string; message?: string } } }).response;
    return resp?.data?.error ?? resp?.data?.message ?? String(err);
  }
  return err instanceof Error ? err.message : String(err);
}
