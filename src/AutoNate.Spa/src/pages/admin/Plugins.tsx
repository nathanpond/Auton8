import { useState } from "react";
import {
  useDeletePlugin,
  useDisablePlugin,
  useEnablePlugin,
  usePlugins,
} from "@/hooks/usePlugins";
import type { Plugin } from "@/api/plugins";
import UploadPluginModal from "./UploadPluginModal";

export default function Plugins() {
  const { data: plugins = [], isLoading } = usePlugins();
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

  return (
    <>
      <div className="page-head">
        <h1 className="page-header mb-1">Plugins</h1>
        <p className="page-head-copy">
          Runtime-loaded plugins that extend AutoNate via hooks. Upload, enable, disable, or delete here.
        </p>
      </div>

      {error && <div className="alert alert-danger">{error}</div>}

      <div className="panel panel-inverse">
        <div className="panel-heading d-flex justify-content-between align-items-center">
          <h4 className="panel-title">Installed plugins</h4>
          <button
            type="button"
            className="btn btn-sm btn-primary"
            onClick={() => setShowUpload(true)}
          >
            Upload plugin…
          </button>
        </div>
        <div className="panel-body">
          {isLoading && <div>Loading…</div>}
          {!isLoading && plugins.length === 0 && (
            <div className="text-muted">No plugins installed yet.</div>
          )}
          {!isLoading && plugins.length > 0 && (
            <div className="table-responsive">
              <table className="table table-striped align-middle">
                <thead>
                  <tr>
                    <th>Name</th>
                    <th>Version</th>
                    <th>Status</th>
                    <th>Uploaded</th>
                    <th style={{ width: "1%", whiteSpace: "nowrap" }}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {plugins.map((p) => (
                    <tr key={p.id}>
                      <td>
                        <strong>{p.name}</strong>
                        {p.lastError && (
                          <div className="small text-danger" title={p.lastError}>
                            Last error: {p.lastError.length > 80 ? p.lastError.slice(0, 80) + "…" : p.lastError}
                          </div>
                        )}
                      </td>
                      <td className="font-monospace small">{p.version}</td>
                      <td>{renderStatusBadge(p)}</td>
                      <td className="small text-muted">
                        {new Date(p.uploadedAt).toLocaleString()}
                      </td>
                      <td className="text-nowrap">
                        {p.status === "Disabled" && (
                          <button
                            type="button"
                            className="btn btn-sm btn-success me-1"
                            disabled={busyId === p.id}
                            onClick={() => runAction(p.id, () => enable.mutateAsync(p.id))}
                          >
                            Enable
                          </button>
                        )}
                        {p.status === "Enabled" && (
                          <button
                            type="button"
                            className="btn btn-sm btn-warning me-1"
                            disabled={busyId === p.id}
                            onClick={() => runAction(p.id, () => disable.mutateAsync(p.id))}
                          >
                            Disable
                          </button>
                        )}
                        <button
                          type="button"
                          className="btn btn-sm btn-outline-danger"
                          disabled={busyId === p.id}
                          onClick={() => {
                            if (
                              confirm(
                                `Delete plugin '${p.name}'? This removes the row and all uploaded files.`,
                              )
                            ) {
                              void runAction(p.id, () => remove.mutateAsync(p.id));
                            }
                          }}
                        >
                          Delete
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>

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
