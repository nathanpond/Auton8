import { FormVersion } from "@/api/forms";
import { useFormVersions, useRestoreFormVersion } from "@/hooks/useForms";

type Props = {
  formId: string;
  onClose: () => void;
  onRestored?: () => void;
};

export default function FormVersions({ formId, onClose, onRestored }: Props) {
  const { data: versions = [], isLoading } = useFormVersions(formId);
  const restore = useRestoreFormVersion();

  const onRestore = async (versionNumber: number) => {
    if (!window.confirm(`Restore v${versionNumber}? A new draft version will be appended.`)) {
      return;
    }
    await restore.mutateAsync({ id: formId, versionNumber });
    onRestored?.();
  };

  return (
    <>
      <div
        className="modal fade show d-block"
        role="dialog"
        aria-modal="true"
        tabIndex={-1}
      >
        <div className="modal-dialog modal-lg">
          <div className="modal-content">
            <div className="modal-header">
              <h5 className="modal-title">Version history</h5>
              <button
                type="button"
                className="btn-close"
                onClick={onClose}
                aria-label="Close"
              />
            </div>
            <div className="modal-body">
              {isLoading && <div className="text-muted">Loading…</div>}
              {!isLoading && versions.length === 0 && (
                <div className="text-muted">No versions yet.</div>
              )}
              {!isLoading && versions.length > 0 && (
                <div className="table-responsive">
                  <table className="table table-sm align-middle">
                    <thead>
                      <tr>
                        <th style={{ width: "5rem" }}>v</th>
                        <th style={{ width: "7rem" }}>Kind</th>
                        <th>When</th>
                        <th>Note</th>
                        <th style={{ width: "8rem" }}></th>
                      </tr>
                    </thead>
                    <tbody>
                      {versions.map((v) => (
                        <VersionRow
                          key={v.id}
                          version={v}
                          isPending={restore.isPending}
                          onRestore={() => onRestore(v.versionNumber)}
                        />
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
            <div className="modal-footer">
              <button type="button" className="btn btn-outline-secondary" onClick={onClose}>
                Close
              </button>
            </div>
          </div>
        </div>
      </div>
      <div className="modal-backdrop fade show" />
    </>
  );
}

function VersionRow({
  version,
  isPending,
  onRestore
}: {
  version: FormVersion;
  isPending: boolean;
  onRestore: () => void;
}) {
  return (
    <tr>
      <td>
        <code>v{version.versionNumber}</code>
      </td>
      <td>
        <KindBadge kind={version.kind} />
      </td>
      <td>{formatWhen(version.createdAtUtc)}</td>
      <td>{version.note ?? ""}</td>
      <td>
        <button
          type="button"
          className="btn btn-outline-secondary btn-sm"
          onClick={onRestore}
          disabled={isPending}
        >
          Restore
        </button>
      </td>
    </tr>
  );
}

function KindBadge({ kind }: { kind: FormVersion["kind"] }) {
  if (kind === "publish") {
    return <span className="badge bg-success">Publish</span>;
  }
  if (kind === "restore") {
    return <span className="badge bg-info text-dark">Restore</span>;
  }
  return <span className="badge bg-secondary">Save</span>;
}

function formatWhen(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}
