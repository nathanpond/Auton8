import { useState } from "react";
import {
  useCreateComment,
  useDeleteComment,
  useEditComment,
  useRecordComments
} from "@/hooks/useRecordComments";
import { RecordCommentModel } from "@/types/records";
import CommentRevisionsDialog from "./CommentRevisionsDialog";
import UserBadge from "./UserBadge";

type Props = {
  recordId: string;
};

export default function CommentsPanel({ recordId }: Props) {
  const [includeDeleted, setIncludeDeleted] = useState(false);
  const { data: comments = [], isLoading } = useRecordComments(recordId, includeDeleted);
  const create = useCreateComment(recordId);
  const edit = useEditComment(recordId);
  const del = useDeleteComment(recordId);

  const [draft, setDraft] = useState("");
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingBody, setEditingBody] = useState("");
  const [revisionsTarget, setRevisionsTarget] = useState<RecordCommentModel | null>(null);
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(null);

  const submitNew = async (e: React.FormEvent) => {
    e.preventDefault();
    if (draft.trim().length === 0) return;
    try {
      await create.mutateAsync(draft);
      setDraft("");
    } catch (err) {
      setFlash({ kind: "error", message: describeError(err) });
    }
  };

  const startEdit = (c: RecordCommentModel) => {
    setEditingId(c.id);
    setEditingBody(c.body);
  };

  const saveEdit = async () => {
    if (!editingId) return;
    try {
      await edit.mutateAsync({ commentId: editingId, body: editingBody });
      setEditingId(null);
      setEditingBody("");
    } catch (err) {
      setFlash({ kind: "error", message: describeError(err) });
    }
  };

  const onDelete = async (id: string) => {
    if (!window.confirm("Delete this comment? It will be hidden but its history is preserved.")) return;
    try {
      await del.mutateAsync(id);
      setFlash({ kind: "success", message: "Deleted." });
    } catch (err) {
      setFlash({ kind: "error", message: describeError(err) });
    }
  };

  return (
    <>
      <div className="d-flex justify-content-between align-items-center mb-3">
        <h5 className="mb-0">Comments</h5>
        <div className="form-check form-switch mb-0">
          <input
            type="checkbox"
            className="form-check-input"
            id="comments-include-deleted"
            checked={includeDeleted}
            onChange={(e) => setIncludeDeleted(e.target.checked)}
          />
          <label className="form-check-label small" htmlFor="comments-include-deleted">
            Show deleted
          </label>
        </div>
      </div>

      {flash && (
        <div
          className={`alert ${flash.kind === "success" ? "alert-success" : "alert-danger"}`}
          role={flash.kind === "success" ? "status" : "alert"}
        >
          {flash.message}
        </div>
      )}

      <form onSubmit={submitNew} className="mb-4">
        <div className="mb-2">
          <textarea
            className="form-control"
            rows={3}
            placeholder="Add a comment..."
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
          />
        </div>
        <div className="text-end">
          <button
            type="submit"
            className="btn btn-primary btn-sm"
            disabled={create.isPending || draft.trim().length === 0}
          >
            <i className="fa fa-comment me-2"></i>Post comment
          </button>
        </div>
      </form>

      {isLoading && <p className="text-body text-opacity-50 mb-0">Loading comments...</p>}

      {!isLoading && comments.length === 0 && (
        <p className="text-body text-opacity-50 mb-0">No comments yet.</p>
      )}

      <ul className="list-unstyled mb-0">
        {comments.map((c) => {
          const isEditing = editingId === c.id;
          return (
            <li
              key={c.id}
              className={`mb-3 pb-3 border-bottom ${c.isDeleted ? "text-body text-opacity-50" : ""}`}
            >
              <div className="d-flex justify-content-between align-items-start small text-body text-opacity-75 mb-1">
                <div>
                  <i className="fa fa-user me-2"></i>
                  <UserBadge userId={c.authorId} />
                  <span className="mx-2">·</span>
                  <span>{formatWhen(c.createdAtUtc)}</span>
                  {c.isEdited && !c.isDeleted && (
                    <button
                      type="button"
                      className="btn btn-link btn-sm p-0 ms-2 align-baseline"
                      onClick={() => setRevisionsTarget(c)}
                    >
                      (edited — view history)
                    </button>
                  )}
                  {c.isDeleted && <span className="badge bg-secondary ms-2">Deleted</span>}
                </div>
                {!c.isDeleted && !isEditing && (
                  <div className="d-flex gap-2">
                    <button
                      type="button"
                      className="btn btn-link btn-sm p-0"
                      onClick={() => startEdit(c)}
                    >
                      Edit
                    </button>
                    <button
                      type="button"
                      className="btn btn-link btn-sm p-0 text-danger"
                      onClick={() => onDelete(c.id)}
                      disabled={del.isPending}
                    >
                      Delete
                    </button>
                  </div>
                )}
              </div>
              {isEditing ? (
                <div>
                  <textarea
                    className="form-control mb-2"
                    rows={3}
                    value={editingBody}
                    onChange={(e) => setEditingBody(e.target.value)}
                  />
                  <div className="text-end">
                    <button
                      type="button"
                      className="btn btn-outline-secondary btn-sm me-2"
                      onClick={() => {
                        setEditingId(null);
                        setEditingBody("");
                      }}
                    >
                      Cancel
                    </button>
                    <button
                      type="button"
                      className="btn btn-primary btn-sm"
                      onClick={saveEdit}
                      disabled={edit.isPending || editingBody.trim().length === 0}
                    >
                      Save
                    </button>
                  </div>
                </div>
              ) : (
                <pre
                  className="mb-0"
                  style={{
                    whiteSpace: "pre-wrap",
                    fontFamily: "inherit",
                    fontSize: "1rem",
                    margin: 0
                  }}
                >
                  {c.body}
                </pre>
              )}
            </li>
          );
        })}
      </ul>

      {revisionsTarget && (
        <CommentRevisionsDialog
          recordId={recordId}
          comment={revisionsTarget}
          onClose={() => setRevisionsTarget(null)}
        />
      )}
    </>
  );
}

function formatWhen(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { message?: string } } }).response;
    return response?.data?.message ?? error.message;
  }
  return String(error);
}
