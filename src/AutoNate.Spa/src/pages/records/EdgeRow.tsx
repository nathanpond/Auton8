import { Link } from "react-router-dom";
import { ActionIcon, Badge, Code, Table, Text } from "@mantine/core";
import { useRecord } from "@/hooks/useRecords";
import { Edge, EdgeType } from "@/types/records";

type Props = {
  edge: Edge;
  edgeType: EdgeType;
  thisRecordId: string;
  onDelete: (edgeId: string) => void;
  busy?: boolean;
};

export default function EdgeRow({ edge, edgeType, thisRecordId, onDelete, busy }: Props) {
  const direction = edge.fromRecordId === thisRecordId ? "outgoing" : "incoming";
  const otherRecordId = direction === "outgoing" ? edge.toRecordId : edge.fromRecordId;
  const { data: other } = useRecord(otherRecordId);

  const verb =
    direction === "outgoing" ? edgeType.name : edgeType.inverseName ?? `← ${edgeType.name}`;

  return (
    <Table.Tr>
      <Table.Td>
        <Badge color="gray" variant="filled" mr={8}>
          {edgeType.shortCode}
        </Badge>
        {verb}
      </Table.Td>
      <Table.Td>
        {other ? (
          <Link to={`/record/${other.key}`}>
            <Code mr={8}>{other.key}</Code>
            {other.name}
          </Link>
        ) : (
          <Text c="dimmed" component="span">
            {otherRecordId.substring(0, 8)}...
          </Text>
        )}
      </Table.Td>
      <Table.Td>
        <Text size="sm" c="dimmed" component="div">
          {Object.keys(edge.data).length === 0 ? <em>—</em> : <Code>{JSON.stringify(edge.data)}</Code>}
        </Text>
      </Table.Td>
      <Table.Td ta="right">
        <ActionIcon
          variant="outline"
          color="red"
          size="sm"
          onClick={() => onDelete(edge.id)}
          disabled={busy}
          title="Remove edge"
        >
          <i className="fa fa-trash" />
        </ActionIcon>
      </Table.Td>
    </Table.Tr>
  );
}
