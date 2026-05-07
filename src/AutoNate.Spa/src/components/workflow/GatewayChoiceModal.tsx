import { useState } from "react";
import { GATEWAY_CHOICE_VARIABLE, GatewayChoice, TaskFormConfig } from "@/api/executions";

type Props = {
  config: TaskFormConfig;
  onClose: () => void;
  onComplete: (taskId: string, variables?: Record<string, unknown>) => Promise<void>;
};

// Default user-task UI when the workflow author chose mode="simple" AND the
// task flows directly into an exclusive gateway. One button per outgoing
// flow; clicking sets the reserved __autonateChosenFlow variable, which the
// publish-time-injected condition expressions on the gateway use to route.
export default function GatewayChoiceModal({ config, onClose, onComplete }: Props) {
  const [submittingFlowId, setSubmittingFlowId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const choices = config.gatewayChoices ?? [];

  const onPick = async (choice: GatewayChoice) => {
    setSubmittingFlowId(choice.flowId);
    setError(null);
    try {
      await onComplete(config.taskId, { [GATEWAY_CHOICE_VARIABLE]: choice.flowId });
      onClose();
    } catch (err) {
      setError(describeError(err));
    } finally {
      setSubmittingFlowId(null);
    }
  };

  const submitting = submittingFlowId !== null;

  return (
    <>
      <div className="modal fade show d-block" role="dialog" aria-modal="true" tabIndex={-1}>
        <div className="modal-dialog">
          <div className="modal-content">
            <div className="modal-header">
              <h5 className="modal-title">{config.taskName || "Choose a path"}</h5>
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
                <p className="mb-3" style={{ whiteSpace: "pre-wrap" }}>
                  {config.description}
                </p>
              )}
              <p className="mb-2">Pick a path to continue:</p>
              {error && <div className="alert alert-danger">{error}</div>}
            </div>
            <div className="modal-footer flex-wrap gap-2">
              <button
                type="button"
                className="btn btn-outline-secondary me-auto"
                onClick={onClose}
                disabled={submitting}
              >
                Cancel
              </button>
              {choices.map((choice) => {
                const isThisOne = submittingFlowId === choice.flowId;
                return (
                  <button
                    key={choice.flowId}
                    type="button"
                    className="btn btn-primary"
                    onClick={() => onPick(choice)}
                    disabled={submitting}
                    title={choice.description ?? undefined}
                  >
                    {isThisOne ? (
                      <>
                        <i className="fa fa-spinner fa-spin me-2" />
                        {choice.label}
                      </>
                    ) : (
                      choice.label
                    )}
                  </button>
                );
              })}
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
