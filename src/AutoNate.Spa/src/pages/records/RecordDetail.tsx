import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { Alert, Badge, Box, Button, Group, Paper, Stack, Tabs, Text, Title } from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import {
  useArchiveRecord,
  useRecordByKey,
  useRestoreRecord,
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
  const archive = useArchiveRecord();
  const restore = useRestoreRecord();
  const { data: isWatching = false } = useWatchStatus(record?.id ?? null);
  const watch = useWatchRecord(record?.id ?? "");
  const unwatch = useUnwatchRecord(record?.id ?? "");

  const [tab, setTab] = useState<Tab>("details");
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(null);

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
            <Button
              variant={isWatching ? "filled" : "outline"}
              leftSection={<i className={`fa ${isWatching ? "fa-eye-slash" : "fa-eye"}`} />}
              onClick={toggleWatched}
              loading={watch.isPending || unwatch.isPending}
            >
              {isWatching ? "Unwatch" : "Watch"}
            </Button>
            <Button
              variant="outline"
              color={record.isArchived ? "green" : "yellow"}
              leftSection={
                <i className={`fa ${record.isArchived ? "fa-box-open" : "fa-box-archive"}`} />
              }
              onClick={toggleArchived}
              loading={archive.isPending || restore.isPending}
            >
              {record.isArchived ? "Restore" : "Archive"}
            </Button>
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
