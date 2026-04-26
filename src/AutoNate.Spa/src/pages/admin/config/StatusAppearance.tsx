import { useEffect, useRef, useState } from "react";
import { AxiosError } from "axios";
import ColorPicker from "@/components/ColorPicker";
import {
  useCreateStatusAppearance,
  useDeleteStatusAppearance,
  useStatusAppearance,
  useUpdateStatusAppearance
} from "@/hooks/useStatusAppearance";
import { badgeTextColor, normalizeHex } from "@/lib/statusAppearance";
import { StatusAppearanceEntry } from "@/types/statusAppearance";

type DraftStatusAppearanceRow = {
  id: string;
  status: string;
  color: string;
};

export default function StatusAppearance() {
  const { data, isLoading } = useStatusAppearance();
  const createEntry = useCreateStatusAppearance();
  const deleteEntry = useDeleteStatusAppearance();
  const [draftRows, setDraftRows] = useState<DraftStatusAppearanceRow[]>([]);
  const [error, setError] = useState<string | null>(null);
  const rows = Array.isArray(data) ? data : [];

  const addRow = () => {
    setDraftRows((current) => [
      ...current,
      {
        id: `draft-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
        status: "",
        color: "#0d6efd"
      }
    ]);
  };

  const updateDraftRow = (id: string, patch: Partial<DraftStatusAppearanceRow>) => {
    setDraftRows((current) =>
      current.map((row) => (row.id === id ? { ...row, ...patch } : row))
    );
  };

  return (
    <>
      <div className="page-head">
        <h1 className="page-header mb-1">Status Appearance</h1>
        <p className="page-head-copy">
          Configure the status-to-color combinations used for badge previews.
        </p>
      </div>

      <div className="panel panel-inverse">
        <div className="panel-heading d-flex justify-content-between align-items-center">
          <h4 className="panel-title mb-0">Statuses</h4>
          <button type="button" className="btn btn-sm btn-primary" onClick={addRow}>
            <i className="fa fa-plus me-2" />
            Add status
          </button>
        </div>
        <div className="panel-body">
          {error && <div className="alert alert-danger">{error}</div>}

          <div className="table-responsive">
            <table className="table table-striped table-bordered align-middle mb-0">
              <thead>
                <tr>
                  <th style={{ width: "32%" }}>Status</th>
                  <th style={{ width: "34%" }}>Color</th>
                  <th style={{ width: "22%" }}>Preview</th>
                  <th style={{ width: "12%" }}></th>
                </tr>
              </thead>
              <tbody>
                {isLoading && (
                  <tr>
                    <td colSpan={4} className="text-center text-body text-opacity-50 p-4">
                      Loading...
                    </td>
                  </tr>
                )}

                {!isLoading && rows.length === 0 && draftRows.length === 0 && (
                  <tr>
                    <td colSpan={4} className="text-center text-body text-opacity-50 p-4">
                      No statuses yet. Add one to get started.
                    </td>
                  </tr>
                )}

                {rows.map((row) => (
                  <PersistedRow
                    key={row.id}
                    row={row}
                    onDelete={async () => {
                      setError(null);
                      try {
                        await deleteEntry.mutateAsync(row.id);
                      } catch (err) {
                        setError(describeError(err));
                      }
                    }}
                  />
                ))}

                {draftRows.map((row) => (
                  <DraftRow
                    key={row.id}
                    row={row}
                    isSaving={createEntry.isPending}
                    onChange={(patch) => updateDraftRow(row.id, patch)}
                    onDelete={() => setDraftRows((current) => current.filter((x) => x.id !== row.id))}
                    onCreate={async (request) => {
                      setError(null);
                      try {
                        await createEntry.mutateAsync(request);
                        setDraftRows((current) => current.filter((x) => x.id !== row.id));
                      } catch (err) {
                        setError(describeError(err));
                        throw err;
                      }
                    }}
                  />
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </>
  );
}

function PersistedRow({
  row,
  onDelete
}: {
  row: StatusAppearanceEntry;
  onDelete: () => Promise<void>;
}) {
  const updateEntry = useUpdateStatusAppearance();
  const [status, setStatus] = useState(row.status);
  const [color, setColor] = useState(row.color);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setStatus(row.status);
    setColor(row.color);
  }, [row.id, row.status, row.color]);

  useEffect(() => {
    if (status === row.status && color === row.color) return;
    if (!status.trim()) return;

    const timeoutId = window.setTimeout(() => {
      void (async () => {
        setError(null);
        try {
          await updateEntry.mutateAsync({
            id: row.id,
            request: { status: status.trim(), color: color.trim() }
          });
        } catch (err) {
          setError(describeError(err));
        }
      })();
    }, 450);

    return () => window.clearTimeout(timeoutId);
  }, [status, color, row.id, row.status, row.color, updateEntry]);

  return (
    <tr>
      <td>
        <input
          className={`form-control ${error ? "is-invalid" : ""}`}
          placeholder="Enter a status"
          value={status}
          onChange={(e) => setStatus(e.target.value)}
        />
        {error && <div className="invalid-feedback d-block">{error}</div>}
      </td>
      <td>
        <ColorPicker id={`status-color-${row.id}`} value={color} onChange={setColor} />
      </td>
      <td>
        <PreviewBadge status={status} color={color} />
      </td>
      <td className="text-center">
        <button
          type="button"
          className="btn btn-outline-danger btn-sm"
          onClick={() => void onDelete()}
          aria-label={`Delete ${status.trim() || row.status}`}
        >
          <i className="fa fa-trash" />
        </button>
      </td>
    </tr>
  );
}

function DraftRow({
  row,
  isSaving,
  onChange,
  onDelete,
  onCreate
}: {
  row: DraftStatusAppearanceRow;
  isSaving: boolean;
  onChange: (patch: Partial<DraftStatusAppearanceRow>) => void;
  onDelete: () => void;
  onCreate: (request: { status: string; color: string }) => Promise<void>;
}) {
  const attemptedKeyRef = useRef<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const status = row.status.trim();
    if (!status) return;
    const requestKey = `${status}::${row.color.trim().toLowerCase()}`;
    if (attemptedKeyRef.current === requestKey) return;

    const timeoutId = window.setTimeout(() => {
      attemptedKeyRef.current = requestKey;
      void (async () => {
        try {
          setError(null);
          await onCreate({
            status,
            color: row.color.trim()
          });
        } catch (err) {
          setError(describeError(err));
        }
      })();
    }, 450);

    return () => window.clearTimeout(timeoutId);
  }, [row.status, row.color]);

  useEffect(() => {
    attemptedKeyRef.current = null;
    setError(null);
  }, [row.status, row.color]);

  return (
    <tr>
      <td>
        <input
          className={`form-control ${error ? "is-invalid" : ""}`}
          placeholder="Enter a status"
          value={row.status}
          onChange={(e) => onChange({ status: e.target.value })}
        />
        {error && <div className="invalid-feedback d-block">{error}</div>}
      </td>
      <td>
        <ColorPicker
          id={`status-color-${row.id}`}
          value={row.color}
          onChange={(value) => onChange({ color: value })}
        />
      </td>
      <td>
        <PreviewBadge status={row.status} color={row.color} />
        {isSaving && row.status.trim() && (
          <div className="small text-body text-opacity-50 mt-1">Saving...</div>
        )}
      </td>
      <td className="text-center">
        <button
          type="button"
          className="btn btn-outline-danger btn-sm"
          onClick={onDelete}
          aria-label={`Delete ${row.status.trim() || "draft status"}`}
        >
          <i className="fa fa-trash" />
        </button>
      </td>
    </tr>
  );
}

function PreviewBadge({ status, color }: { status: string; color: string }) {
  const backgroundColor = normalizeHex(color) ?? "#6c757d";
  const textColor = badgeTextColor(color);
  const previewText = status.trim() || "Preview";

  return (
    <span
      className="badge rounded-pill px-3 py-2 fw-semibold"
      style={{
        backgroundColor,
        color: textColor,
        display: "inline-block"
      }}
    >
      {previewText}
    </span>
  );
}

function describeError(error: unknown): string {
  const axiosError = error as AxiosError<{ error?: string }>;
  return axiosError.response?.data?.error ?? axiosError.message ?? "Something went wrong.";
}
