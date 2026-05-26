import { Suspense, lazy, useCallback } from "react";
import { Link, useParams } from "react-router-dom";
import {
  Anchor,
  Box,
  Group,
  Loader,
  Stack,
  Text,
  Title
} from "@mantine/core";
import { notifications } from "@mantine/notifications";
import { useDocument, useUpdateDocument } from "@/hooks/useDocuments";

// Phase 3 (post-switch): we host @eigenpal/docx-editor-react in this
// distraction-free route. The editor brings its own title bar, toolbar,
// rulers, and zoom — we pass the doc title + a "Back to project" link
// into its title bar right slot so we don't end up with two chrome rows
// stacked. Renames flow through the REST documents endpoint via
// useUpdateDocument; body content flows through Yjs + Hocuspocus, which
// the wrapper component owns.

const DocxDocumentEditor = lazy(
  () => import("@/components/documents/DocxDocumentEditor")
);

export default function DocumentEditorPage() {
  const { documentId } = useParams<{ documentId: string }>();
  const { data: doc, isLoading, error } = useDocument(documentId ?? null);
  const updateDocument = useUpdateDocument();

  const onRename = useCallback(
    async (newTitle: string) => {
      if (!doc) return;
      const trimmed = newTitle.trim();
      if (!trimmed || trimmed === doc.title) return;
      try {
        await updateDocument.mutateAsync({
          id: doc.id,
          previousProjectId: doc.projectId,
          previousFolderId: doc.folderId,
          patch: { title: trimmed }
        });
        notifications.show({
          message: `Renamed to "${trimmed}".`,
          color: "green"
        });
      } catch {
        notifications.show({
          message: "Failed to rename document.",
          color: "red"
        });
      }
    },
    [doc, updateDocument]
  );

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
      <Suspense
        fallback={
          <Group justify="center" mt="xl">
            <Loader />
          </Group>
        }
      >
        <Box style={{ flex: 1, minHeight: 0 }}>
          <DocxDocumentEditor
            documentId={doc.id}
            documentTitle={doc.title}
            onRenameDocument={onRename}
            titleBarRight={
              <Anchor
                component={Link}
                to={`/documents/p/${doc.projectId}`}
                size="sm"
              >
                <i className="fa fa-arrow-left" aria-hidden style={{ marginRight: 6 }} />
                Back to project
              </Anchor>
            }
          />
        </Box>
      </Suspense>
    </Box>
  );
}
