import { Suspense, lazy } from "react";
import { Link, useParams } from "react-router-dom";
import {
  Anchor,
  Badge,
  Box,
  Button,
  Group,
  Loader,
  Stack,
  Text,
  Title
} from "@mantine/core";
import { useDocument } from "@/hooks/useDocuments";
import { PageContextRegistryProvider } from "@/agent/pageContext/PageContextRegistry";

// Phase 11 — read-only preview surface. Mounts the same docx-editor against
// the same live Yjs doc, but in `previewMode`: chrome-free, read-only, with
// bindings already resolved into the document tree (Phase 10), so this is
// literally "what the populated document looks like" with no transformation
// step.
//
// docx-editor's own title bar is hidden along with the toolbar in preview,
// so this page renders its own slim header (doc title + back-to-editor).
// Shares the lazy DocxDocumentEditor chunk with DocumentEditorPage (React
// dedupes the dynamic import by module), and mounts OUTSIDE the AppShell
// (wired in router.tsx) for the same full-bleed treatment as the editor.

const DocxDocumentEditor = lazy(
  () => import("@/components/documents/DocxDocumentEditor")
);

export default function DocumentPreviewPage() {
  const { documentId } = useParams<{ documentId: string }>();
  const { data: doc, isLoading, error } = useDocument(documentId ?? null);

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
    // Local page-context registry: DocxDocumentEditor registers chat page
    // context via a hook regardless of whether the AI panel is shown, and
    // this route lives outside the AppShell that normally provides it.
    <PageContextRegistryProvider>
      <Box style={{ display: "flex", flexDirection: "column", height: "100vh" }}>
        {/* Slim preview header — docx-editor's own title bar is suppressed
            in preview mode, so this carries the title + nav back to edit. */}
        <Group
          justify="space-between"
          wrap="nowrap"
          px="md"
          py="xs"
          style={{
            borderBottom: "1px solid var(--mantine-color-gray-3)",
            background: "var(--mantine-color-body)"
          }}
        >
          <Group gap="sm" wrap="nowrap" style={{ minWidth: 0 }}>
            <Badge variant="light" color="blue">
              Preview
            </Badge>
            <Text fw={600} truncate>
              {doc.title}
            </Text>
          </Group>
          <Group gap="sm" wrap="nowrap">
            <Anchor component={Link} to={`/documents/p/${doc.projectId}`} size="sm">
              <i className="fa fa-arrow-left" aria-hidden style={{ marginRight: 6 }} />
              Back to project
            </Anchor>
            <Button
              component={Link}
              to={`/documents/edit/${doc.id}`}
              size="xs"
              variant="default"
              leftSection={<i className="fa fa-pen-to-square" aria-hidden />}
            >
              Edit
            </Button>
          </Group>
        </Group>

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
              previewMode
            />
          </Box>
        </Suspense>
      </Box>
    </PageContextRegistryProvider>
  );
}
