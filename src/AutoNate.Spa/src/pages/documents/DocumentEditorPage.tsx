import { useMemo } from "react";
import { Link, useParams } from "react-router-dom";
import {
  Anchor,
  Badge,
  Box,
  Code,
  Group,
  Loader,
  Stack,
  Text,
  Title
} from "@mantine/core";
import { useDocument } from "@/hooks/useDocuments";

// Phase 2 of the Documents feature: a minimal "editor" route. Renders the
// document title + a read-only pretty-printed `bodyJsonb` block so the
// data round-trip is visible end-to-end before the docx-editor + Hocuspocus
// integration lands in Phase 3. Mounts OUTSIDE AppShell (see router.tsx) so
// the future docx-editor will get full bleed; for now we wrap the content
// in a thin chrome with a "Back to project" link.
export default function DocumentEditorPage() {
  const { documentId } = useParams<{ documentId: string }>();
  const { data: doc, isLoading, error } = useDocument(documentId ?? null);

  const prettyBody = useMemo(() => {
    if (!doc) return "";
    try {
      return JSON.stringify(JSON.parse(doc.bodyJsonb), null, 2);
    } catch {
      return doc.bodyJsonb;
    }
  }, [doc]);

  if (isLoading) {
    return (
      <Group justify="center" mt="xl">
        <Loader />
      </Group>
    );
  }
  if (error || !doc) {
    return (
      <Stack p="md">
        <Title order={2}>Document not found</Title>
        <Text c="dimmed">
          The document either doesn't exist or you don't have permission to view it.
        </Text>
        <Anchor component={Link} to="/documents">
          Back to Documents
        </Anchor>
      </Stack>
    );
  }

  return (
    <Box style={{ display: "flex", flexDirection: "column", height: "100vh" }}>
      {/* Editor chrome — kept very thin in Phase 2; Phase 3 replaces this
          with the docx-editor's own toolbar + the right-side AI panel. */}
      <Group
        justify="space-between"
        align="center"
        px="md"
        py="xs"
        style={{
          borderBottom: "1px solid var(--mantine-color-gray-3)",
          background: "var(--mantine-color-body)"
        }}
      >
        <Group gap="md" style={{ minWidth: 0 }}>
          <Anchor component={Link} to={`/documents/p/${doc.projectId}`} size="sm">
            <i className="fa fa-arrow-left" aria-hidden style={{ marginRight: 6 }} />
            Back to project
          </Anchor>
          <Title order={4} style={{ wordBreak: "break-word" }}>
            {doc.title}
          </Title>
          <Badge color="gray" variant="light">
            {doc.kind}
          </Badge>
          <Badge color="blue" variant="light">
            v{doc.currentVersionNumber - 1}
          </Badge>
        </Group>
        <Text size="xs" c="dimmed">
          Editor preview — full editor lands in Phase 3.
        </Text>
      </Group>

      <Box style={{ flex: 1, overflow: "auto", padding: 24 }}>
        <Stack gap="md" style={{ maxWidth: 880, margin: "0 auto" }}>
          {doc.description ? (
            <Text c="dimmed">{doc.description}</Text>
          ) : null}
          <Title order={5}>Body (read-only JSON)</Title>
          <Code block style={{ whiteSpace: "pre", overflowX: "auto" }}>
            {prettyBody || "(empty)"}
          </Code>
        </Stack>
      </Box>
    </Box>
  );
}
