import { useState } from "react";
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
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(null);

  const onDelete = async (edgeId: string) => {
    if (!window.confirm("Remove this edge?")) return;
    try {
      await del.mutateAsync(edgeId);
      setFlash({ kind: "success", message: "Removed." });
    } catch (err) {
      setFlash({ kind: "error", message: describeError(err) });
    }
  };

  return (
    <>
      <div className="d-flex justify-content-between align-items-center mb-3">
        <h5 className="mb-0">Edges</h5>
        <button
          type="button"
          className="btn btn-primary btn-sm"
          onClick={() => setDialogOpen(true)}
          disabled={!recordType}
        >
          <i className="fa fa-link me-2"></i>New link
        </button>
      </div>

      {flash && (
        <div
          className={`alert ${flash.kind === "success" ? "alert-success" : "alert-danger"}`}
          role={flash.kind === "success" ? "status" : "alert"}
        >
          {flash.message}
        </div>
      )}

      {isLoading && <p className="text-body text-opacity-50 mb-0">Loading edges...</p>}

      {!isLoading && edges.length === 0 && (
        <p className="text-body text-opacity-50 mb-0">
          No edges yet. Click "New link" to relate this record to another.
        </p>
      )}

      {edges.length > 0 && (
        <div className="table-responsive">
          <table className="table table-sm table-bordered align-middle mb-0">
            <thead>
              <tr>
                <th>Relation</th>
                <th>Other record</th>
                <th>Data</th>
                <th style={{ width: "4rem" }}></th>
              </tr>
            </thead>
            <tbody>
              {edges.map((edge) => {
                const edgeType = edgeTypes.find((et) => et.id === edge.edgeTypeId);
                if (!edgeType) {
                  return (
                    <tr key={edge.id}>
                      <td colSpan={4} className="text-warning">
                        Unknown edge type {edge.edgeTypeId}
                      </td>
                    </tr>
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
            </tbody>
          </table>
        </div>
      )}

      {dialogOpen && recordType && (
        <EdgeLinkDialog
          thisRecord={record}
          thisRecordType={recordType}
          onClose={() => setDialogOpen(false)}
          onSuccess={(message) => {
            setFlash({ kind: "success", message });
            setDialogOpen(false);
          }}
          onError={(message) => setFlash({ kind: "error", message })}
        />
      )}
    </>
  );
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { message?: string } } }).response;
    return response?.data?.message ?? error.message;
  }
  return String(error);
}
