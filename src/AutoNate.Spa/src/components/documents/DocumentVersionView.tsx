import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { Group, Loader, Stack, Text } from "@mantine/core";
import { DocxEditor, createEmptyDocument } from "@eigenpal/docx-editor-react";
import { Node as PmNode } from "prosemirror-model";
import { schema, fromProseDoc } from "@eigenpal/docx-editor-core/prosemirror";
import { fetchDocumentVersion } from "@/api/documents";

// Phase 9 polish — static read-only render of a single historical version.
// Versions are stored as ProseMirror JSON (the same body_jsonb shape the
// live doc mirrors). To render one without touching the live Yjs doc, we
// rebuild the PM node from JSON against docx-editor's own schema, convert
// it to docx-editor's Document model via the library's `fromProseDoc`, and
// mount a standalone read-only editor (`externalContent={false}`, no Yjs /
// Hocuspocus / plugins). Used by DocumentPreviewPage when `?version=N` is
// present.

type Props = {
  documentId: string;
  versionNumber: number;
};

export default function DocumentVersionView({ documentId, versionNumber }: Props) {
  const { data, isLoading, error } = useQuery({
    queryKey: ["documents", "document-version", documentId, versionNumber] as const,
    queryFn: ({ signal }) => fetchDocumentVersion(documentId, versionNumber, signal)
  });

  // Convert the stored PM JSON into docx-editor's Document model. The empty
  // base supplies default styles/sections; visual formatting rides on the
  // runs' marks (docx-editor stamps resolved style formatting onto runs),
  // so the render is faithful without the original style table.
  const doc = useMemo(() => {
    if (!data?.bodyJsonb) return createEmptyDocument();
    let json: unknown;
    try {
      json = JSON.parse(data.bodyJsonb);
    } catch (err) {
      console.error("[version-view] version body is not valid JSON", err);
      return null;
    }
    // Empty / placeholder snapshots (e.g. the auto-created "Initial
    // version" of a brand-new doc store `{}`) aren't a PM doc — render a
    // blank page rather than erroring; that's what the version actually was.
    if (!json || typeof json !== "object" || (json as { type?: unknown }).type !== "doc") {
      return createEmptyDocument();
    }
    try {
      const node = PmNode.fromJSON(schema, json);
      return fromProseDoc(node, createEmptyDocument());
    } catch (err) {
      console.error("[version-view] failed to convert version body", err);
      return null;
    }
  }, [data?.bodyJsonb]);

  if (isLoading) {
    return (
      <Group justify="center" mt="xl">
        <Loader />
      </Group>
    );
  }
  if (error || !data) {
    return (
      <Stack p="md" gap={4}>
        <Text fw={600}>Version not available</Text>
        <Text c="dimmed" size="sm">
          This version couldn&apos;t be loaded — it may have been deleted.
        </Text>
      </Stack>
    );
  }
  if (!doc) {
    return (
      <Stack p="md" gap={4}>
        <Text fw={600}>Couldn&apos;t render this version</Text>
        <Text c="dimmed" size="sm">
          The stored content could not be parsed.
        </Text>
      </Stack>
    );
  }

  return (
    <DocxEditor
      document={doc}
      externalContent={false}
      readOnly
      mode="viewing"
      showToolbar={false}
      showRuler={false}
      showZoomControl={false}
      style={{ height: "100%", background: "var(--mantine-color-gray-0)" }}
    />
  );
}
