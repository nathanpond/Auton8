import { Box, Button, Group, Modal, Stack, Text, Title } from "@mantine/core";
import { useCommentRevisions } from "@/hooks/useRecordComments";
import { RecordCommentModel } from "@/types/records";
import UserBadge from "./UserBadge";

type Props = {
  recordId: string;
  comment: RecordCommentModel;
  onClose: () => void;
};

export default function CommentRevisionsDialog({ recordId, comment, onClose }: Props) {
  const { data: revisions = [], isLoading } = useCommentRevisions(recordId, comment.id);

  return (
    <Modal opened onClose={onClose} title="Comment edit history" size="lg">
      <Stack gap="md">
        <Text size="xs" c="dimmed">
          Created {formatWhen(comment.createdAtUtc)}.
          {comment.isEdited && <> Last edited {formatWhen(comment.bodyUpdatedAtUtc)}.</>}
        </Text>

        <div>
          <Title order={6} mb="xs">
            Current
          </Title>
          <Box
            component="pre"
            p="md"
            style={{
              background: "var(--mantine-color-default-hover)",
              borderRadius: 4,
              whiteSpace: "pre-wrap",
              margin: 0
            }}
          >
            {comment.body}
          </Box>
        </div>

        <Title order={6}>Previous versions</Title>
        {isLoading && (
          <Text c="dimmed" size="sm">
            Loading...
          </Text>
        )}
        {!isLoading && revisions.length === 0 && (
          <Text c="dimmed" size="sm">
            No prior edits.
          </Text>
        )}
        {revisions.map((r) => (
          <div key={r.id}>
            <Text size="xs" c="dimmed" mb={4}>
              Replaced {formatWhen(r.replacedAtUtc)}{" "}
              <UserBadge userId={r.replacedBy} withByPrefix />
            </Text>
            <Box
              component="pre"
              p="md"
              style={{
                background: "var(--mantine-color-default-hover)",
                borderRadius: 4,
                whiteSpace: "pre-wrap",
                margin: 0
              }}
            >
              {r.body}
            </Box>
          </div>
        ))}

        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            Close
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function formatWhen(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}
