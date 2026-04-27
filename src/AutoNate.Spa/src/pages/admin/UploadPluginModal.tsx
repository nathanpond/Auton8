import { useRef, useState } from "react";
import { useUploadPlugin } from "@/hooks/usePlugins";

type Props = {
  onClose: () => void;
};

export default function UploadPluginModal({ onClose }: Props) {
  const upload = useUploadPlugin();
  const inputRef = useRef<HTMLInputElement | null>(null);
  const [file, setFile] = useState<File | null>(null);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    if (!file) {
      setError("Choose a .zip file first.");
      return;
    }
    try {
      await upload.mutateAsync(file);
      onClose();
    } catch (err) {
      setError(describeError(err));
    }
  };

  return (
    <div
      className="modal d-block"
      tabIndex={-1}
      role="dialog"
      style={{ background: "rgba(0,0,0,0.5)" }}
    >
      <div className="modal-dialog modal-dialog-centered" role="document">
        <form className="modal-content" onSubmit={submit}>
          <div className="modal-header">
            <h5 className="modal-title">Upload plugin</h5>
            <button type="button" className="btn-close" aria-label="Close" onClick={onClose} />
          </div>
          <div className="modal-body">
            <p className="text-muted small">
              Choose a plugin <code>.zip</code> file. The archive must contain a{" "}
              <code>plugin.json</code> manifest at the root and the entry assembly listed in it.
            </p>
            <input
              ref={inputRef}
              type="file"
              accept=".zip,application/zip"
              className="form-control"
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            />
            {file && (
              <div className="mt-2 small text-muted">
                Selected: <strong>{file.name}</strong> ({Math.round(file.size / 1024)} KB)
              </div>
            )}
            {error && <div className="alert alert-danger mt-3 mb-0">{error}</div>}
          </div>
          <div className="modal-footer">
            <button type="button" className="btn btn-secondary" onClick={onClose}>
              Cancel
            </button>
            <button
              type="submit"
              className="btn btn-primary"
              disabled={!file || upload.isPending}
            >
              {upload.isPending ? "Uploading…" : "Upload"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

function describeError(err: unknown): string {
  if (typeof err === "object" && err && "response" in err) {
    const resp = (err as { response?: { data?: { error?: string; message?: string } } }).response;
    return resp?.data?.error ?? resp?.data?.message ?? String(err);
  }
  return err instanceof Error ? err.message : String(err);
}
