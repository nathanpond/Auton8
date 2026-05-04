import { useEffect, useState } from "react";
import { createPortal } from "react-dom";

type Props = {
  taskLabel: string;
  currentDueDate: string | null;
  busy: boolean;
  onConfirm: (dueDateIso: string | null) => void;
  onCancel: () => void;
};

// Parses an ISO 8601 string into the "yyyy-MM-dd" form a date input needs.
// We render the date in the user's local timezone (matches what they'd see
// elsewhere in the UI). Returns "" when the input can't be parsed so the
// field shows blank instead of an error.
function isoToInputValue(iso: string | null): string {
  if (!iso) return "";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "";
  const pad = (n: number) => n.toString().padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

// Admin override picker for setting/clearing a runtime task's due date. Empty
// input clears the due date on save.
export default function ChangeDueDateModal({
  taskLabel,
  currentDueDate,
  busy,
  onConfirm,
  onCancel
}: Props) {
  const [value, setValue] = useState<string>(isoToInputValue(currentDueDate));

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape" && !busy) onCancel();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [busy, onCancel]);

  const submit = () => {
    if (value.trim().length === 0) {
      onConfirm(null);
      return;
    }
    // The date input gives us "yyyy-MM-dd" with no time. Anchor to noon UTC
    // so the chosen calendar date round-trips for every timezone from UTC-12
    // through UTC+12. Anchoring to local midnight previously shifted the
    // date one day earlier in UTC for any user east of UTC, since 00:00
    // local on May 3 is May 2 in UTC and the server interprets the
    // timestamp in UTC. UTC+13/UTC+14 (Kiritimati, Tonga) still shift by
    // one day; the only fully timezone-safe fix is sending YYYY-MM-DD as a
    // date-only string, which would require a server-side contract change.
    const [yearStr, monthStr, dayStr] = value.split("-");
    const year = Number(yearStr);
    const month = Number(monthStr);
    const day = Number(dayStr);
    if (!year || !month || !day) {
      onConfirm(null);
      return;
    }
    const noonUtc = new Date(Date.UTC(year, month - 1, day, 12, 0, 0));
    if (Number.isNaN(noonUtc.getTime())) {
      onConfirm(null);
      return;
    }
    onConfirm(noonUtc.toISOString());
  };

  // Portal to document.body so the modal escapes the workflow-execution-modal
  // stacking context. See ReassignTaskModal for the full explanation.
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
              <h5 className="modal-title">Change Due Date</h5>
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
                Change due date for <strong>{taskLabel}</strong>.
              </p>
              <p className="small text-body text-opacity-75 mb-3">
                Currently due:{" "}
                <strong>
                  {currentDueDate ? new Date(currentDueDate).toLocaleDateString() : "(no due date)"}
                </strong>
              </p>
              <label className="form-label" htmlFor="task-due-date">
                New due date
              </label>
              <input
                id="task-due-date"
                type="date"
                className="form-control"
                value={value}
                onChange={(e) => setValue(e.target.value)}
                disabled={busy}
              />
              <p className="form-text small">Leave blank to clear the due date.</p>
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
