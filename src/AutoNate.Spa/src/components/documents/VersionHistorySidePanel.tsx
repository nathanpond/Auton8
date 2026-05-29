import {
  ActionIcon,
  Badge,
  Box,
  Divider,
  Group,
  Loader,
  ScrollArea,
  Stack,
  Text,
  Tooltip
} from "@mantine/core";
import type { DocumentVersionSummaryDto } from "@/api/documents";
import { useDocumentVersions } from "@/hooks/useDocuments";

// Phase 9 polish — version-history side panel. Lists every snapshot of the
// open document (newest first) from the REST versions API. Clicking "View"
// opens that version read-only in the Phase-11 preview surface (a new tab,
// so the editor stays put). v1 is list + view only — restore is deferred
// because pushing a restored body into the live Yjs doc is non-trivial (the
// REST body mirror only seeds Yjs on a cold load).

type Props = {
  documentId: string;
  // Hide button — mirrors BindingsSidePanel so the editor can wire it to
  // the same toolbar-toggle / activePanel machinery.
  onClose?: () => void;
};

const KIND_LABEL: Record<DocumentVersionSummaryDto["kind"], string> = {
  manual: "Manual",
  autosave: "Autosave",
  restore: "Restore"
};

const KIND_COLOR: Record<DocumentVersionSummaryDto["kind"], string> = {
  manual: "blue",
  autosave: "gray",
  restore: "grape"
};

export default function VersionHistorySidePanel({ documentId, onClose }: Props) {
  const { data, isLoading } = useDocumentVersions(documentId);
  const versions = data?.items ?? [];

  return (
    <Box
      style={{
        width: 320,
        height: "100%",
        borderLeft: "1px solid var(--mantine-color-gray-3)",
        background: "var(--mantine-color-body)",
        display: "flex",
        flexDirection: "column",
        minHeight: 0
      }}
    >
      <Group justify="space-between" px="sm" py="xs">
        <Text fw={600} size="sm">
          Version history
        </Text>
        {onClose ? (
          <Tooltip label="Close version history" withArrow openDelay={350}>
            <ActionIcon
              size="sm"
              variant="subtle"
              onClick={onClose}
              aria-label="Close version history"
            >
              <i className="fa fa-xmark" aria-hidden />
            </ActionIcon>
          </Tooltip>
        ) : null}
      </Group>
      <Divider />
      <ScrollArea style={{ flex: 1 }}>
        {isLoading ? (
          <Group justify="center" py="md">
            <Loader size="xs" />
          </Group>
        ) : versions.length === 0 ? (
          <Stack p="sm" gap={4}>
            <Text c="dimmed" size="xs">
              No versions yet.
            </Text>
          </Stack>
        ) : (
          <Stack gap={4} p="xs">
            {versions.map((v) => (
              <VersionRow key={v.id} documentId={documentId} version={v} />
            ))}
          </Stack>
        )}
      </ScrollArea>
    </Box>
  );
}

function VersionRow({
  documentId,
  version
}: {
  documentId: string;
  version: DocumentVersionSummaryDto;
}) {
  const when = new Date(version.createdAtUtc).toLocaleString();
  return (
    <Box
      p="xs"
      style={{
        border: "1px solid var(--mantine-color-gray-3)",
        borderRadius: 4
      }}
    >
      <Group justify="space-between" gap={4} wrap="nowrap">
        <Stack gap={2} style={{ minWidth: 0, flex: 1 }}>
          <Group gap={6} wrap="nowrap">
            <Badge size="xs" variant="light" color={KIND_COLOR[version.kind]}>
              v{version.versionNumber} · {KIND_LABEL[version.kind]}
            </Badge>
          </Group>
          <Text size="xs" truncate>
            {version.createdByName ?? "Unknown"} · {when}
          </Text>
          {version.note ? (
            <Text size="xs" c="dimmed" truncate>
              {version.note}
            </Text>
          ) : null}
        </Stack>
        <Tooltip label="View this version (read-only)" withArrow openDelay={350}>
          <ActionIcon
            size="xs"
            variant="subtle"
            aria-label={`View version ${version.versionNumber}`}
            onClick={() =>
              window.open(
                `/documents/preview/${documentId}?version=${version.versionNumber}`,
                "_blank",
                "noopener"
              )
            }
          >
            <i className="fa fa-eye" aria-hidden />
          </ActionIcon>
        </Tooltip>
      </Group>
    </Box>
  );
}
