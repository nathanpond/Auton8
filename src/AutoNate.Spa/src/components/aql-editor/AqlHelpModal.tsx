import {
  Accordion,
  Alert,
  Badge,
  Code,
  Divider,
  Group,
  List,
  Loader,
  Modal,
  Stack,
  Table,
  Text,
  Title
} from "@mantine/core";
import { useQuery } from "@tanstack/react-query";
import { fetchAqlSchema, type AqlEntityMeta, type AqlSchema } from "@/api/aqlSchema";

export type AqlHelpModalProps = {
  opened: boolean;
  onClose: () => void;
};

export default function AqlHelpModal({ opened, onClose }: AqlHelpModalProps) {
  const schemaQuery = useQuery({
    queryKey: ["aql", "schema"],
    queryFn: ({ signal }) => fetchAqlSchema(signal),
    staleTime: 5 * 60_000,
    gcTime: 30 * 60_000,
    enabled: opened
  });

  return (
    <Modal
      opened={opened}
      onClose={onClose}
      title="AQL reference"
      size="xl"
      centered
      scrollAreaComponent={undefined}
    >
      <Stack gap="md">
        <GrammarSection />
        <ExamplesSection />
        {schemaQuery.isLoading && (
          <Group gap="xs">
            <Loader size="sm" />
            <Text size="sm" c="dimmed">Loading schema…</Text>
          </Group>
        )}
        {schemaQuery.isError && (
          <Alert color="red" variant="light">
            Couldn't load the schema reference. The grammar and examples above
            are still accurate; the entity list will appear once the network
            request succeeds.
          </Alert>
        )}
        {schemaQuery.data && (
          <>
            <OperatorsSection schema={schemaQuery.data} />
            <BuiltinFunctionsSection schema={schemaQuery.data} />
            <EntitiesSection schema={schemaQuery.data} />
          </>
        )}
      </Stack>
    </Modal>
  );
}

// ---- Static sections ------------------------------------------------

function GrammarSection() {
  return (
    <Stack gap="xs">
      <Title order={4}>Grammar</Title>
      <Text size="sm">
        A query is a sequence of clauses in a fixed order. Every clause is
        optional; <Code>FROM</Code> defaults to <Code>Records</Code> when
        omitted.
      </Text>
      <Code block>
{`[FROM <entity>]
[WHERE <expression>]
[ORDER BY <field> [ASC|DESC], ...]
[COLUMNS(<field-or-function>, ...)]
[GROUP(<field>, ...)]
[LIMIT <n>]`}
      </Code>
      <Text size="sm" c="dimmed">
        Keywords are case-insensitive. Strings use double or single quotes
        with <Code>\n</Code>, <Code>\t</Code>, <Code>\"</Code>, <Code>\'</Code>{" "}
        escapes. Relative dates use <Code>-2w</Code>, <Code>+1d</Code>,{" "}
        <Code>-3m</Code> with units <Code>h</Code>/<Code>d</Code>/<Code>w</Code>
        /<Code>m</Code>/<Code>y</Code>. Boolean values are <Code>True</Code>{" "}
        and <Code>False</Code>; the null sentinel is <Code>NULL</Code>.
      </Text>
    </Stack>
  );
}

function ExamplesSection() {
  const examples: Array<{ query: string; description: string }> = [
    {
      query: 'FROM Records WHERE RecordType = "Car"',
      description: "All cars."
    },
    {
      query: 'FROM Records WHERE CreatedDate > -2w ORDER BY CreatedDate DESC',
      description: "Records created in the past two weeks, newest first."
    },
    {
      query: 'FROM Flows WHERE Status = "In-progress" COLUMNS(FlowName, CURRENTSTEP(Name), CURRENTSTEP(Assignee))',
      description: "In-progress flows with their current step and assignee."
    },
    {
      query: 'FROM Flows GROUP(Status) COLUMNS(Status, COUNT())',
      description: "Flow counts by status."
    },
    {
      query: 'FROM Workflows WHERE USESNODE("userTask") COLUMNS(ModelName, NUMNODES(), LASTEXECUTED())',
      description: "Workflows that contain a user-task node."
    }
  ];
  return (
    <Stack gap="xs">
      <Title order={4}>Examples</Title>
      <Stack gap={6}>
        {examples.map((e) => (
          <Stack key={e.query} gap={2}>
            <Code block>{e.query}</Code>
            <Text size="xs" c="dimmed">{e.description}</Text>
          </Stack>
        ))}
      </Stack>
    </Stack>
  );
}

// ---- Schema-driven sections ----------------------------------------

function OperatorsSection({ schema }: { schema: AqlSchema }) {
  const types = Object.keys(schema.operatorsByDataType);
  return (
    <Stack gap="xs">
      <Title order={4}>Operators</Title>
      <Text size="sm" c="dimmed">
        Allowed comparison operators depend on the column's data type.
      </Text>
      <Table withTableBorder withColumnBorders>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>Data type</Table.Th>
            <Table.Th>Operators</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {types.map((t) => (
            <Table.Tr key={t}>
              <Table.Td><Code>{t}</Code></Table.Td>
              <Table.Td>
                <Group gap={6}>
                  {schema.operatorsByDataType[t as keyof typeof schema.operatorsByDataType].map((op) => (
                    <Code key={op}>{op}</Code>
                  ))}
                </Group>
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
      <Text size="xs" c="dimmed">
        <Code>~</Code> is a case-insensitive substring match. NULL comparisons
        use <Code>= NULL</Code> and <Code>!= NULL</Code>.
      </Text>
    </Stack>
  );
}

function BuiltinFunctionsSection({ schema }: { schema: AqlSchema }) {
  return (
    <Stack gap="xs">
      <Title order={4}>Built-in functions</Title>
      <Text size="sm" c="dimmed">
        Available in every entity. <Code>CONTAINS</Code>/<Code>IN</Code>/
        <Code>BETWEEN</Code> live in the WHERE clause; aggregates live in
        COLUMNS/ORDER&nbsp;BY and require a GROUP clause.
      </Text>
      <Stack gap={4}>
        <Text size="sm" fw={500}>WHERE-clause functions</Text>
        <Group gap={6}>
          {schema.whereFunctions.map((fn) => (
            <Code key={fn}>{fn}(…)</Code>
          ))}
        </Group>
      </Stack>
      <Stack gap={4}>
        <Text size="sm" fw={500}>Aggregates</Text>
        <Group gap={6}>
          {schema.globalAggregates.map((a) => (
            <Code key={a.name}>{a.name}{a.requiresArgument ? "(field)" : "()"}</Code>
          ))}
        </Group>
      </Stack>
    </Stack>
  );
}

function EntitiesSection({ schema }: { schema: AqlSchema }) {
  return (
    <Stack gap="xs">
      <Title order={4}>Entities</Title>
      <Text size="sm" c="dimmed">
        Each entity exposes its own columns and functions. Records merges
        type-specific fields when a <Code>RecordType = &quot;…&quot;</Code>{" "}
        filter is present.
      </Text>
      <Accordion multiple variant="separated">
        {schema.entities.map((entity) => (
          <Accordion.Item key={entity.name} value={entity.name}>
            <Accordion.Control>
              <Group gap="xs">
                <Text fw={500}>{entity.name}</Text>
                {entity.hasDynamicFields && (
                  <Badge size="xs" variant="light" color="blue">
                    dynamic fields
                  </Badge>
                )}
              </Group>
            </Accordion.Control>
            <Accordion.Panel>
              <EntityDetail entity={entity} />
            </Accordion.Panel>
          </Accordion.Item>
        ))}
      </Accordion>
    </Stack>
  );
}

function EntityDetail({ entity }: { entity: AqlEntityMeta }) {
  return (
    <Stack gap="sm">
      <Stack gap={4}>
        <Text size="sm" fw={500}>Columns</Text>
        <Table>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Name</Table.Th>
              <Table.Th>Type</Table.Th>
              <Table.Th>Aggregable</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {entity.staticColumns.map((c) => (
              <Table.Tr key={c.name}>
                <Table.Td><Code>{c.name}</Code></Table.Td>
                <Table.Td>{c.dataType}</Table.Td>
                <Table.Td>{c.isAggregable ? "yes" : ""}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Stack>

      {entity.allowedWhereFunctions.length > 0 && (
        <Stack gap={4}>
          <Divider />
          <Text size="sm" fw={500}>WHERE-clause functions</Text>
          <Group gap={6}>
            {entity.allowedWhereFunctions.map((fn) => (
              <Code key={fn}>{fn}(…)</Code>
            ))}
          </Group>
        </Stack>
      )}

      {entity.rowFunctions.length > 0 && (
        <Stack gap={4}>
          <Divider />
          <Text size="sm" fw={500}>Row functions (use in COLUMNS / ORDER BY)</Text>
          <List size="sm" spacing={2}>
            {entity.rowFunctions.map((fn) => {
              // Backend versions before the closed-set arg vocabulary
              // change omit `arguments` entirely; tolerate undefined so a
              // stale cached schema doesn't white-screen the modal.
              const args = fn.arguments ?? [];
              return (
                <List.Item key={fn.name}>
                  <Code>{fn.name}{fn.acceptsArgument ? "(arg)" : "()"}</Code>{" "}
                  <Text component="span" size="xs" c="dimmed">→ {fn.dataType}</Text>
                  {args.length > 0 && (
                    <Stack gap={2} mt={2} ml="md">
                      <Text size="xs" c="dimmed">Allowed arguments:</Text>
                      <Group gap={4}>
                        {args.map((arg) => (
                          <Code key={arg}>{arg}</Code>
                        ))}
                      </Group>
                    </Stack>
                  )}
                </List.Item>
              );
            })}
          </List>
        </Stack>
      )}

      {entity.hasDynamicFields && (
        <Stack gap={4}>
          <Divider />
          <Text size="sm" c="dimmed">
            Add <Code>RecordType = &quot;Type&quot;</Code> in WHERE to expose
            that type's fields in COLUMNS and ORDER&nbsp;BY.
          </Text>
        </Stack>
      )}
    </Stack>
  );
}
