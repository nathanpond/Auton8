import { useEffect, useMemo, useState } from "react";
import { createPortal } from "react-dom";
import { useUserDirectory, userDisplayName } from "@/hooks/useUserDirectory";
import { useUsers } from "@/hooks/useUsers";

type Props = {
  taskLabel: string;
  currentAssignee: string | null;
  busy: boolean;
  onConfirm: (assignee: string | null) => void;
  onCancel: () => void;
};

// Admin override picker for reassigning a single runtime task. The save
// button posts the chosen userId (or null to clear). Mirrors the spirit of
// ConfirmModal but with a single-select user picker.
export default function ReassignTaskModal({
  taskLabel,
  currentAssignee,
  busy,
  onConfirm,
  onCancel
}: Props) {
  const { data: users = [] } = useUsers();
  const directory = useUserDirectory();
  const [selected, setSelected] = useState<string>(currentAssignee ?? "");

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape" && !busy) onCancel();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [busy, onCancel]);

  const sortedUsers = useMemo(
    () =>
      [...users].sort((a, b) => {
        const an = userDisplayName(a) ?? a.username;
        const bn = userDisplayName(b) ?? b.username;
        return an.localeCompare(bn);
      }),
    [users]
  );

  const currentLabel = (() => {
    if (!currentAssignee) return "(unassigned)";
    const u = directory.get(currentAssignee);
    return userDisplayName(u) ?? currentAssignee;
  })();

  const submit = () => {
    const next = selected.trim().length === 0 ? null : selected;
    onConfirm(next);
  };

  // Portal to document.body so the modal escapes the workflow-execution-modal
  // stacking context. Without the portal the inner modal-backdrop renders
  // *over* the modal because its parent (z-index: 1050, position: fixed) traps
  // descendants in its own stacking context.
  return createPortal(
    <>
      <div
        className="modal fade show d-block"
        tabIndex={-1}
        role="dialog"
        aria-modal="true"
        style={{ zIndex: 1090 }}
      >
        <div className="modal-dialog">
          <div className="modal-content">
            <div className="modal-header">
              <h5 className="modal-title">Reassign Task</h5>
              <button
                type="button"
                className="btn-close"
                aria-label="Close"
                disabled={busy}
                onClick={onCancel}
              />
            </div>
            <div className="modal-body">
              <p className="mb-2">
                Reassign <strong>{taskLabel}</strong>.
              </p>
              <p className="small text-body text-opacity-75 mb-3">
                Currently assigned to: <strong>{currentLabel}</strong>
              </p>
              <label className="form-label" htmlFor="reassign-task-user">
                New assignee
              </label>
              <select
                id="reassign-task-user"
                className="form-select"
                value={selected}
                onChange={(e) => setSelected(e.target.value)}
                disabled={busy}
              >
                <option value="">(unassigned)</option>
                {sortedUsers.map((u) => (
                  <option key={u.userId} value={u.userId}>
                    {userDisplayName(u) ?? u.username}
                  </option>
                ))}
              </select>
            </div>
            <div className="modal-footer">
              <button
                type="button"
                className="btn btn-outline-secondary"
                onClick={onCancel}
                disabled={busy}
              >
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-primary"
                onClick={submit}
                disabled={busy}
              >
                {busy ? "Saving…" : "Save"}
              </button>
            </div>
          </div>
        </div>
      </div>
      <div className="modal-backdrop fade show" style={{ zIndex: 1085 }} />
    </>,
    document.body
  );
}
