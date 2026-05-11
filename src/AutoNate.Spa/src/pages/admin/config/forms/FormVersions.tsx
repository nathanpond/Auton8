import { Badge, Button, Group, Modal, Table, Text } from "@mantine/core";
import { FormVersion } from "@/api/forms";
import { useFormVersions, useRestoreFormVersion } from "@/hooks/useForms";

type Props = {
  formId: string;
  onClose: () => void;
  onRestored?: () => void;
};

export default function FormVersions({ formId, onClose, onRestored }: Props) {
  const { data: versions = [], isLoading } = useFormVersions(formId);
  const restore = useRestoreFormVersion();

  const onRestore = async (versionNumber: number) => {
    if (!window.confirm(`Restore v${versionNumber}? A new draft version will be appended.`)) {
      return;
    }
    await restore.mutateAsync({ id: formId, versionNumber });
    onRestored?.();
  };

  return (
    <Modal opened onClose={onClose} title="Version history" size="lg">
      {isLoading && (
        <Text c="dimmed" size="sm">
          Loading…
        </Text>
      )}
      {!isLoading && versions.length === 0 && (
        <Text c="dimmed" size="sm">
          No versions yet.
        </Text>
      )}
      {!isLoading && versions.length > 0 && (
        <Table withTableBorder striped>
          <Table.Thead>
            <Table.Tr>
              <Table.Th style={{ width: "5rem" }}>v</Table.Th>
              <Table.Th style={{ width: "7rem" }}>Kind</Table.Th>
              <Table.Th>When</Table.Th>
              <Table.Th>Note</Table.Th>
              <Table.Th style={{ width: "8rem" }} />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {versions.map((v) => (
              <VersionRow
                key={v.id}
                version={v}
                isPending={restore.isPending}
                onRestore={() => onRestore(v.versionNumber)}
              />
            ))}
          </Table.Tbody>
        </Table>
      )}
      <Group justify="flex-end" mt="md">
        <Button variant="default" onClick={onClose}>
          Close
        </Button>
      </Group>
    </Modal>
  );
}

function VersionRow({
  version,
  isPending,
  onRestore
}: {
  version: FormVersion;
  isPending: boolean;
  onRestore: () => void;
}) {
  return (
    <Table.Tr>
      <Table.Td>
        <code>v{version.versionNumber}</code>
      </Table.Td>
      <Table.Td>
        <KindBadge kind={version.kind} />
      </Table.Td>
      <Table.Td>{formatWhen(version.createdAtUtc)}</Table.Td>
      <Table.Td>{version.note ?? ""}</Table.Td>
      <Table.Td>
        <Button size="xs" variant="default" onClick={onRestore} loading={isPending}>
          Restore
        </Button>
      </Table.Td>
    </Table.Tr>
  );
}

function KindBadge({ kind }: { kind: FormVersion["kind"] }) {
  if (kind === "publish") {
    return (
      <Badge color="green" variant="filled">
        Publish
      </Badge>
    );
  }
  if (kind === "restore") {
    return (
      <Badge color="cyan" variant="filled">
        Restore
      </Badge>
    );
  }
  return (
    <Badge color="gray" variant="filled">
      Save
    </Badge>
  );
}

function formatWhen(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}
