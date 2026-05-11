import { useState } from "react";
import { Anchor, Badge, Code, Grid, Group, Text } from "@mantine/core";
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

const KIND_BADGE_COLORS: Record<string, string> = {
  type_created: "green",
  type_updated: "cyan",
  type_archived: "yellow",
  type_unarchived: "cyan",
  field_added: "green",
  field_renamed: "cyan",
  field_config_changed: "cyan",
  field_required_changed: "cyan",
  field_reordered: "cyan",
  field_archived: "yellow",
  field_unarchived: "cyan"
};

export default function SchemaAuditPanel({ recordTypeId }: Props) {
  const { data: audit = [], isLoading } = useRecordTypeAudit(recordTypeId);
  const [expanded, setExpanded] = useState<Set<number>>(new Set());

  if (isLoading) {
    return (
      <Text c="dimmed" size="sm">
        Loading history...
      </Text>
    );
  }

  if (audit.length === 0) {
    return (
      <Text c="dimmed" size="sm">
        No schema changes yet.
      </Text>
    );
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
    <ol style={{ listStyle: "none", margin: 0, padding: 0 }}>
      {audit.map((entry) => {
        const label = KIND_LABELS[entry.changeKind] ?? entry.changeKind;
        const color = KIND_BADGE_COLORS[entry.changeKind] ?? "gray";
        const isExpanded = expanded.has(entry.id);
        const summary = describeChange(entry);
        return (
          <li
            key={entry.id}
            style={{
              marginBottom: 8,
              paddingBottom: 8,
              borderBottom: "1px solid var(--mantine-color-default-border)"
            }}
          >
            <Group justify="space-between" align="flex-start" wrap="nowrap">
              <div>
                <Badge color={color} variant="filled" mr={8}>
                  {label}
                </Badge>
                {summary && <span>{summary}</span>}
                <Text size="sm" c="dimmed" mt={4} component="div">
                  <UserBadge userId={entry.changedBy} withByPrefix />
                  <span style={{ margin: "0 8px" }}>·</span>
                  {formatWhen(entry.changedAtUtc)}
                </Text>
              </div>
              {(entry.before !== null || entry.after !== null) && (
                <Anchor component="button" type="button" size="sm" onClick={() => toggle(entry.id)}>
                  {isExpanded ? "Hide details" : "Details"}
                </Anchor>
              )}
            </Group>
            {isExpanded && (
              <Grid mt="xs">
                <Grid.Col span={6}>
                  <Text size="sm" c="dimmed" mb={4}>
                    Before
                  </Text>
                  <Code block style={{ whiteSpace: "pre-wrap", fontSize: 13 }}>
                    {formatJson(entry.before)}
                  </Code>
                </Grid.Col>
                <Grid.Col span={6}>
                  <Text size="sm" c="dimmed" mb={4}>
                    After
                  </Text>
                  <Code block style={{ whiteSpace: "pre-wrap", fontSize: 13 }}>
                    {formatJson(entry.after)}
                  </Code>
                </Grid.Col>
              </Grid>
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
