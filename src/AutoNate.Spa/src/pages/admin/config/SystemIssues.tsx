import { useMemo, useState } from "react";
import { isAxiosError } from "axios";
import type { DataTableColumn } from "@/components/data-table/DataTable";
import {
  Alert,
  Badge,
  Box,
  Button,
  Code,
  Grid,
  Group,
  Modal,
  NativeSelect,
  Stack,
  Text,
  TextInput
} from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import {
  SystemIssueCategory,
  SystemIssueModel,
  SystemIssueSeverity,
  SystemIssueState,
  listSystemIssues
} from "@/api/systemIssues";
import {
  useAcknowledgeSystemIssue,
  useResolveSystemIssue,
  useSystemIssue
} from "@/hooks/useSystemIssues";
import { DataTable } from "@/components/data-table/DataTable";

const STATE_OPTIONS: { value: SystemIssueState | ""; label: string }[] = [
  { value: "open", label: "Open" },
  { value: "acknowledged", label: "Acknowledged" },
  { value: "auto_resolved", label: "Auto-resolved" },
  { value: "resolved", label: "Resolved" },
  { value: "", label: "All states" }
];

const SEVERITY_OPTIONS: { value: SystemIssueSeverity | ""; label: string }[] = [
  { value: "", label: "Any severity" },
  { value: "critical", label: "Critical" },
  { value: "error", label: "Error" },
  { value: "warning", label: "Warning" },
  { value: "info", label: "Info" }
];

const CATEGORY_OPTIONS: { value: SystemIssueCategory | ""; label: string }[] = [
  { value: "", label: "Any category" },
  { value: "data_integrity", label: "Data integrity" },
  { value: "workflow", label: "Workflow" },
  { value: "bus", label: "Bus" },
  { value: "auth", label: "Auth" },
  { value: "config", label: "Config" },
  { value: "resource", label: "Resource" },
  { value: "plugin", label: "Plugin" },
  { value: "unhandled", label: "Unhandled" }
];

const COLUMN_WIDTHS = ["10%", "38%", "13%", "16%", "8%", "15%"];

export default function SystemIssues() {
  const [state, setState] = useState<SystemIssueState | "">("open");
  const [severity, setSeverity] = useState<SystemIssueSeverity | "">("");
  const [category, setCategory] = useState<SystemIssueCategory | "">("");
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const detail = useSystemIssue(selectedId);

  const columns = useMemo<DataTableColumn<SystemIssueModel>[]>(
    () => [
      {
        id: "severity",
        accessorKey: "severity",
        header: "Severity",
        cell: ({ row }) => <SeverityBadge severity={row.original.severity} />
      },
      {
        id: "title",
        accessorKey: "title",
        header: "Title",
        meta: { wrap: true },
        cell: ({ row }) => (
          <>
            <Text fw={600}>{row.original.title}</Text>
            {row.original.summary && (
              <Text size="sm" c="dimmed">
                {row.original.summary}
              </Text>
            )}
          </>
        )
      },
      {
        id: "category",
        accessorKey: "category",
        header: "Category",
        cell: ({ row }) => (
          <Text size="sm" c="dimmed" component="span">
            {row.original.category}
          </Text>
        )
      },
      {
        id: "detectorId",
        accessorKey: "detectorId",
        header: "Detector",
        cell: ({ row }) => (
          <Text size="sm" c="dimmed" component="span">
            {row.original.detectorId}
          </Text>
        )
      },
      {
        id: "occurrenceCount",
        accessorKey: "occurrenceCount",
        header: "Count"
      },
      {
        id: "lastSeenAtUtc",
        accessorKey: "lastSeenAtUtc",
        header: "Last seen",
        cell: ({ row }) => (
          <Text size="sm" c="dimmed" component="span">
            {new Date(row.original.lastSeenAtUtc).toLocaleString()}
          </Text>
        )
      }
    ],
    []
  );

  return (
    <>
      <PageHeader
        title="System Issues"
        description="Persistent log of issues detectors have surfaced. Refreshes every 15 seconds."
      />

      <DataTable<SystemIssueModel>
        mode="client"
        loadAll={async () => {
          const r = await listSystemIssues({
            state,
            severity: severity || undefined,
            category: category || undefined
          });
          return r.items;
        }}
        queryKey={["system-issues", { state, severity, category }]}
        refetchInterval={15_000}
        columns={columns}
        rowKey={(i) => i.id}
        columnWidths={COLUMN_WIDTHS}
        initialSort={[{ id: "lastSeenAtUtc", desc: true }]}
        searchPlaceholder="Search issues…"
        emptyMessage="No issues match the current filters."
        loadingMessage="Loading issues…"
        onRowClick={(i) => setSelectedId(i.id)}
        getRowAriaLabel={(i) => `Open ${i.title}`}
        globalFilterFn={(i, search) => {
          const needle = search.toLowerCase();
          return `${i.title} ${i.summary ?? ""} ${i.category} ${i.detectorId}`
            .toLowerCase()
            .includes(needle);
        }}
        toolbarLeft={
          <Group gap="xs" align="center">
            <NativeSelect
              size="xs"
              style={{ width: "auto" }}
              value={state}
              onChange={(e) => setState(e.currentTarget.value as SystemIssueState | "")}
              aria-label="Filter by state"
              data={STATE_OPTIONS}
            />
            <NativeSelect
              size="xs"
              style={{ width: "auto" }}
              value={severity}
              onChange={(e) => setSeverity(e.currentTarget.value as SystemIssueSeverity | "")}
              aria-label="Filter by severity"
              data={SEVERITY_OPTIONS}
            />
            <NativeSelect
              size="xs"
              style={{ width: "auto" }}
              value={category}
              onChange={(e) => setCategory(e.currentTarget.value as SystemIssueCategory | "")}
              aria-label="Filter by category"
              data={CATEGORY_OPTIONS}
            />
          </Group>
        }
      />

      {selectedId && (
        <IssueDetailDrawer
          issue={detail.data ?? null}
          isLoading={detail.isLoading}
          onClose={() => setSelectedId(null)}
        />
      )}
    </>
  );
}

function SeverityBadge({ severity }: { severity: string }) {
  const color =
    severity === "critical"
      ? "red"
      : severity === "error"
        ? "red"
        : severity === "warning"
          ? "yellow"
          : "gray";
  return (
    <Badge color={color} variant="filled">
      {severity}
    </Badge>
  );
}

function IssueDetailDrawer({
  issue,
  isLoading,
  onClose
}: {
  issue: SystemIssueModel | null;
  isLoading: boolean;
  onClose: () => void;
}) {
  const facts = useMemo(() => prettyJson(issue?.factsJson), [issue?.factsJson]);
  const acknowledge = useAcknowledgeSystemIssue();
  const resolve = useResolveSystemIssue();
  const [resolveNotes, setResolveNotes] = useState("");

  const isOpen = issue?.state === "open";
  const isOpenOrAcknowledged = issue?.state === "open" || issue?.state === "acknowledged";
  const lastError: unknown = acknowledge.error ?? resolve.error;

  return (
    <Modal
      opened
      onClose={onClose}
      title={issue?.title ?? (isLoading ? "Loading…" : "Issue")}
      size="lg"
      scrollAreaComponent={undefined}
    >
      {lastError ? (
        <Alert color="red" variant="light" mb="md">
          {describeError(lastError)}
        </Alert>
      ) : null}
      {isLoading || !issue ? (
        <Text c="dimmed">Loading…</Text>
      ) : (
        <Stack gap={4}>
          <DetailRow label="State">{issue.state}</DetailRow>
          <DetailRow label="Severity">
            <SeverityBadge severity={issue.severity} />
          </DetailRow>
          <DetailRow label="Category">{issue.category}</DetailRow>
          <DetailRow label="Detector">
            <Code>{issue.detectorId}</Code>
          </DetailRow>
          <DetailRow label="Fingerprint">
            <Code style={{ fontSize: 13 }}>{issue.fingerprint}</Code>
          </DetailRow>
          {issue.summary && (
            <DetailRow label="Summary">
              <Text style={{ whiteSpace: "pre-wrap" }}>{issue.summary}</Text>
            </DetailRow>
          )}
          {issue.relatedEntityKind && (
            <DetailRow label="Related">
              <Code>
                {issue.relatedEntityKind}/{issue.relatedEntityId ?? "?"}
              </Code>
            </DetailRow>
          )}
          <DetailRow label="Occurrences">{issue.occurrenceCount}</DetailRow>
          <DetailRow label="First seen">
            <Text size="sm">{new Date(issue.firstSeenAtUtc).toLocaleString()}</Text>
          </DetailRow>
          <DetailRow label="Last seen">
            <Text size="sm">{new Date(issue.lastSeenAtUtc).toLocaleString()}</Text>
          </DetailRow>
          {issue.resolvedAtUtc && (
            <DetailRow label="Resolved">
              <Text size="sm">
                {new Date(issue.resolvedAtUtc).toLocaleString()} ({issue.resolutionKind})
              </Text>
            </DetailRow>
          )}
          {issue.autoRemediationLastError && (
            <DetailRow label="Last remediation error">
              <Text size="sm" c="red">
                {issue.autoRemediationLastError}
              </Text>
            </DetailRow>
          )}
          <DetailRow label="Facts">
            <Code
              block
              style={{ maxHeight: 300, overflow: "auto", fontSize: 13, whiteSpace: "pre-wrap" }}
            >
              {facts}
            </Code>
          </DetailRow>
        </Stack>
      )}
      <Group justify="flex-end" wrap="wrap" gap="xs" mt="md">
        {isOpenOrAcknowledged && (
          <Box style={{ minWidth: 260, flex: "1 1 260px", marginRight: "auto" }}>
            <TextInput
              size="xs"
              placeholder="Resolution notes (optional)"
              value={resolveNotes}
              onChange={(e) => setResolveNotes(e.currentTarget.value)}
              disabled={resolve.isPending}
            />
          </Box>
        )}
        {isOpen && (
          <Button
            variant="outline"
            disabled={!issue}
            loading={acknowledge.isPending}
            onClick={() => issue && acknowledge.mutate(issue.id)}
          >
            Acknowledge
          </Button>
        )}
        {isOpenOrAcknowledged && (
          <Button
            color="green"
            disabled={!issue}
            loading={resolve.isPending}
            onClick={() => {
              if (!issue) return;
              resolve.mutate(
                { id: issue.id, notes: resolveNotes.trim() || undefined },
                { onSuccess: () => setResolveNotes("") }
              );
            }}
          >
            Resolve
          </Button>
        )}
        <Button variant="default" onClick={onClose}>
          Close
        </Button>
      </Group>
    </Modal>
  );
}

function DetailRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <Grid>
      <Grid.Col span={3}>
        <Text fw={500}>{label}</Text>
      </Grid.Col>
      <Grid.Col span={9}>{children}</Grid.Col>
    </Grid>
  );
}

function describeError(err: unknown): string {
  if (isAxiosError(err)) {
    const status = err.response?.status;
    if (status === 403) return "You don't have permission for that action.";
    if (status === 404) return "Issue no longer exists.";
    if (status === 409) {
      const reason = (err.response?.data as { reason?: string } | undefined)?.reason;
      if (reason === "not_open") return "Someone else changed this issue's state. Refresh to see the latest.";
      if (reason === "already_resolved") return "This issue is already resolved.";
      return "State conflict — refresh and try again.";
    }
    return err.message;
  }
  return err instanceof Error ? err.message : "Unknown error";
}

function prettyJson(raw: string | null | undefined): string {
  if (!raw) return "{}";
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}
