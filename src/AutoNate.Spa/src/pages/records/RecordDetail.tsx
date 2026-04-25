import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import {
  useArchiveRecord,
  useRecordByKey,
  useRestoreRecord,
  useUpdateRecord
} from "@/hooks/useRecords";
import { useRecordTypeFields, useRecordTypes } from "@/hooks/useRecordTypes";
import "./fields/renderers";
import CommentsPanel from "./CommentsPanel";
import EdgesPanel from "./EdgesPanel";
import RecordForm from "./RecordForm";
import RecordHistoryPanel from "./RecordHistoryPanel";

type Tab = "details" | "edges" | "comments" | "history";

export default function RecordDetail() {
  const { typeShortCode, key = "" } = useParams<{ typeShortCode?: string; key: string }>();
  // When opened via /record/:key the typeShortCode isn't part of the URL, so
  // derive it from the key prefix (keys are formatted "<short_code>-<n>").
  const code = (typeShortCode ?? key.split("-")[0] ?? "").toUpperCase();
  const navigate = useNavigate();

  const { data: types = [] } = useRecordTypes(true);
  const type = types.find((t) => t.shortCode === code) ?? null;
  const { data: record, isLoading } = useRecordByKey(key);
  const { data: fields = [] } = useRecordTypeFields(type?.id ?? null, true);

  const update = useUpdateRecord(record?.id ?? "");
  const archive = useArchiveRecord();
  const restore = useRestoreRecord();

  const [tab, setTab] = useState<Tab>("details");
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(null);

  if (isLoading || !type) {
    return <div className="panel panel-inverse"><div className="panel-body p-4 text-center">Loading...</div></div>;
  }

  if (!record) {
    return (
      <div className="page-head">
        <h1 className="page-header mb-1">Record not found</h1>
        <p className="page-head-copy">
          <code>{key}</code> wasn't found. <Link to={`/records/${code}`}>Back to list</Link>.
        </p>
      </div>
    );
  }

  const toggleArchived = async () => {
    try {
      if (record.isArchived) {
        await restore.mutateAsync(record.id);
        setFlash({ kind: "success", message: "Restored." });
      } else {
        await archive.mutateAsync(record.id);
        setFlash({ kind: "success", message: "Archived." });
      }
    } catch (err) {
      setFlash({ kind: "error", message: describeError(err) });
    }
  };

  return (
    <>
      <div className="page-head d-flex justify-content-between align-items-start">
        <div>
          <h1 className="page-header mb-1">
            <code className="me-2">{record.key}</code>
            {record.name}
            {record.isArchived && <span className="badge bg-secondary ms-2">Archived</span>}
          </h1>
          <p className="page-head-copy mb-0">
            <Link to={`/records/${code}`}>&larr; Back to list</Link>
          </p>
        </div>
        <div>
          <button
            type="button"
            className={`btn ${record.isArchived ? "btn-outline-success" : "btn-outline-warning"}`}
            onClick={toggleArchived}
            disabled={archive.isPending || restore.isPending}
          >
            <i className={`fa ${record.isArchived ? "fa-box-open" : "fa-box-archive"} me-2`}></i>
            {record.isArchived ? "Restore" : "Archive"}
          </button>
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

      <ul className="nav nav-tabs mb-3">
        <li className="nav-item">
          <button
            type="button"
            className={`nav-link ${tab === "details" ? "active" : ""}`}
            onClick={() => setTab("details")}
          >
            Details
          </button>
        </li>
        <li className="nav-item">
          <button
            type="button"
            className={`nav-link ${tab === "edges" ? "active" : ""}`}
            onClick={() => setTab("edges")}
          >
            Edges
          </button>
        </li>
        <li className="nav-item">
          <button
            type="button"
            className={`nav-link ${tab === "comments" ? "active" : ""}`}
            onClick={() => setTab("comments")}
          >
            Comments
          </button>
        </li>
        <li className="nav-item">
          <button
            type="button"
            className={`nav-link ${tab === "history" ? "active" : ""}`}
            onClick={() => setTab("history")}
          >
            History
          </button>
        </li>
      </ul>

      <div className="panel panel-inverse">
        <div className="panel-body">
          {tab === "details" && (
            <RecordForm
              fields={fields}
              initialName={record.name}
              initialStatus={record.status}
              initialDueDate={record.dueDate}
              initialValues={record.values}
              initialAssigneeIds={record.assigneeIds}
              submitLabel="Save"
              onCancel={() => navigate(`/records/${code}`)}
              onSubmit={async ({ name, status, dueDate, values, assigneeIds }) => {
                try {
                  await update.mutateAsync({ name, status, dueDate, values, assigneeIds });
                  setFlash({ kind: "success", message: "Saved." });
                } catch (err) {
                  setFlash({ kind: "error", message: describeError(err) });
                }
              }}
            />
          )}
          {tab === "edges" && <EdgesPanel record={record} />}
          {tab === "comments" && <CommentsPanel recordId={record.id} />}
          {tab === "history" && <RecordHistoryPanel recordId={record.id} fields={fields} />}
        </div>
      </div>
    </>
  );
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { message?: string } } }).response;
    return response?.data?.message ?? error.message;
  }
  return String(error);
}
