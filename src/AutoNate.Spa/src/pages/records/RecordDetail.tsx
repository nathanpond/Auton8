import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import {
  ActionIcon,
  Alert,
  Badge,
  Box,
  Button,
  Code,
  Group,
  Modal,
  Paper,
  Stack,
  Tabs,
  Text,
  Title,
  Tooltip
} from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import {
  useDeleteRecord,
  useRecordByKey,
  useUnwatchRecord,
  useUpdateRecord,
  useWatchRecord,
  useWatchStatus
} from "@/hooks/useRecords";
import { useRecordTypeFields, useRecordTypes } from "@/hooks/useRecordTypes";
import "./fields/renderers";
import CommentsPanel from "./CommentsPanel";
import EdgesPanel from "./EdgesPanel";
import RecordForm from "./RecordForm";
import RecordHistoryPanel from "./RecordHistoryPanel";

type Tab = "details" | "edges" | "history";

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
  const deleteRecord = useDeleteRecord();
  const { data: isWatching = false } = useWatchStatus(record?.id ?? null);
  const watch = useWatchRecord(record?.id ?? "");
  const unwatch = useUnwatchRecord(record?.id ?? "");

  const [tab, setTab] = useState<Tab>("details");
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(null);
  const [deleteOpen, setDeleteOpen] = useState(false);

  if (isLoading || !type) {
    return (
      <Paper withBorder radius="md" p="lg" ta="center">
        <Text c="dimmed">Loading...</Text>
      </Paper>
    );
  }

  if (!record) {
    return (
      <Box py="md">
        <PageHeader
          title="Record not found"
          description={
            <>
              <code>{key}</code> wasn&apos;t found. <Link to={`/records/${code}`}>Back to list</Link>.
            </>
          }
        />
      </Box>
    );
  }

  const toggleWatched = async () => {
    try {
      if (isWatching) {
        await unwatch.mutateAsync();
        setFlash({ kind: "success", message: "Unwatched." });
      } else {
        await watch.mutateAsync();
        setFlash({ kind: "success", message: "Watching." });
      }
    } catch (err) {
      setFlash({ kind: "error", message: describeError(err) });
    }
  };

  const confirmDelete = async () => {
    try {
      await deleteRecord.mutateAsync(record.id);
      // Hop back to the list before the cached record query has a chance to
      // refetch a 404 and crash the detail view.
      navigate(`/records/${code}`);
    } catch (err) {
      setDeleteOpen(false);
      setFlash({ kind: "error", message: describeError(err) });
    }
  };

  return (
    <Box py="md">
      <PageHeader
        title={
          <Group gap="xs" wrap="wrap" align="center">
            <code style={{ marginRight: 4 }}>{record.key}</code>
            <Title order={1} m={0} style={{ display: "inline" }}>
              {record.name}
            </Title>
            {record.isArchived && (
              <Badge color="gray" variant="filled">
                Archived
              </Badge>
            )}
          </Group>
        }
        description={<Link to={`/records/${code}`}>&larr; Back to list</Link>}
        actions={
          <Group gap="xs">
            <Tooltip label={isWatching ? "Unwatch" : "Watch"} withArrow>
              <ActionIcon
                size="lg"
                variant="subtle"
                color={isWatching ? "blue" : "gray"}
                aria-label={isWatching ? "Unwatch" : "Watch"}
                onClick={toggleWatched}
                loading={watch.isPending || unwatch.isPending}
              >
                <i className={`fa ${isWatching ? "fa-eye-slash" : "fa-eye"}`} />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="Delete" withArrow>
              <ActionIcon
                size="lg"
                variant="subtle"
                color="red"
                aria-label="Delete"
                onClick={() => setDeleteOpen(true)}
              >
                <i className="fa fa-trash" />
              </ActionIcon>
            </Tooltip>
          </Group>
        }
      />

      {flash && (
        <Alert
          color={flash.kind === "success" ? "green" : "red"}
          variant="light"
          role={flash.kind === "success" ? "status" : "alert"}
          mb="sm"
        >
          {flash.message}
        </Alert>
      )}

      <Tabs value={tab} onChange={(value) => value && setTab(value as Tab)} mb="md">
        <Tabs.List>
          <Tabs.Tab value="details">Details</Tabs.Tab>
          <Tabs.Tab value="edges">Edges</Tabs.Tab>
          <Tabs.Tab value="history">History</Tabs.Tab>
        </Tabs.List>
      </Tabs>

      <Stack gap="md">
        <Paper withBorder radius="md" p="md">
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
          {tab === "history" && <RecordHistoryPanel recordId={record.id} fields={fields} />}
        </Paper>

        <Paper withBorder radius="md" p="md">
          <Title order={4} mb="sm">
            Comments
          </Title>
          <CommentsPanel recordId={record.id} />
        </Paper>
      </Stack>

      <Modal
        opened={deleteOpen}
        onClose={() => (deleteRecord.isPending ? undefined : setDeleteOpen(false))}
        title="Delete record"
        centered
      >
        <Stack gap="md">
          <Text>
            Permanently delete <Code>{record.key}</Code>{" "}
            <strong>{record.name}</strong>? This will also remove every comment, history
            entry, edge, and watch attached to this record.
          </Text>
          <Text c="red" fw={600}>
            This cannot be undone.
          </Text>
          <Group justify="flex-end" gap="xs">
            <Button
              variant="default"
              onClick={() => setDeleteOpen(false)}
              disabled={deleteRecord.isPending}
            >
              Cancel
            </Button>
            <Button
              color="red"
              leftSection={<i className="fa fa-trash" />}
              onClick={confirmDelete}
              loading={deleteRecord.isPending}
            >
              Delete
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Box>
  );
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { message?: string } } }).response;
    return response?.data?.message ?? error.message;
  }
  return String(error);
}
