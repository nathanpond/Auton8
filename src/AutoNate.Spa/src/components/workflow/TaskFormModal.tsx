import { useState } from "react";
import { TaskFormConfig } from "@/api/executions";
import { JsxFormHost } from "@/components/JsxFormHost";

type Props = {
  config: TaskFormConfig;
  onClose: () => void;
  onComplete: (taskId: string, variables: Record<string, unknown>) => Promise<void>;
};

// Mode="modal": render the configured Form inside a modal. The form's
// submit payload becomes the workflow variables passed to complete-task.
export default function TaskFormModal({ config, onClose, onComplete }: Props) {
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!config.form) {
    return (
      <NoFormModal
        title={config.taskName || "Complete task"}
        message={
          config.formShortCode
            ? `The form "${config.formShortCode}" referenced by this task could not be loaded.`
            : "This task is configured for a form but no form is selected. Edit the user task in Workflow Studio."
        }
        onClose={onClose}
      />
    );
  }

  const submit = async (payload: Record<string, unknown>) => {
    setSubmitting(true);
    setError(null);
    try {
      await onComplete(config.taskId, payload);
      onClose();
    } catch (err) {
      setError(describeError(err));
      throw err;
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <>
      <div className="modal fade show d-block" role="dialog" aria-modal="true" tabIndex={-1}>
        <div className="modal-dialog modal-lg">
          <div className="modal-content">
            <div className="modal-header">
              <div>
                <h5 className="modal-title">{config.taskName || "Complete task"}</h5>
                {config.processInstanceName && (
                  <small className="text-body text-opacity-75">
                    <i className="fa fa-diagram-project me-2" />
                    {config.processInstanceName}
                  </small>
                )}
              </div>
              <button
                type="button"
                className="btn-close"
                onClick={onClose}
                aria-label="Close"
                disabled={submitting}
              />
            </div>
            <div className="modal-body">
              {config.form.isDraftFallback && (
                <div className="alert alert-warning small">
                  <strong>Heads up:</strong> form <code>{config.form.shortCode}</code> has no
                  published version yet — the draft is being shown.
                </div>
              )}
              {error && <div className="alert alert-danger">{error}</div>}
              <JsxFormHost
                source={config.form.formCode}
                data={config.variables as Record<string, unknown>}
                mode="edit"
                context={{
                  taskId: config.taskId,
                  taskName: config.taskName,
                  taskDefinitionKey: config.taskDefinitionKey,
                  processInstanceId: config.processInstanceId,
                  processInstanceName: config.processInstanceName
                }}
                extras={{ shortCode: config.form.shortCode }}
                onSubmit={submit}
              />
            </div>
          </div>
        </div>
      </div>
      <div className="modal-backdrop fade show" />
    </>
  );
}

function NoFormModal({
  title,
  message,
  onClose
}: {
  title: string;
  message: string;
  onClose: () => void;
}) {
  return (
    <>
      <div className="modal fade show d-block" role="dialog" aria-modal="true" tabIndex={-1}>
        <div className="modal-dialog">
          <div className="modal-content">
            <div className="modal-header">
              <h5 className="modal-title">{title}</h5>
              <button type="button" className="btn-close" onClick={onClose} aria-label="Close" />
            </div>
            <div className="modal-body">
              <div className="alert alert-warning mb-0">{message}</div>
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

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { reason?: string } } }).response;
    return response?.data?.reason ?? error.message;
  }
  return String(error);
}
