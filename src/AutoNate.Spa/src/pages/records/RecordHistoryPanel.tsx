import { useMemo } from "react";
import { useRecordHistory } from "@/hooks/useRecords";
import { RecordHistoryEntry, RecordTypeField } from "@/types/records";
import { getRenderer } from "./fields/registry";
import UserBadge from "./UserBadge";

type Props = {
  recordId: string;
  fields: RecordTypeField[];
};

type ChangeGroup = {
  groupKey: string;
  changedAtUtc: string;
  changedBy: string;
  entries: RecordHistoryEntry[];
};

export default function RecordHistoryPanel({ recordId, fields }: Props) {
  const { data: entries = [], isLoading } = useRecordHistory(recordId);
  const fieldsByKey = useMemo(
    () => new Map(fields.map((f) => [f.fieldKey, f] as const)),
    [fields]
  );

  const groups = useMemo(() => groupByChangeSet(entries), [entries]);

  if (isLoading) {
    return <p className="text-body text-opacity-50 mb-0">Loading history...</p>;
  }

  if (entries.length === 0) {
    return <p className="text-body text-opacity-50 mb-0">No history yet.</p>;
  }

  return (
    <ol className="list-unstyled mb-0">
      {groups.map((group) => (
        <li key={group.groupKey} className="mb-3 pb-3 border-bottom">
          <div className="small text-body text-opacity-75 mb-2">
            <span>{formatWhen(group.changedAtUtc)}</span>
            <span className="mx-2">·</span>
            <UserBadge userId={group.changedBy} withByPrefix />
          </div>
          <ul className="list-unstyled mb-0 ps-3">
            {group.entries.map((entry) => (
              <li key={entry.id} className="mb-1">
                <ChangeLine entry={entry} field={entry.fieldKey ? fieldsByKey.get(entry.fieldKey) ?? null : null} />
              </li>
            ))}
          </ul>
        </li>
      ))}
    </ol>
  );
}

function ChangeLine({
  entry,
  field
}: {
  entry: RecordHistoryEntry;
  field: RecordTypeField | null;
}) {
  const renderer = field ? getRenderer(field.dataType) : null;
  const formatRaw = (raw: unknown) => {
    if (raw === null || raw === undefined) return <em className="text-body text-opacity-50">empty</em>;
    if (field && renderer) return renderer.formatValue(field, raw);
    if (typeof raw === "object") return <code>{JSON.stringify(raw)}</code>;
    return String(raw);
  };

  switch (entry.changeKind) {
    case "created":
      return (
        <span>
          <span className="badge bg-success me-2">Created</span>
        </span>
      );
    case "value_changed":
      return (
        <span>
          <code className="me-2">{field?.displayName ?? entry.fieldKey}</code>
          <span className="text-body text-opacity-75">from</span> {formatRaw(entry.oldValue)}{" "}
          <span className="text-body text-opacity-75">→</span> {formatRaw(entry.newValue)}
        </span>
      );
    case "name_changed":
      return (
        <span>
          <span className="badge bg-info text-dark me-2">Name</span>
          {formatRaw(entry.oldValue)}
          <span className="text-body text-opacity-75 mx-2">→</span>
          {formatRaw(entry.newValue)}
        </span>
      );
    case "assignees_changed":
      return (
        <span>
          <span className="badge bg-info text-dark me-2">Assignees</span>
          {formatAssigneeArray(entry.oldValue)}
          <span className="text-body text-opacity-75 mx-2">→</span>
          {formatAssigneeArray(entry.newValue)}
        </span>
      );
    case "archived":
      return <span className="badge bg-warning text-dark">Archived</span>;
    case "unarchived":
      return <span className="badge bg-info text-dark">Restored</span>;
    default:
      return (
        <span>
          <span className="badge bg-secondary me-2">{entry.changeKind}</span>
          {entry.fieldKey && <code className="me-2">{entry.fieldKey}</code>}
        </span>
      );
  }
}

function formatAssigneeArray(raw: unknown): React.ReactNode {
  if (!Array.isArray(raw) || raw.length === 0) {
    return <em className="text-body text-opacity-50">none</em>;
  }
  return (
    <span>
      {(raw as string[]).map((id, i) => (
        <span key={id}>
          {i > 0 && ", "}
          <UserBadge userId={id} />
        </span>
      ))}
    </span>
  );
}

/**
 * Bundles history entries that came from the same mutation. Groups by
 * `changeSetId` when present (rows tagged by the store), and falls back to
 * `(changedBy, changedAtUtc)` for legacy rows that predate change-set tagging.
 *
 * Result order matches the input (already sorted newest-first by the API).
 */
function groupByChangeSet(entries: RecordHistoryEntry[]): ChangeGroup[] {
  const groups: ChangeGroup[] = [];
  const seen = new Map<string, ChangeGroup>();

  for (const entry of entries) {
    const key = entry.changeSetId ?? `legacy:${entry.changedBy}|${entry.changedAtUtc}`;
    let group = seen.get(key);
    if (!group) {
      group = {
        groupKey: key,
        changedAtUtc: entry.changedAtUtc,
        changedBy: entry.changedBy,
        entries: []
      };
      seen.set(key, group);
      groups.push(group);
    }
    group.entries.push(entry);
  }

  // Sort each group's entries so 'created'/'name_changed' top the list before
  // value_changed for a more readable summary.
  const order: Record<string, number> = {
    created: 0,
    name_changed: 1,
    archived: 2,
    unarchived: 2,
    assignees_changed: 3,
    value_changed: 4
  };
  for (const g of groups) {
    g.entries.sort((a, b) => (order[a.changeKind] ?? 9) - (order[b.changeKind] ?? 9));
  }

  return groups;
}

function formatWhen(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}
