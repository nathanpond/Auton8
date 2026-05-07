import { useState } from "react";
import { TaskFormConfig } from "@/api/executions";

type Props = {
  config: TaskFormConfig;
  onClose: () => void;
  onComplete: (taskId: string) => Promise<void>;
};

// Default user-task UI when the workflow author chose mode="simple" (or left
// the userForm config off entirely). One button → one POST. No form, no
// payload variables.
export default function SimpleCompleteTaskModal({ config, onClose, onComplete }: Props) {
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const onClick = async () => {
    setSubmitting(true);
    setError(null);
    try {
      await onComplete(config.taskId);
      onClose();
    } catch (err) {
      setError(describeError(err));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <>
      <div className="modal fade show d-block" role="dialog" aria-modal="true" tabIndex={-1}>
        <div className="modal-dialog">
          <div className="modal-content">
            <div className="modal-header">
              <h5 className="modal-title">{config.taskName || "Complete task"}</h5>
              <button
                type="button"
                className="btn-close"
                onClick={onClose}
                aria-label="Close"
                disabled={submitting}
              />
            </div>
            <div className="modal-body">
              {config.processInstanceName && (
                <p className="text-body text-opacity-75 mb-2">
                  <i className="fa fa-diagram-project me-2" />
                  {config.processInstanceName}
                </p>
              )}
              {config.description && (
                <p className="mb-2" style={{ whiteSpace: "pre-wrap" }}>
                  {config.description}
                </p>
              )}
              <p>
                Mark this task complete? The workflow will continue from here using the
                process variables already on the instance.
              </p>
              {error && <div className="alert alert-danger">{error}</div>}
            </div>
            <div className="modal-footer">
              <button
                type="button"
                className="btn btn-outline-secondary"
                onClick={onClose}
                disabled={submitting}
              >
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-success"
                onClick={onClick}
                disabled={submitting}
              >
                {submitting ? (
                  <>
                    <i className="fa fa-spinner fa-spin me-2" />
                    Completing…
                  </>
                ) : (
                  <>
                    <i className="fa fa-check me-2" />
                    Complete Task
                  </>
                )}
              </button>
            </div>
          </div>
        </div>
      </div>
      <div className="modal-backdrop fade show" />
    </>
  );
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { reason?: string } } }).response;
    return response?.data?.reason ?? error.message;
  }
  return String(error);
}
