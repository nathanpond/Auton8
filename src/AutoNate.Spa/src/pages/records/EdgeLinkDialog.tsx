import { useEffect, useMemo, useState } from "react";
import {
  Box,
  Button,
  Code,
  Divider,
  Grid,
  Group,
  Modal,
  Select,
  Switch,
  Text,
  TextInput,
  Title
} from "@mantine/core";
import { useCreateEdge, useEdgeTypeFields, useEdgeTypes } from "@/hooks/useRecordEdges";
import { useRecords } from "@/hooks/useRecords";
import { useRecordTypes } from "@/hooks/useRecordTypes";
import { EdgeType, RecordModel, RecordType } from "@/types/records";
import "./fields/renderers";
import { defaultFieldConfig } from "../record-types/fieldTypeDefaults";
import { getRenderer } from "./fields/registry";

type Props = {
  thisRecord: RecordModel;
  thisRecordType: RecordType;
  onClose: () => void;
  onSuccess: (message: string) => void;
  onError: (message: string) => void;
};

type LinkDirection = "outgoing" | "incoming";

export default function EdgeLinkDialog({
  thisRecord,
  thisRecordType,
  onClose,
  onSuccess,
  onError
}: Props) {
  const { data: edgeTypes = [] } = useEdgeTypes(false);
  const { data: recordTypes = [] } = useRecordTypes(false);

  const candidateEdgeTypes = useMemo(
    () =>
      edgeTypes.filter((et) => {
        const fromOk = !et.fromRecordTypeIds || et.fromRecordTypeIds.includes(thisRecordType.id);
        const toOk = !et.toRecordTypeIds || et.toRecordTypeIds.includes(thisRecordType.id);
        return fromOk || toOk;
      }),
    [edgeTypes, thisRecordType.id]
  );

  const [edgeTypeId, setEdgeTypeId] = useState<string>(candidateEdgeTypes[0]?.id ?? "");
  useEffect(() => {
    if (!edgeTypeId && candidateEdgeTypes.length > 0) {
      setEdgeTypeId(candidateEdgeTypes[0].id);
    }
  }, [candidateEdgeTypes, edgeTypeId]);

  const edgeType = candidateEdgeTypes.find((et) => et.id === edgeTypeId);

  const fromAllowed = edgeType
    ? !edgeType.fromRecordTypeIds || edgeType.fromRecordTypeIds.includes(thisRecordType.id)
    : false;
  const toAllowed = edgeType
    ? !edgeType.toRecordTypeIds || edgeType.toRecordTypeIds.includes(thisRecordType.id)
    : false;

  const [direction, setDirection] = useState<LinkDirection>(
    fromAllowed ? "outgoing" : "incoming"
  );
  useEffect(() => {
    if (!fromAllowed && toAllowed) setDirection("incoming");
    else if (fromAllowed && !toAllowed) setDirection("outgoing");
  }, [fromAllowed, toAllowed]);

  const otherSideAllowedTypeIds = useMemo<string[] | null>(() => {
    if (!edgeType) return null;
    return direction === "outgoing"
      ? edgeType.toRecordTypeIds ?? null
      : edgeType.fromRecordTypeIds ?? null;
  }, [edgeType, direction]);

  const otherTypeOptions = useMemo<RecordType[]>(() => {
    if (otherSideAllowedTypeIds === null) return recordTypes;
    return recordTypes.filter((rt) => otherSideAllowedTypeIds.includes(rt.id));
  }, [recordTypes, otherSideAllowedTypeIds]);

  const [otherTypeId, setOtherTypeId] = useState<string>("");
  useEffect(() => {
    if (otherTypeOptions.length > 0 && !otherTypeOptions.find((t) => t.id === otherTypeId)) {
      setOtherTypeId(otherTypeOptions[0].id);
    }
  }, [otherTypeOptions, otherTypeId]);

  const { data: candidates } = useRecords(
    {
      recordTypeId: otherTypeId,
      page: 0,
      pageSize: 200,
      includeArchived: false,
      sort: "updated_desc"
    },
    Boolean(otherTypeId)
  );

  const [otherRecordId, setOtherRecordId] = useState<string>("");

  const { data: edgeFields = [] } = useEdgeTypeFields(edgeTypeId || null);
  const [data, setData] = useState<Record<string, unknown>>({});
  useEffect(() => {
    const next: Record<string, unknown> = {};
    for (const f of edgeFields) {
      next[f.fieldKey] = (defaultFieldConfig(f.dataType) as unknown) === f.config ? "" : "";
    }
    setData(next);
  }, [edgeFields]);

  const create = useCreateEdge(thisRecord.id);

  if (candidateEdgeTypes.length === 0) {
    return (
      <Modal opened onClose={onClose} title="Link to another record" size="lg">
        <Text>
          No edge types are configured for record type <Code>{thisRecordType.shortCode}</Code>.
          Define one under <strong>Edge Types</strong> first.
        </Text>
        <Group justify="flex-end" mt="md">
          <Button variant="default" onClick={onClose}>
            Close
          </Button>
        </Group>
      </Modal>
    );
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!edgeType || !otherRecordId) return;
    const fromId = direction === "outgoing" ? thisRecord.id : otherRecordId;
    const toId = direction === "outgoing" ? otherRecordId : thisRecord.id;
    try {
      await create.mutateAsync({
        edgeTypeId: edgeType.id,
        fromRecordId: fromId,
        toRecordId: toId,
        data
      });
      onSuccess("Linked.");
    } catch (err) {
      onError(describeError(err));
    }
  };

  const directionData: { value: LinkDirection; label: string }[] = [
    { value: "outgoing", label: `This record ${labelFor(edgeType, "forward")} other` },
    { value: "incoming", label: `Other ${labelFor(edgeType, "forward")} this record` }
  ];

  return (
    <Modal opened onClose={onClose} title="Link to another record" size="lg">
      <Box component="form" onSubmit={submit}>
        <Grid>
          <Grid.Col span={{ base: 12, md: 6 }}>
            <Select
              label="Edge type"
              value={edgeTypeId || null}
              onChange={(v) => setEdgeTypeId(v ?? "")}
              data={candidateEdgeTypes.map((et) => ({
                value: et.id,
                label: `${et.shortCode} - ${et.name}`
              }))}
              allowDeselect={false}
            />
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 6 }}>
            <Select
              label="Direction"
              value={direction}
              onChange={(v) => setDirection((v as LinkDirection) ?? "outgoing")}
              disabled={!fromAllowed || !toAllowed || !edgeType?.isDirected}
              data={directionData}
              allowDeselect={false}
            />
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 5 }}>
            <Select
              label="Other record type"
              value={otherTypeId || null}
              onChange={(v) => setOtherTypeId(v ?? "")}
              data={otherTypeOptions.map((rt) => ({
                value: rt.id,
                label: `${rt.shortCode} - ${rt.name}`
              }))}
              disabled={otherTypeOptions.length === 0}
              allowDeselect={false}
            />
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 7 }}>
            <Select
              label="Other record"
              value={otherRecordId || null}
              onChange={(v) => setOtherRecordId(v ?? "")}
              placeholder="Select..."
              disabled={!candidates || candidates.items.length === 0}
              data={(candidates?.items ?? [])
                .filter((r) => r.id !== thisRecord.id || edgeType?.allowSelfReference)
                .map((r) => ({ value: r.id, label: `${r.key} — ${r.name}` }))}
              searchable
              description={
                candidates && candidates.totalCount > candidates.items.length
                  ? `Showing ${candidates.items.length} of ${candidates.totalCount}.`
                  : undefined
              }
            />
          </Grid.Col>
          {edgeFields.length > 0 && (
            <Grid.Col span={12}>
              <Divider my="xs" />
              <Title order={6} mb="sm">
                Edge data
              </Title>
              <Grid>
                {edgeFields.map((field) => {
                  const renderer = getRenderer(field.dataType);
                  if (!renderer) {
                    return (
                      <Grid.Col key={field.id} span={{ base: 12, md: 6 }}>
                        <TextInput
                          label={field.displayName}
                          placeholder={`(${field.dataType})`}
                          value={String(data[field.fieldKey] ?? "")}
                          onChange={(e) =>
                            setData((d) => ({ ...d, [field.fieldKey]: e.currentTarget.value }))
                          }
                        />
                      </Grid.Col>
                    );
                  }
                  return (
                    <Grid.Col key={field.id} span={{ base: 12, md: 6 }}>
                      <Box>
                        <Text size="sm" fw={500} mb={4}>
                          {field.displayName}
                          {field.isRequired && (
                            <Text component="span" c="red" ml={4}>
                              *
                            </Text>
                          )}
                        </Text>
                        <SimpleFieldInput
                          fieldKey={field.fieldKey}
                          dataType={field.dataType}
                          value={data[field.fieldKey]}
                          onChange={(v) => setData((d) => ({ ...d, [field.fieldKey]: v }))}
                        />
                      </Box>
                    </Grid.Col>
                  );
                })}
              </Grid>
            </Grid.Col>
          )}
        </Grid>
        <Group justify="flex-end" mt="md" gap="xs">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button
            type="submit"
            disabled={!edgeType || !otherRecordId}
            loading={create.isPending}
          >
            Create link
          </Button>
        </Group>
      </Box>
    </Modal>
  );
}

function labelFor(edgeType: EdgeType | undefined, dir: "forward" | "inverse") {
  if (!edgeType) return "→";
  if (dir === "forward") return edgeType.name;
  return edgeType.inverseName ?? `← ${edgeType.name}`;
}

function SimpleFieldInput({
  fieldKey,
  dataType,
  value,
  onChange
}: {
  fieldKey: string;
  dataType: string;
  value: unknown;
  onChange: (v: unknown) => void;
}) {
  switch (dataType) {
    case "boolean":
      return (
        <Switch
          id={`edge-${fieldKey}`}
          checked={Boolean(value)}
          onChange={(e) => onChange(e.currentTarget.checked)}
          label={Boolean(value) ? "Yes" : "No"}
        />
      );
    case "number":
      return (
        <TextInput
          type="number"
          value={value === null || value === undefined ? "" : String(value)}
          onChange={(e) =>
            onChange(e.currentTarget.value === "" ? null : Number(e.currentTarget.value))
          }
        />
      );
    case "date":
      return (
        <TextInput
          type="date"
          value={(value as string | null) ?? ""}
          onChange={(e) => onChange(e.currentTarget.value || null)}
        />
      );
    default:
      return (
        <TextInput
          value={(value as string | null) ?? ""}
          onChange={(e) => onChange(e.currentTarget.value)}
        />
      );
  }
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { message?: string } } }).response;
    return response?.data?.message ?? error.message;
  }
  return String(error);
}
