import { toast } from "@/components/notifications/toast";
import { useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import {
  ActionIcon,
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
  recordByKeyKey,
  recordHistoryKey,
  recordKey as recordIdKey,
  useDeleteRecord,
  useRecordByKey,
  useUnwatchRecord,
  useUpdateRecord,
  useWatchRecord,
  useWatchStatus
} from "@/hooks/useRecords";
import { useRecordTypeFields, useRecordTypes } from "@/hooks/useRecordTypes";
import { permissionKey, usePermissionChecks } from "@/hooks/usePermissionChecks";
import { useInvalidateOnChannels } from "@/hooks/useInvalidateOnChannels";
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
  const [deleteOpen, setDeleteOpen] = useState(false);

  // The delete affordance used to render unconditionally (archived-85). The backend
  // gate held — the API refused the call — but offering a button that always
  // fails is its own defect: the user cannot tell "not allowed" from "broken",
  // and it reads as an invitation to try. Instance-level check because the
  // backend gates this on the specific record, not the kind.
  const deleteCheck = useMemo(
    () => (record ? [{ kind: "record", action: "delete", id: record.id }] : []),
    [record]
  );
  const { data: deletePermissions } = usePermissionChecks(deleteCheck);
  const canDelete =
    deleteCheck.length > 0 &&
    (deletePermissions?.get(permissionKey(deleteCheck[0])) ?? false);

  // Live updates: when this specific record changes upstream, invalidate the
  // detail/by-key/history queries so the view refreshes without a manual
  // reload. Subscribed only once the record id resolves; the scoped channel
  // means we only see events for this one record.
  const recordId = record?.id ?? null;
  const channels = useMemo(
    () => (recordId ? [`record:${recordId}`] : []),
    [recordId],
  );
  const queryKeys = useMemo(
    () => (recordId
      ? [recordIdKey(recordId), recordByKeyKey(key), recordHistoryKey(recordId, undefined)]
      : []),
    [recordId, key],
  );
  useInvalidateOnChannels(channels, queryKeys, { enabled: Boolean(recordId) });

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
        toast.success("Unwatched.");
      } else {
        await watch.mutateAsync();
        toast.success("Watching.");
      }
    } catch (err) {
      toast.error(describeError(err));
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
      toast.error(describeError(err));
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
            {canDelete && (
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
            )}
          </Group>
        }
      />

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
                  toast.success("Saved.");
                } catch (err) {
                  toast.error(describeError(err));
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
