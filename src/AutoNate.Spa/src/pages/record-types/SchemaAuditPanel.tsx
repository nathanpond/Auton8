import { useState } from "react";
import { useRecordTypeAudit } from "@/hooks/useRecordTypes";
import { RecordTypeAuditEntry } from "@/types/records";
import UserBadge from "../records/UserBadge";

type Props = {
  recordTypeId: string;
};

const KIND_LABELS: Record<string, string> = {
  type_created: "Record type created",
  type_updated: "Type details updated",
  type_archived: "Type archived",
  type_unarchived: "Type restored",
  field_added: "Field added",
  field_renamed: "Field renamed",
  field_config_changed: "Field config changed",
  field_required_changed: "Required toggled",
  field_reordered: "Field reordered",
  field_archived: "Field archived",
  field_unarchived: "Field restored"
};

const KIND_BADGES: Record<string, string> = {
  type_created: "bg-success",
  type_updated: "bg-info text-dark",
  type_archived: "bg-warning text-dark",
  type_unarchived: "bg-info text-dark",
  field_added: "bg-success",
  field_renamed: "bg-info text-dark",
  field_config_changed: "bg-info text-dark",
  field_required_changed: "bg-info text-dark",
  field_reordered: "bg-info text-dark",
  field_archived: "bg-warning text-dark",
  field_unarchived: "bg-info text-dark"
};

export default function SchemaAuditPanel({ recordTypeId }: Props) {
  const { data: audit = [], isLoading } = useRecordTypeAudit(recordTypeId);
  const [expanded, setExpanded] = useState<Set<number>>(new Set());

  if (isLoading) {
    return <p className="text-body text-opacity-50 mb-0">Loading history...</p>;
  }

  if (audit.length === 0) {
    return <p className="text-body text-opacity-50 mb-0">No schema changes yet.</p>;
  }

  const toggle = (id: number) => {
    setExpanded((s) => {
      const next = new Set(s);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  return (
    <ol className="list-unstyled mb-0">
      {audit.map((entry) => {
        const label = KIND_LABELS[entry.changeKind] ?? entry.changeKind;
        const badge = KIND_BADGES[entry.changeKind] ?? "bg-secondary";
        const isExpanded = expanded.has(entry.id);
        const summary = describeChange(entry);
        return (
          <li key={entry.id} className="mb-2 pb-2 border-bottom">
            <div className="d-flex justify-content-between align-items-start">
              <div>
                <span className={`badge ${badge} me-2`}>{label}</span>
                {summary && <span>{summary}</span>}
                <div className="small text-body text-opacity-75 mt-1">
                  <UserBadge userId={entry.changedBy} withByPrefix />
                  <span className="mx-2">·</span>
                  {formatWhen(entry.changedAtUtc)}
                </div>
              </div>
              {(entry.before !== null || entry.after !== null) && (
                <button
                  type="button"
                  className="btn btn-link btn-sm p-0"
                  onClick={() => toggle(entry.id)}
                >
                  {isExpanded ? "Hide details" : "Details"}
                </button>
              )}
            </div>
            {isExpanded && (
              <div className="row g-2 mt-2">
                <div className="col">
                  <div className="small text-body text-opacity-75 mb-1">Before</div>
                  <pre className="bg-body-secondary p-2 rounded mb-0 small" style={{ whiteSpace: "pre-wrap" }}>
                    {formatJson(entry.before)}
                  </pre>
                </div>
                <div className="col">
                  <div className="small text-body text-opacity-75 mb-1">After</div>
                  <pre className="bg-body-tertiary p-2 rounded mb-0 small" style={{ whiteSpace: "pre-wrap" }}>
                    {formatJson(entry.after)}
                  </pre>
                </div>
              </div>
            )}
          </li>
        );
      })}
    </ol>
  );
}

/**
 * One-line summary for the most common change kinds. Pulls the human-relevant
 * bits out of the before/after JSONB blobs so the user doesn't have to expand
 * the row to know what happened.
 */
function describeChange(entry: RecordTypeAuditEntry): string {
  const after = (entry.after ?? {}) as Record<string, unknown>;
  const before = (entry.before ?? {}) as Record<string, unknown>;

  switch (entry.changeKind) {
    case "type_created":
      return typeof after.short_code === "string"
        ? `${after.short_code} (${after.name ?? ""})`
        : "";
    case "type_updated":
      return typeof after.name === "string" ? `name: ${after.name}` : "";
    case "field_added": {
      const data = (after.data ?? {}) as Record<string, unknown>;
      return data.field_key ? `${data.field_key} (${data.data_type ?? "?"})` : "";
    }
    case "field_renamed": {
      const beforeData = (before.data ?? {}) as Record<string, unknown>;
      const afterData = (after.data ?? {}) as Record<string, unknown>;
      return `${beforeData.display_name ?? "?"} → ${afterData.display_name ?? "?"}`;
    }
    case "field_required_changed": {
      const afterData = (after.data ?? {}) as Record<string, unknown>;
      return `required = ${afterData.is_required}`;
    }
    case "field_reordered": {
      const beforeData = (before.data ?? {}) as Record<string, unknown>;
      const afterData = (after.data ?? {}) as Record<string, unknown>;
      return `position ${beforeData.sort_order ?? "?"} → ${afterData.sort_order ?? "?"}`;
    }
    case "field_archived":
    case "field_unarchived":
    case "field_config_changed": {
      const data = (after.data ?? before.data ?? {}) as Record<string, unknown>;
      const fieldId = (after.field_id ?? before.field_id) as string | undefined;
      return data.field_key
        ? String(data.field_key)
        : fieldId
          ? `field ${fieldId.substring(0, 8)}`
          : "";
    }
    default:
      return "";
  }
}

function formatJson(value: unknown): string {
  if (value === null || value === undefined) return "—";
  return JSON.stringify(value, null, 2);
}

function formatWhen(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}
