import { Suspense, lazy, useCallback, useEffect, useState } from "react";
import {
  Link,
  useNavigate,
  useParams,
  useSearchParams
} from "react-router-dom";
import {
  Anchor,
  Box,
  Button,
  Group,
  Loader,
  Stack,
  Text,
  Title
} from "@mantine/core";
import { notifications } from "@mantine/notifications";
import { useDocument, useUpdateDocument } from "@/hooks/useDocuments";
import {
  discardDocumentImportBuffer,
  fetchDocumentImportBuffer
} from "@/api/documents";
import { PageContextRegistryProvider } from "@/agent/pageContext/PageContextRegistry";

// Phase 3 (post-switch): we host @eigenpal/docx-editor-react in this
// distraction-free route. The editor brings its own title bar, toolbar,
// rulers, and zoom — we pass the doc title + a "Back to project" link
// into its title bar right slot so we don't end up with two chrome rows
// stacked. Renames flow through the REST documents endpoint via
// useUpdateDocument; body content flows through Yjs + Hocuspocus, which
// the wrapper component owns.
//
// Phase 7 import flow: when `?import=1` is present, the page fetches the
// stashed OOXML buffer up front and feeds it to the editor in import
// mode. The editor parses it client-side, calls `onImportFinalized`
// with the resulting ProseMirror JSON, and this page commits that JSON
// to `body_jsonb` via the documents PATCH endpoint + discards the
// stash + navigates to the same route without the query flag so the
// next mount uses the normal Yjs path (the sidecar cold-load seed reads
// `body_jsonb` and populates the Y.Doc).

const DocxDocumentEditor = lazy(
  () => import("@/components/documents/DocxDocumentEditor")
);

export default function DocumentEditorPage() {
  const { documentId } = useParams<{ documentId: string }>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const { data: doc, isLoading, error } = useDocument(documentId ?? null);
  const updateDocument = useUpdateDocument();

  const wantsImport = searchParams.get("import") === "1";

  // Import buffer is fetched once per page load. We keep it in component
  // state so it can be passed down + retried on transient network
  // failures. `null` means "not yet attempted" (initial state); an
  // ArrayBuffer means "loaded, awaiting editor mount"; a sentinel
  // `false` means "fetch failed — fall back to blank editor" so the
  // user still gets in.
  const [importBuffer, setImportBuffer] = useState<ArrayBuffer | null | false>(
    null
  );
  useEffect(() => {
    if (!wantsImport || !documentId) {
      setImportBuffer(null);
      return;
    }
    let cancelled = false;
    fetchDocumentImportBuffer(documentId)
      .then((buf) => {
        if (!cancelled) setImportBuffer(buf);
      })
      .catch((err) => {
        if (cancelled) return;
        console.error("[import] failed to fetch buffer", err);
        notifications.show({
          message:
            "Import buffer not available — opening blank. The original upload may have already been processed.",
          color: "yellow"
        });
        setImportBuffer(false);
      });
    return () => {
      cancelled = true;
    };
  }, [wantsImport, documentId]);

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

  // Called by DocxDocumentEditor once the OOXML buffer has been parsed
  // into ProseMirror state. We PATCH the JSON into body_jsonb, discard
  // the stash, then strip `?import=1` from the URL so the next mount
  // uses the standard Yjs path. The sidecar's cold-load seed picks the
  // body_jsonb mirror up and writes it into the Y.Doc on first connect.
  const onImportFinalized = useCallback(
    async (bodyJsonb: string) => {
      if (!doc) return;
      try {
        await updateDocument.mutateAsync({
          id: doc.id,
          previousProjectId: doc.projectId,
          previousFolderId: doc.folderId,
          patch: { bodyJsonb }
        });
        // Best-effort cleanup. If the discard fails (transient I/O on the
        // server) the stash will linger but the document itself is now
        // fully self-contained via body_jsonb, so the user isn't blocked.
        try {
          await discardDocumentImportBuffer(doc.id);
        } catch (err) {
          console.warn("[import] failed to discard stash", err);
        }
        notifications.show({
          message: "Import complete.",
          color: "green"
        });
        // Replace so the user's back button doesn't re-trigger import.
        navigate(`/documents/edit/${doc.id}`, { replace: true });
      } catch (err) {
        console.error("[import] failed to commit body_jsonb", err);
        notifications.show({
          message: "Failed to finalize import. Try refreshing the page.",
          color: "red"
        });
      }
    },
    [doc, navigate, updateDocument]
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
          The document either doesn&apos;t exist or you don&apos;t have permission to view it.
        </Text>
        <Anchor component={Link} to="/documents">
          Back to Documents
        </Anchor>
      </Stack>
    );
  }

  // Block the editor mount until we resolve the import buffer state.
  // Without this guard, the editor would mount briefly in live (Yjs)
  // mode before being remounted in import mode once the fetch resolves
  // — which would write an empty Yjs state to the server and discard
  // the import in the process.
  if (wantsImport && importBuffer === null) {
    return (
      <Group justify="center" mt="xl">
        <Stack align="center" gap={4}>
          <Loader />
          <Text size="sm" c="dimmed">
            Fetching import buffer…
          </Text>
        </Stack>
      </Group>
    );
  }

  // `importBuffer === false` (fetch failed) collapses back to a normal
  // live-mode mount — the editor opens blank and the user can re-upload
  // if they want the original content back.
  const importBufferForEditor =
    wantsImport && importBuffer instanceof ArrayBuffer ? importBuffer : null;

  return (
    // PageContextRegistryProvider lives in AppShell, but this editor route
    // mounts OUTSIDE the shell for full-bleed. Wrap a local provider here
    // so useRegisterPageContext inside DocxDocumentEditor (the chat
    // page-context plumbing) has something to talk to. Scoped to this
    // route — the AppShell's own registry continues to serve other pages.
    <PageContextRegistryProvider>
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
              <Group gap="sm" wrap="nowrap">
                <Anchor
                  component={Link}
                  to={`/documents/p/${doc.projectId}`}
                  size="sm"
                >
                  <i className="fa fa-arrow-left" aria-hidden style={{ marginRight: 6 }} />
                  Back to project
                </Anchor>
                <Button
                  component={Link}
                  to={`/documents/preview/${doc.id}`}
                  size="xs"
                  variant="default"
                  leftSection={<i className="fa fa-eye" aria-hidden />}
                >
                  Preview
                </Button>
              </Group>
            }
            importBuffer={importBufferForEditor}
            onImportFinalized={onImportFinalized}
          />
        </Box>
      </Suspense>
    </Box>
    </PageContextRegistryProvider>
  );
}
