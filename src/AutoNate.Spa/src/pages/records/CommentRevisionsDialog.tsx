import { useCommentRevisions } from "@/hooks/useRecordComments";
import { RecordCommentModel } from "@/types/records";
import UserBadge from "./UserBadge";

type Props = {
  recordId: string;
  comment: RecordCommentModel;
  onClose: () => void;
};

export default function CommentRevisionsDialog({ recordId, comment, onClose }: Props) {
  const { data: revisions = [], isLoading } = useCommentRevisions(recordId, comment.id);

  return (
    <>
      <div className="modal fade show d-block" role="dialog" aria-modal="true" tabIndex={-1}>
        <div className="modal-dialog modal-lg">
          <div className="modal-content">
            <div className="modal-header">
              <h5 className="modal-title">Comment edit history</h5>
              <button type="button" className="btn-close" onClick={onClose} aria-label="Close" />
            </div>
            <div className="modal-body">
              <p className="text-body text-opacity-75 small mb-3">
                Created {formatWhen(comment.createdAtUtc)}.
                {comment.isEdited && (
                  <> Last edited {formatWhen(comment.bodyUpdatedAtUtc)}.</>
                )}
              </p>

              <div className="mb-3">
                <h6 className="mb-2">Current</h6>
                <pre className="bg-body-tertiary p-3 rounded mb-0" style={{ whiteSpace: "pre-wrap" }}>
                  {comment.body}
                </pre>
              </div>

              <h6 className="mb-2">Previous versions</h6>
              {isLoading && <p className="text-body text-opacity-50 mb-0">Loading...</p>}
              {!isLoading && revisions.length === 0 && (
                <p className="text-body text-opacity-50 mb-0">No prior edits.</p>
              )}
              {revisions.map((r) => (
                <div key={r.id} className="mb-3">
                  <div className="small text-body text-opacity-75 mb-1">
                    Replaced {formatWhen(r.replacedAtUtc)}{" "}
                    <UserBadge userId={r.replacedBy} withByPrefix />
                  </div>
                  <pre
                    className="bg-body-secondary p-3 rounded mb-0"
                    style={{ whiteSpace: "pre-wrap" }}
                  >
                    {r.body}
                  </pre>
                </div>
              ))}
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

function formatWhen(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}
