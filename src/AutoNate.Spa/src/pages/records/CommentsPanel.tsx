import { toast } from "@/components/notifications/toast";
import { useState } from "react";
import {
  Badge,
  Box,
  Button,
  Group,
  Stack,
  Switch,
  Text,
  Textarea
} from "@mantine/core";
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

  const ordered = [...comments].sort((a, b) => a.createdAtUtc.localeCompare(b.createdAtUtc));

  const [draft, setDraft] = useState("");
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingBody, setEditingBody] = useState("");
  const [revisionsTarget, setRevisionsTarget] = useState<RecordCommentModel | null>(null);

  const submitNew = async (e: React.FormEvent) => {
    e.preventDefault();
    if (draft.trim().length === 0) return;
    try {
      await create.mutateAsync(draft);
      setDraft("");
    } catch (err) {
      toast.error(describeError(err));
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
      toast.error(describeError(err));
    }
  };

  const onDelete = async (id: string) => {
    if (!window.confirm("Delete this comment? It will be hidden but its history is preserved.")) return;
    try {
      await del.mutateAsync(id);
      toast.success("Deleted.");
    } catch (err) {
      toast.error(describeError(err));
    }
  };

  return (
    <Stack gap="md">
      <Group justify="flex-end">
        <Switch
          checked={includeDeleted}
          onChange={(e) => setIncludeDeleted(e.currentTarget.checked)}
          label="Show deleted"
          size="sm"
        />
      </Group>

      {isLoading && (
        <Text size="sm" c="dimmed">
          Loading comments...
        </Text>
      )}

      {!isLoading && ordered.length === 0 && (
        <Text size="sm" c="dimmed">
          No comments yet.
        </Text>
      )}

      <Stack gap="md">
        {ordered.map((c) => {
          const isEditing = editingId === c.id;
          return (
            <Box
              key={c.id}
              pb="md"
              style={{
                borderBottom: "1px solid var(--mantine-color-default-border)",
                opacity: c.isDeleted ? 0.6 : 1
              }}
            >
              <Group justify="space-between" align="flex-start" wrap="nowrap" mb={6}>
                <Text size="xs" c="dimmed" component="div">
                  <i className="fa fa-user" style={{ marginRight: 6 }} />
                  <UserBadge userId={c.authorId} />
                  <span style={{ margin: "0 8px" }}>·</span>
                  <span>{formatWhen(c.createdAtUtc)}</span>
                  {c.isEdited && !c.isDeleted && (
                    <Button
                      variant="subtle"
                      size="compact-xs"
                      ml={4}
                      onClick={() => setRevisionsTarget(c)}
                    >
                      (edited — view history)
                    </Button>
                  )}
                  {c.isDeleted && (
                    <Badge color="gray" variant="filled" ml={8}>
                      Deleted
                    </Badge>
                  )}
                </Text>
                {!c.isDeleted && !isEditing && (
                  <Group gap="xs">
                    <Button variant="subtle" size="compact-xs" onClick={() => startEdit(c)}>
                      Edit
                    </Button>
                    <Button
                      variant="subtle"
                      color="red"
                      size="compact-xs"
                      onClick={() => onDelete(c.id)}
                      loading={del.isPending}
                    >
                      Delete
                    </Button>
                  </Group>
                )}
              </Group>
              {isEditing ? (
                <Stack gap="xs">
                  <Textarea
                    minRows={3}
                    autosize
                    value={editingBody}
                    onChange={(e) => setEditingBody(e.currentTarget.value)}
                  />
                  <Group justify="flex-end" gap="xs">
                    <Button
                      variant="default"
                      size="xs"
                      onClick={() => {
                        setEditingId(null);
                        setEditingBody("");
                      }}
                    >
                      Cancel
                    </Button>
                    <Button
                      size="xs"
                      onClick={saveEdit}
                      loading={edit.isPending}
                      disabled={editingBody.trim().length === 0}
                    >
                      Save
                    </Button>
                  </Group>
                </Stack>
              ) : (
                <pre
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
            </Box>
          );
        })}
      </Stack>

      <Box component="form" onSubmit={submitNew}>
        <Stack gap="xs">
          <Textarea
            minRows={3}
            autosize
            placeholder="Add a comment..."
            value={draft}
            onChange={(e) => setDraft(e.currentTarget.value)}
          />
          <Group justify="flex-end">
            <Button
              type="submit"
              size="sm"
              loading={create.isPending}
              disabled={draft.trim().length === 0}
              leftSection={<i className="fa fa-comment" />}
            >
              Post comment
            </Button>
          </Group>
        </Stack>
      </Box>

      {revisionsTarget && (
        <CommentRevisionsDialog
          recordId={recordId}
          comment={revisionsTarget}
          onClose={() => setRevisionsTarget(null)}
        />
      )}
    </Stack>
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
