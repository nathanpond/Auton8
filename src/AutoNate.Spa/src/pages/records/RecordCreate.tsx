import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useCreateRecord } from "@/hooks/useRecords";
import { useRecordTypeFields, useRecordTypes } from "@/hooks/useRecordTypes";
import "./fields/renderers";
import RecordForm from "./RecordForm";

export default function RecordCreate() {
  const { typeShortCode = "" } = useParams<{ typeShortCode: string }>();
  const code = typeShortCode.toUpperCase();
  const navigate = useNavigate();

  const { data: types = [], isLoading: loadingTypes } = useRecordTypes(true);
  const type = types.find((t) => t.shortCode === code) ?? null;
  const { data: fields = [] } = useRecordTypeFields(type?.id ?? null, false);

  const create = useCreateRecord();
  const [error, setError] = useState<string | null>(null);

  if (loadingTypes) {
    return <div className="panel panel-inverse"><div className="panel-body p-4 text-center">Loading...</div></div>;
  }

  if (!type) {
    return (
      <div className="page-head">
        <h1 className="page-header mb-1">New Record</h1>
        <p className="page-head-copy">
          Unknown record type <code>{code}</code>.{" "}
          <Link to="/record-types">Browse record types</Link>.
        </p>
      </div>
    );
  }

  return (
    <>
      <div className="page-head">
        <h1 className="page-header mb-1">
          New {type.name} <code className="ms-2 fs-6">{type.shortCode}</code>
        </h1>
        <p className="page-head-copy mb-0">
          <Link to={`/records/${code}`}>&larr; Back to list</Link>
        </p>
      </div>

      <div className="panel panel-inverse">
        <div className="panel-body">
          <RecordForm
            fields={fields}
            submitLabel="Create"
            topLevelError={error}
            onCancel={() => navigate(`/records/${code}`)}
            onSubmit={async ({ name, values, assigneeIds }) => {
              try {
                setError(null);
                const created = await create.mutateAsync({
                  recordTypeId: type.id,
                  name,
                  values,
                  assigneeIds: assigneeIds.length > 0 ? assigneeIds : null
                });
                navigate(`/record/${created.key}`);
              } catch (err) {
                setError(describeError(err));
              }
            }}
          />
        </div>
      </div>
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
