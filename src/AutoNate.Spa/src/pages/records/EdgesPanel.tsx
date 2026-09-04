import { toast } from "@/components/notifications/toast";
import { useState } from "react";
import { Button, Group, Stack, Table, Text, Title } from "@mantine/core";
import { useDeleteEdge, useEdgeTypes, useRecordEdges } from "@/hooks/useRecordEdges";
import { useRecordTypes } from "@/hooks/useRecordTypes";
import { RecordModel } from "@/types/records";
import EdgeLinkDialog from "./EdgeLinkDialog";
import EdgeRow from "./EdgeRow";

type Props = {
  record: RecordModel;
};

export default function EdgesPanel({ record }: Props) {
  const { data: edges = [], isLoading } = useRecordEdges(record.id, "both");
  const { data: edgeTypes = [] } = useEdgeTypes(true);
  const { data: recordTypes = [] } = useRecordTypes(true);
  const recordType = recordTypes.find((t) => t.id === record.recordTypeId);
  const del = useDeleteEdge(record.id);

  const [dialogOpen, setDialogOpen] = useState(false);

  const onDelete = async (edgeId: string) => {
    if (!window.confirm("Remove this edge?")) return;
    try {
      await del.mutateAsync(edgeId);
      toast.success("Removed.");
    } catch (err) {
      toast.error(describeError(err));
    }
  };

  return (
    <Stack gap="md">
      <Group justify="space-between" align="center">
        <Title order={5} m={0}>
          Edges
        </Title>
        <Button
          size="sm"
          onClick={() => setDialogOpen(true)}
          disabled={!recordType}
          leftSection={<i className="fa fa-link" />}
        >
          New link
        </Button>
      </Group>

      {isLoading && (
        <Text size="sm" c="dimmed">
          Loading edges...
        </Text>
      )}

      {!isLoading && edges.length === 0 && (
        <Text size="sm" c="dimmed">
          No edges yet. Click &quot;New link&quot; to relate this record to another.
        </Text>
      )}

      {edges.length > 0 && (
        <Table withTableBorder striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Relation</Table.Th>
              <Table.Th>Other record</Table.Th>
              <Table.Th>Data</Table.Th>
              <Table.Th style={{ width: "4rem" }} />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {edges.map((edge) => {
              const edgeType = edgeTypes.find((et) => et.id === edge.edgeTypeId);
              if (!edgeType) {
                return (
                  <Table.Tr key={edge.id}>
                    <Table.Td colSpan={4}>
                      <Text c="yellow">Unknown edge type {edge.edgeTypeId}</Text>
                    </Table.Td>
                  </Table.Tr>
                );
              }
              return (
                <EdgeRow
                  key={edge.id}
                  edge={edge}
                  edgeType={edgeType}
                  thisRecordId={record.id}
                  onDelete={onDelete}
                  busy={del.isPending}
                />
              );
            })}
          </Table.Tbody>
        </Table>
      )}

      {dialogOpen && recordType && (
        <EdgeLinkDialog
          thisRecord={record}
          thisRecordType={recordType}
          onClose={() => setDialogOpen(false)}
          onSuccess={(message) => {
            toast.success(message);
            setDialogOpen(false);
          }}
          onError={(message) => toast.error(message)}
        />
      )}
    </Stack>
  );
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { message?: string } } }).response;
    return response?.data?.message ?? error.message;
  }
  return String(error);
}
