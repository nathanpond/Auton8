import { useEffect, useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import {
  Alert,
  Badge,
  Box,
  Button,
  Card,
  Divider,
  Grid,
  Group,
  Modal,
  NativeSelect,
  Stack,
  Switch,
  Table,
  Text,
  Textarea,
  TextInput,
  Title
} from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import {
  useArchiveField,
  useArchiveRecordType,
  useCreateField,
  useFieldTypes,
  useRecordType,
  useRecordTypeFields,
  useRestoreField,
  useRestoreRecordType,
  useUpdateField,
  useUpdateRecordType
} from "@/hooks/useRecordTypes";
import { FieldDataType, RecordTypeField } from "@/types/records";
import IconPicker from "@/components/IconPicker";
import ColorPicker from "@/components/ColorPicker";
import FieldConfigPanel from "./FieldConfigPanel";
import SchemaAuditPanel from "./SchemaAuditPanel";
import { defaultFieldConfig, humanDataType } from "./fieldTypeDefaults";

type FieldModalState =
  | { kind: "none" }
  | { kind: "add" }
  | { kind: "edit"; field: RecordTypeField };

export default function RecordTypeEditor() {
  const { id } = useParams<{ id: string }>();
  const recordTypeId = id ?? null;

  const { data: type, isLoading } = useRecordType(recordTypeId);
  const [includeArchivedFields, setIncludeArchivedFields] = useState(false);
  const { data: fields = [] } = useRecordTypeFields(recordTypeId, includeArchivedFields);
  const { data: fieldTypes = [] } = useFieldTypes();

  const update = useUpdateRecordType(id ?? "");
  const archive = useArchiveRecordType();
  const restore = useRestoreRecordType();

  const [nameDraft, setNameDraft] = useState("");
  const [descDraft, setDescDraft] = useState("");
  const [iconDraft, setIconDraft] = useState("");
  const [colorDraft, setColorDraft] = useState("");
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(null);
  const [fieldModal, setFieldModal] = useState<FieldModalState>({ kind: "none" });

  useEffect(() => {
    if (type) {
      setNameDraft(type.name);
      setDescDraft(type.description ?? "");
      setIconDraft(type.icon ?? "");
      setColorDraft(type.color ?? "");
    }
  }, [type]);

  const dirty = useMemo(() => {
    if (!type) return false;
    return (
      nameDraft !== type.name ||
      (descDraft || null) !== (type.description ?? null) ||
      (iconDraft || null) !== (type.icon ?? null) ||
      (colorDraft || null) !== (type.color ?? null)
    );
  }, [type, nameDraft, descDraft, iconDraft, colorDraft]);

  if (isLoading || !type) {
    return (
      <Box py="md">
        <PageHeader title="Record Type" />
        <Card withBorder p="lg" ta="center">
          <Text c="dimmed">{isLoading ? "Loading..." : "Record type not found."}</Text>
        </Card>
      </Box>
    );
  }

  const saveDetails = async () => {
    try {
      await update.mutateAsync({
        name: nameDraft.trim(),
        description: descDraft.trim() || null,
        icon: iconDraft.trim() || null,
        color: colorDraft.trim() || null
      });
      setFlash({ kind: "success", message: "Saved." });
    } catch (err) {
      setFlash({ kind: "error", message: describeError(err) });
    }
  };

  const toggleArchived = async () => {
    try {
      if (type.isArchived) {
        await restore.mutateAsync(type.id);
        setFlash({ kind: "success", message: `Restored ${type.shortCode}.` });
      } else {
        await archive.mutateAsync(type.id);
        setFlash({ kind: "success", message: `Archived ${type.shortCode}.` });
      }
    } catch (err) {
      setFlash({ kind: "error", message: describeError(err) });
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
        description={<Link to="/record-types">&larr; Back to record types</Link>}
        actions={
          <Group gap="xs">
            <Button
              component={Link}
              to={`/records/${type.shortCode}`}
              variant="default"
              leftSection={<i className="fa fa-list" />}
            >
              View records
            </Button>
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
          </Group>
        }
      />

      {flash && (
        <Alert
          color={flash.kind === "success" ? "green" : "red"}
          variant="light"
          role={flash.kind === "success" ? "status" : "alert"}
          mb="md"
        >
          {flash.message}
        </Alert>
      )}

      <Card withBorder shadow="sm" mb="md">
        <Title order={5} mb="md">
          Details
        </Title>
        <Grid>
          <Grid.Col span={{ base: 12, md: 6 }}>
            <TextInput
              label="Name"
              value={nameDraft}
              onChange={(e) => setNameDraft(e.currentTarget.value)}
            />
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 3 }}>
            <Text size="sm" fw={500} mb={4}>
              Icon
            </Text>
            <IconPicker value={iconDraft} onChange={setIconDraft} />
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 3 }}>
            <Text size="sm" fw={500} mb={4}>
              Color
            </Text>
            <ColorPicker value={colorDraft} onChange={setColorDraft} />
          </Grid.Col>
          <Grid.Col span={12}>
            <Textarea
              label="Description"
              rows={3}
              value={descDraft}
              onChange={(e) => setDescDraft(e.currentTarget.value)}
            />
          </Grid.Col>
        </Grid>
        <Group justify="flex-end" mt="md">
          <Button onClick={saveDetails} disabled={!dirty || update.isPending} loading={update.isPending}>
            Save details
          </Button>
        </Group>
      </Card>

      <Card withBorder shadow="sm" mb="md">
        <Group justify="space-between" align="center" mb="md">
          <Title order={5} m={0}>
            Fields
          </Title>
          <Group gap="md" align="center">
            <Switch
              id="include-archived-fields"
              size="sm"
              checked={includeArchivedFields}
              onChange={(e) => setIncludeArchivedFields(e.currentTarget.checked)}
              label="Show archived"
            />
            <Button
              size="xs"
              onClick={() => setFieldModal({ kind: "add" })}
              disabled={fieldTypes.length === 0}
              leftSection={<i className="fa fa-plus" />}
            >
              Add field
            </Button>
          </Group>
        </Group>
        <Table withTableBorder withColumnBorders striped verticalSpacing="xs">
          <Table.Thead>
            <Table.Tr>
              <Table.Th style={{ width: "4rem" }}>#</Table.Th>
              <Table.Th>Field key</Table.Th>
              <Table.Th>Display name</Table.Th>
              <Table.Th style={{ width: "9rem" }}>Type</Table.Th>
              <Table.Th style={{ width: "6rem" }}>Required</Table.Th>
              <Table.Th style={{ width: "9rem" }}>Status</Table.Th>
              <Table.Th style={{ width: "6rem" }} />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {fields.length === 0 && (
              <Table.Tr>
                <Table.Td colSpan={7} ta="center" py="lg">
                  <Text c="dimmed">No fields yet. Add one to start capturing data.</Text>
                </Table.Td>
              </Table.Tr>
            )}
            {fields.map((f) => (
              <Table.Tr key={f.id} style={f.isArchived ? { opacity: 0.5 } : undefined}>
                <Table.Td>{f.sortOrder}</Table.Td>
                <Table.Td>
                  <code>{f.fieldKey}</code>
                </Table.Td>
                <Table.Td>{f.displayName}</Table.Td>
                <Table.Td>{humanDataType(f.dataType)}</Table.Td>
                <Table.Td>{f.isRequired ? "Yes" : ""}</Table.Td>
                <Table.Td>
                  {f.isArchived ? (
                    <Badge color="gray" variant="filled">
                      Archived
                    </Badge>
                  ) : (
                    <Badge color="green" variant="filled">
                      Active
                    </Badge>
                  )}
                </Table.Td>
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
      </Card>

      <Card withBorder shadow="sm">
        <Title order={5} mb="md">
          Schema change history
        </Title>
        <SchemaAuditPanel recordTypeId={type.id} />
      </Card>

      {fieldModal.kind !== "none" && (
        <FieldModal
          recordTypeId={type.id}
          state={fieldModal}
          dataTypes={fieldTypes.map((ft) => ft.dataType)}
          onClose={() => setFieldModal({ kind: "none" })}
          onSuccess={(message) => {
            setFlash({ kind: "success", message });
            setFieldModal({ kind: "none" });
          }}
          onError={(m) => setFlash({ kind: "error", message: m })}
        />
      )}

    </>
  );
}

function FieldModal({
  recordTypeId,
  state,
  dataTypes,
  onClose,
  onSuccess,
  onError
}: {
  recordTypeId: string;
  state: Exclude<FieldModalState, { kind: "none" }>;
  dataTypes: FieldDataType[];
  onClose: () => void;
  onSuccess: (message: string) => void;
  onError: (m: string) => void;
}) {
  const isEdit = state.kind === "edit";
  const existing = isEdit ? state.field : null;

  const [fieldKey, setFieldKey] = useState(existing?.fieldKey ?? "");
  const [displayName, setDisplayName] = useState(existing?.displayName ?? "");
  const [dataType, setDataType] = useState<FieldDataType>(
    existing?.dataType ?? (dataTypes[0] ?? "text")
  );
  const [config, setConfig] = useState<Record<string, unknown>>(
    existing?.config ?? defaultFieldConfig(dataTypes[0] ?? "text")
  );
  const [isRequired, setIsRequired] = useState(existing?.isRequired ?? false);
  const [sortOrder, setSortOrder] = useState(existing?.sortOrder ?? 0);

  const create = useCreateField(recordTypeId);
  const update = useUpdateField(recordTypeId);
  const archive = useArchiveField(recordTypeId);
  const restore = useRestoreField(recordTypeId);

  const onTypeChange = (next: FieldDataType) => {
    setDataType(next);
    if (!isEdit) {
      setConfig(defaultFieldConfig(next));
    }
  };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      if (isEdit && existing) {
        await update.mutateAsync({
          fieldId: existing.id,
          request: {
            displayName: displayName.trim(),
            config,
            isRequired,
            sortOrder
          }
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

  const toggleArchived = async () => {
    if (!existing) return;
    try {
      if (existing.isArchived) {
        await restore.mutateAsync(existing.id);
        onSuccess(`Restored ${existing.fieldKey}.`);
      } else {
        await archive.mutateAsync(existing.id);
        onSuccess(`Archived ${existing.fieldKey}.`);
      }
    } catch (err) {
      onError(describeError(err));
    }
  };

  return (
    <Modal
      opened
      onClose={onClose}
      title={isEdit ? `Edit field: ${existing?.fieldKey}` : "Add field"}
      size="lg"
    >
      <Box component="form" onSubmit={submit}>
        <Grid>
          <Grid.Col span={{ base: 12, md: 6 }}>
            <TextInput
              label="Field key"
              value={fieldKey}
              onChange={(e) => setFieldKey(e.currentTarget.value)}
              placeholder="status"
              required={!isEdit}
              disabled={isEdit}
              description="Lowercase snake_case. Used as the stable identifier in data and filters. Cannot be changed later."
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
              description={
                isEdit
                  ? "Data type cannot change. Archive this field and add a new one instead."
                  : undefined
              }
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
              id="field-required"
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
            {isEdit && existing && (
              <Button
                variant="outline"
                color={existing.isArchived ? "green" : "yellow"}
                onClick={toggleArchived}
                disabled={archive.isPending || restore.isPending}
              >
                {existing.isArchived ? "Restore field" : "Archive field"}
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

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { message?: string } } }).response;
    return response?.data?.message ?? error.message;
  }
  return String(error);
}
