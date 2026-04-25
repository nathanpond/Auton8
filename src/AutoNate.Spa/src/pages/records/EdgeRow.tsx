import { Link } from "react-router-dom";
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

  const verb = direction === "outgoing"
    ? edgeType.name
    : edgeType.inverseName ?? `← ${edgeType.name}`;

  return (
    <tr>
      <td>
        <span className="badge bg-secondary me-2">{edgeType.shortCode}</span>
        {verb}
      </td>
      <td>
        {other ? (
          <Link to={`/record/${other.key}`}>
            <code className="me-2">{other.key}</code>
            {other.name}
          </Link>
        ) : (
          <span className="text-body text-opacity-50">{otherRecordId.substring(0, 8)}...</span>
        )}
      </td>
      <td className="text-body text-opacity-75 small">
        {Object.keys(edge.data).length === 0 ? (
          <em>—</em>
        ) : (
          <code>{JSON.stringify(edge.data)}</code>
        )}
      </td>
      <td className="text-end">
        <button
          type="button"
          className="btn btn-outline-danger btn-sm"
          onClick={() => onDelete(edge.id)}
          disabled={busy}
          title="Remove edge"
        >
          <i className="fa fa-trash"></i>
        </button>
      </td>
    </tr>
  );
}
