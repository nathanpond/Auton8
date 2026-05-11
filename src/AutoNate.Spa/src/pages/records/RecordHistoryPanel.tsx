import { useMemo } from "react";
import { Badge, Text } from "@mantine/core";
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
    return (
      <Text size="sm" c="dimmed">
        Loading history...
      </Text>
    );
  }

  if (entries.length === 0) {
    return (
      <Text size="sm" c="dimmed">
        No history yet.
      </Text>
    );
  }

  return (
    <ol style={{ listStyle: "none", margin: 0, padding: 0 }}>
      {groups.map((group) => (
        <li
          key={group.groupKey}
          style={{
            marginBottom: 16,
            paddingBottom: 16,
            borderBottom: "1px solid var(--mantine-color-default-border)"
          }}
        >
          <Text size="sm" c="dimmed" mb={8}>
            <span>{formatWhen(group.changedAtUtc)}</span>
            <span style={{ margin: "0 8px" }}>·</span>
            <UserBadge userId={group.changedBy} withByPrefix />
          </Text>
          <ul style={{ listStyle: "none", margin: 0, padding: 0, paddingLeft: 16 }}>
            {group.entries.map((entry) => (
              <li key={entry.id} style={{ marginBottom: 4 }}>
                <ChangeLine
                  entry={entry}
                  field={entry.fieldKey ? fieldsByKey.get(entry.fieldKey) ?? null : null}
                />
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
    if (raw === null || raw === undefined) return <em style={{ color: "var(--mantine-color-dimmed)" }}>empty</em>;
    if (field && renderer) return renderer.formatValue(field, raw);
    if (typeof raw === "object") return <code>{JSON.stringify(raw)}</code>;
    return String(raw);
  };

  switch (entry.changeKind) {
    case "created":
      return (
        <Badge color="green" variant="filled">
          Created
        </Badge>
      );
    case "value_changed":
      return (
        <span>
          <code style={{ marginRight: 8 }}>{field?.displayName ?? entry.fieldKey}</code>
          <Text component="span" size="xs" c="dimmed">
            from
          </Text>{" "}
          {formatRaw(entry.oldValue)}{" "}
          <Text component="span" size="xs" c="dimmed">
            →
          </Text>{" "}
          {formatRaw(entry.newValue)}
        </span>
      );
    case "name_changed":
      return (
        <span>
          <Badge color="cyan" variant="filled" mr={8}>
            Name
          </Badge>
          {formatRaw(entry.oldValue)}{" "}
          <Text component="span" size="xs" c="dimmed">
            →
          </Text>{" "}
          {formatRaw(entry.newValue)}
        </span>
      );
    case "assignees_changed":
      return (
        <span>
          <Badge color="cyan" variant="filled" mr={8}>
            Assignees
          </Badge>
          {formatAssigneeArray(entry.oldValue)}{" "}
          <Text component="span" size="xs" c="dimmed">
            →
          </Text>{" "}
          {formatAssigneeArray(entry.newValue)}
        </span>
      );
    case "status_changed":
      return (
        <span>
          <Badge color="cyan" variant="filled" mr={8}>
            Status
          </Badge>
          {formatRaw(entry.oldValue)}{" "}
          <Text component="span" size="xs" c="dimmed">
            →
          </Text>{" "}
          {formatRaw(entry.newValue)}
        </span>
      );
    case "due_date_changed":
      return (
        <span>
          <Badge color="cyan" variant="filled" mr={8}>
            Due Date
          </Badge>
          {formatRaw(entry.oldValue)}{" "}
          <Text component="span" size="xs" c="dimmed">
            →
          </Text>{" "}
          {formatRaw(entry.newValue)}
        </span>
      );
    case "archived":
      return (
        <Badge color="yellow" variant="filled">
          Archived
        </Badge>
      );
    case "unarchived":
      return (
        <Badge color="cyan" variant="filled">
          Restored
        </Badge>
      );
    default:
      return (
        <span>
          <Badge color="gray" variant="filled" mr={8}>
            {entry.changeKind}
          </Badge>
          {entry.fieldKey && <code>{entry.fieldKey}</code>}
        </span>
      );
  }
}

function formatAssigneeArray(raw: unknown): React.ReactNode {
  if (!Array.isArray(raw) || raw.length === 0) {
    return <em style={{ color: "var(--mantine-color-dimmed)" }}>none</em>;
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
    status_changed: 1,
    due_date_changed: 1,
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
