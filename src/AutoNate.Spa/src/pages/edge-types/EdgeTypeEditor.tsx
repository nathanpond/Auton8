import { toast } from "@/components/notifications/toast";
import { useEffect, useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import {
  Badge,
  Box,
  Button,
  Card,
  Divider,
  Grid,
  Group,
  Modal,
  NativeSelect,
  Switch,
  Table,
  Text,
  TextInput,
  Title
} from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import {
  useArchiveEdgeType,
  useCreateEdgeTypeField,
  useDeleteEdgeTypeField,
  useEdgeType,
  useEdgeTypeFields,
  useRestoreEdgeType,
  useUpdateEdgeType,
  useUpdateEdgeTypeField
} from "@/hooks/useRecordEdges";
import { useFieldTypes, useRecordTypes } from "@/hooks/useRecordTypes";
import { EdgeCardinality, EdgeTypeField, FieldDataType } from "@/types/records";
import FieldConfigPanel from "../record-types/FieldConfigPanel";
import { defaultFieldConfig, humanDataType } from "../record-types/fieldTypeDefaults";

type FieldModalState =
  | { kind: "none" }
  | { kind: "add" }
  | { kind: "edit"; field: EdgeTypeField };

export default function EdgeTypeEditor() {
  const { id } = useParams<{ id: string }>();
  const edgeTypeId = id ?? null;

  const { data: type, isLoading } = useEdgeType(edgeTypeId);
  const { data: fields = [] } = useEdgeTypeFields(edgeTypeId);
  const { data: fieldTypes = [] } = useFieldTypes();
  const { data: recordTypes = [] } = useRecordTypes(false);

  const update = useUpdateEdgeType(id ?? "");
  const archive = useArchiveEdgeType();
  const restore = useRestoreEdgeType();

  const [name, setName] = useState("");
  const [inverse, setInverse] = useState("");
  const [isDirected, setIsDirected] = useState(true);
  const [allowSelfRef, setAllowSelfRef] = useState(false);
  const [cardinality, setCardinality] = useState<EdgeCardinality>("many_to_many");
  const [fromTypes, setFromTypes] = useState<string[]>([]);
  const [toTypes, setToTypes] = useState<string[]>([]);
  const [fieldModal, setFieldModal] = useState<FieldModalState>({ kind: "none" });

  useEffect(() => {
    if (!type) return;
    setName(type.name);
    setInverse(type.inverseName ?? "");
    setIsDirected(type.isDirected);
    setAllowSelfRef(type.allowSelfReference);
    setCardinality(type.cardinality);
    setFromTypes(type.fromRecordTypeIds ?? []);
    setToTypes(type.toRecordTypeIds ?? []);
  }, [type]);

  const dirty = useMemo(() => {
    if (!type) return false;
    return (
      name !== type.name ||
      (inverse || null) !== (type.inverseName ?? null) ||
      isDirected !== type.isDirected ||
      allowSelfRef !== type.allowSelfReference ||
      cardinality !== type.cardinality ||
      !arraysEqual(fromTypes, type.fromRecordTypeIds ?? []) ||
      !arraysEqual(toTypes, type.toRecordTypeIds ?? [])
    );
  }, [type, name, inverse, isDirected, allowSelfRef, cardinality, fromTypes, toTypes]);

  if (isLoading || !type) {
    return (
      <Card withBorder p="lg" ta="center">
        <Text c="dimmed">{isLoading ? "Loading..." : "Edge type not found."}</Text>
      </Card>
    );
  }

  const save = async () => {
    try {
      await update.mutateAsync({
        name: name.trim(),
        inverseName: inverse.trim() || null,
        isDirected,
        allowSelfReference: allowSelfRef,
        cardinality,
        fromRecordTypeIds: fromTypes.length === 0 ? null : fromTypes,
        toRecordTypeIds: toTypes.length === 0 ? null : toTypes
      });
      toast.success("Saved.");
    } catch (err) {
      toast.error(describeError(err));
    }
  };

  const toggleArchived = async () => {
    try {
      if (type.isArchived) {
        await restore.mutateAsync(type.id);
        toast.success("Restored.");
      } else {
        await archive.mutateAsync(type.id);
        toast.success("Archived.");
      }
    } catch (err) {
      toast.error(describeError(err));
    }
  };

  return (
    <>
      <PageHeader
        title={
          <Group gap="xs" wrap="wrap" align="center">
            <code style={{ marginRight: 4 }}>{type.shortCode}</code>
            <Title order={1} m={0} style={{ display: "inline" }}>
              {type.name}
            </Title>
            {type.isArchived && (
              <Badge color="gray" variant="filled">
                Archived
              </Badge>
            )}
          </Group>
        }
        description={<Link to="/record-relationship-types">&larr; Back to relationship types</Link>}
        actions={
          <Button
            variant="outline"
            color={type.isArchived ? "green" : "yellow"}
            leftSection={
              <i className={`fa ${type.isArchived ? "fa-box-open" : "fa-box-archive"}`} />
            }
            onClick={toggleArchived}
            loading={archive.isPending || restore.isPending}
          >
            {type.isArchived ? "Restore" : "Archive"}
          </Button>
        }
      />

      <Card withBorder shadow="sm" mb="md">
        <Title order={5} mb="md">
          Settings
        </Title>
        <Grid>
          <Grid.Col span={{ base: 12, md: 6 }}>
            <TextInput
              label="Forward name"
              value={name}
              onChange={(e) => setName(e.currentTarget.value)}
            />
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 6 }}>
            <TextInput
              label="Inverse name"
              value={inverse}
              onChange={(e) => setInverse(e.currentTarget.value)}
              placeholder={isDirected ? "Optional" : "Used as the symmetric label"}
            />
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 3 }}>
            <NativeSelect
              label="Cardinality"
              value={cardinality}
              onChange={(e) => setCardinality(e.currentTarget.value as EdgeCardinality)}
              data={[
                { value: "many_to_many", label: "many_to_many" },
                { value: "one_to_one", label: "one_to_one" },
                { value: "one_to_many", label: "one_to_many" },
                { value: "many_to_one", label: "many_to_one" }
              ]}
            />
          </Grid.Col>
          <Grid.Col span={{ base: 6, md: 3 }} style={{ display: "flex", alignItems: "flex-end" }}>
            <Switch
              id="ee-directed"
              checked={isDirected}
              onChange={(e) => setIsDirected(e.currentTarget.checked)}
              label="Directed"
            />
          </Grid.Col>
          <Grid.Col span={{ base: 6, md: 3 }} style={{ display: "flex", alignItems: "flex-end" }}>
            <Switch
              id="ee-self-ref"
              checked={allowSelfRef}
              onChange={(e) => setAllowSelfRef(e.currentTarget.checked)}
              label="Allow self-reference"
            />
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 6 }}>
            <Text size="sm" fw={500} mb={4}>
              Allowed source record types
            </Text>
            <RecordTypeMultiSelect
              value={fromTypes}
              onChange={setFromTypes}
              options={recordTypes.map((rt) => ({ id: rt.id, label: `${rt.shortCode} - ${rt.name}` }))}
            />
            <Text size="xs" c="dimmed" mt={4}>
              Leave empty to allow any record type as source.
            </Text>
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 6 }}>
            <Text size="sm" fw={500} mb={4}>
              Allowed target record types
            </Text>
            <RecordTypeMultiSelect
              value={toTypes}
              onChange={setToTypes}
              options={recordTypes.map((rt) => ({ id: rt.id, label: `${rt.shortCode} - ${rt.name}` }))}
            />
            <Text size="xs" c="dimmed" mt={4}>
              Leave empty to allow any record type as target.
            </Text>
          </Grid.Col>
        </Grid>
        <Group justify="flex-end" mt="md">
          <Button onClick={save} disabled={!dirty || update.isPending} loading={update.isPending}>
            Save settings
          </Button>
        </Group>
      </Card>

      <Card withBorder shadow="sm">
        <Group justify="space-between" align="center" mb="md">
          <Title order={5} m={0}>
            Edge data fields
          </Title>
          <Button
            size="xs"
            onClick={() => setFieldModal({ kind: "add" })}
            disabled={fieldTypes.length === 0}
            leftSection={<i className="fa fa-plus" />}
          >
            Add field
          </Button>
        </Group>
        {fields.length === 0 && (
          <Text c="dimmed">
            No edge data fields. Edges of this type will only carry the source/target references.
          </Text>
        )}
        {fields.length > 0 && (
          <Table withTableBorder withColumnBorders striped verticalSpacing="xs">
            <Table.Thead>
              <Table.Tr>
                <Table.Th style={{ width: "4rem" }}>#</Table.Th>
                <Table.Th>Key</Table.Th>
                <Table.Th>Display name</Table.Th>
                <Table.Th style={{ width: "9rem" }}>Type</Table.Th>
                <Table.Th style={{ width: "6rem" }}>Required</Table.Th>
                <Table.Th style={{ width: "5rem" }} />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {fields.map((f) => (
                <Table.Tr key={f.id}>
                  <Table.Td>{f.sortOrder}</Table.Td>
                  <Table.Td>
                    <code>{f.fieldKey}</code>
                  </Table.Td>
                  <Table.Td>{f.displayName}</Table.Td>
                  <Table.Td>{humanDataType(f.dataType)}</Table.Td>
                  <Table.Td>{f.isRequired ? "Yes" : ""}</Table.Td>
                  <Table.Td ta="right">
                    <Button
                      size="xs"
                      variant="default"
                      onClick={() => setFieldModal({ kind: "edit", field: f })}
                    >
                      Edit
                    </Button>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Card>

      {fieldModal.kind !== "none" && (
        <EdgeFieldModal
          edgeTypeId={type.id}
          state={fieldModal}
          dataTypes={fieldTypes.map((ft) => ft.dataType)}
          onClose={() => setFieldModal({ kind: "none" })}
          onSuccess={(message) => {
            toast.success(message);
            setFieldModal({ kind: "none" });
          }}
          onError={(m) => toast.error(m)}
        />
      )}
    </>
  );
}

function RecordTypeMultiSelect({
  value,
  onChange,
  options
}: {
  value: string[];
  onChange: (next: string[]) => void;
  options: { id: string; label: string }[];
}) {
  return (
    <Box
      p="xs"
      style={{
        display: "flex",
        flexWrap: "wrap",
        gap: 8,
        border: "1px solid var(--mantine-color-default-border)",
        borderRadius: "var(--mantine-radius-default)"
      }}
    >
      {options.length === 0 && (
        <Text size="sm" c="dimmed">
          No record types available.
        </Text>
      )}
      {options.map((opt) => {
        const selected = value.includes(opt.id);
        return (
          <Button
            key={opt.id}
            size="xs"
            variant={selected ? "filled" : "default"}
            onClick={() => {
              onChange(selected ? value.filter((v) => v !== opt.id) : [...value, opt.id]);
            }}
          >
            {opt.label}
          </Button>
        );
      })}
    </Box>
  );
}

function EdgeFieldModal({
  edgeTypeId,
  state,
  dataTypes,
  onClose,
  onSuccess,
  onError
}: {
  edgeTypeId: string;
  state: Exclude<FieldModalState, { kind: "none" }>;
  dataTypes: FieldDataType[];
  onClose: () => void;
  onSuccess: (m: string) => void;
  onError: (m: string) => void;
}) {
  const isEdit = state.kind === "edit";
  const existing = isEdit ? state.field : null;
  const [fieldKey, setFieldKey] = useState(existing?.fieldKey ?? "");
  const [displayName, setDisplayName] = useState(existing?.displayName ?? "");
  const [dataType, setDataType] = useState<FieldDataType>(existing?.dataType ?? (dataTypes[0] ?? "text"));
  const [config, setConfig] = useState<Record<string, unknown>>(
    existing?.config ?? defaultFieldConfig(dataTypes[0] ?? "text")
  );
  const [isRequired, setIsRequired] = useState(existing?.isRequired ?? false);
  const [sortOrder, setSortOrder] = useState(existing?.sortOrder ?? 0);

  const create = useCreateEdgeTypeField(edgeTypeId);
  const update = useUpdateEdgeTypeField(edgeTypeId);
  const del = useDeleteEdgeTypeField(edgeTypeId);

  const onTypeChange = (next: FieldDataType) => {
    setDataType(next);
    if (!isEdit) setConfig(defaultFieldConfig(next));
  };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      if (isEdit && existing) {
        await update.mutateAsync({
          fieldId: existing.id,
          request: { displayName: displayName.trim(), config, isRequired, sortOrder }
        });
        onSuccess(`Updated ${existing.fieldKey}.`);
      } else {
        await create.mutateAsync({
          fieldKey: fieldKey.trim().toLowerCase(),
          displayName: displayName.trim(),
          dataType,
          config,
          isRequired,
          sortOrder
        });
        onSuccess(`Added ${fieldKey}.`);
      }
    } catch (err) {
      onError(describeError(err));
    }
  };

  const remove = async () => {
    if (!existing) return;
    try {
      await del.mutateAsync(existing.id);
      onSuccess(`Removed ${existing.fieldKey}.`);
    } catch (err) {
      onError(describeError(err));
    }
  };

  return (
    <Modal
      opened
      onClose={onClose}
      title={isEdit ? `Edit field: ${existing?.fieldKey}` : "Add edge field"}
      size="lg"
    >
      <Box component="form" onSubmit={submit}>
        <Grid>
          <Grid.Col span={{ base: 12, md: 6 }}>
            <TextInput
              label="Field key"
              value={fieldKey}
              onChange={(e) => setFieldKey(e.currentTarget.value)}
              placeholder="weight"
              required={!isEdit}
              disabled={isEdit}
            />
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 6 }}>
            <TextInput
              label="Display name"
              value={displayName}
              onChange={(e) => setDisplayName(e.currentTarget.value)}
              required
            />
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 6 }}>
            <NativeSelect
              label="Data type"
              value={dataType}
              onChange={(e) => onTypeChange(e.currentTarget.value as FieldDataType)}
              disabled={isEdit}
              data={dataTypes.map((dt) => ({ value: dt, label: humanDataType(dt) }))}
            />
          </Grid.Col>
          <Grid.Col span={{ base: 6, md: 3 }}>
            <TextInput
              label="Sort order"
              type="number"
              value={sortOrder}
              onChange={(e) => setSortOrder(Number(e.currentTarget.value))}
            />
          </Grid.Col>
          <Grid.Col span={{ base: 6, md: 3 }} style={{ display: "flex", alignItems: "flex-end" }}>
            <Switch
              id="edge-field-required"
              checked={isRequired}
              onChange={(e) => setIsRequired(e.currentTarget.checked)}
              label="Required"
            />
          </Grid.Col>
        </Grid>
        <Divider my="md" />
        <Title order={6} mb="md">
          Configuration
        </Title>
        <FieldConfigPanel dataType={dataType} config={config} onChange={setConfig} />
        <Group justify="space-between" mt="md">
          <Box>
            {isEdit && (
              <Button variant="outline" color="red" onClick={remove} disabled={del.isPending}>
                Delete field
              </Button>
            )}
          </Box>
          <Group gap="xs">
            <Button variant="default" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" loading={create.isPending || update.isPending}>
              {isEdit ? "Save" : "Add field"}
            </Button>
          </Group>
        </Group>
      </Box>
    </Modal>
  );
}

function arraysEqual(a: string[], b: string[]) {
  if (a.length !== b.length) return false;
  const sortedA = [...a].sort();
  const sortedB = [...b].sort();
  for (let i = 0; i < sortedA.length; i++) if (sortedA[i] !== sortedB[i]) return false;
  return true;
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { message?: string } } }).response;
    return response?.data?.message ?? error.message;
  }
  return String(error);
}
