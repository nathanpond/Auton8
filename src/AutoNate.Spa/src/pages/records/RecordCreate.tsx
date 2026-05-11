import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { Box, Paper, Text } from "@mantine/core";
import PageHeader from "@/components/PageHeader";
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
    return (
      <Paper withBorder radius="md" p="lg" ta="center">
        <Text c="dimmed">Loading...</Text>
      </Paper>
    );
  }

  if (!type) {
    return (
      <Box py="md">
        <PageHeader
          title="New Record"
          description={
            <>
              Unknown record type <code>{code}</code>.{" "}
              <Link to="/record-types">Browse record types</Link>.
            </>
          }
        />
      </Box>
    );
  }

  return (
    <Box py="md">
      <PageHeader
        title={
          <>
            New {type.name} <code style={{ marginLeft: 8, fontSize: 16 }}>{type.shortCode}</code>
          </>
        }
        description={<Link to={`/records/${code}`}>&larr; Back to list</Link>}
      />

      <Paper withBorder radius="md" p="md">
        <RecordForm
          fields={fields}
          submitLabel="Create"
          topLevelError={error}
          onCancel={() => navigate(`/records/${code}`)}
          onSubmit={async ({ name, status, dueDate, values, assigneeIds }) => {
            try {
              setError(null);
              const created = await create.mutateAsync({
                recordTypeId: type.id,
                name,
                status,
                dueDate,
                values,
                assigneeIds: assigneeIds.length > 0 ? assigneeIds : null
              });
              navigate(`/record/${created.key}`);
            } catch (err) {
              setError(describeError(err));
            }
          }}
        />
      </Paper>
    </Box>
  );
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { message?: string } } }).response;
    return response?.data?.message ?? error.message;
  }
  return String(error);
}
