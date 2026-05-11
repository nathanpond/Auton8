import { Alert, Box, Code, Paper, Stack, Table, Text, Title } from "@mantine/core";
import { useEventCatalog } from "@/hooks/useEventCatalog";

export default function Events() {
  const { data, isLoading, isError, error } = useEventCatalog();

  if (isLoading) {
    return (
      <Box py="md">
        <Title order={1} mb="sm">
          Events
        </Title>
        <Text c="dimmed">Loading event catalog…</Text>
      </Box>
    );
  }

  if (isError || !data) {
    return (
      <Box py="md">
        <Title order={1} mb="sm">
          Events
        </Title>
        <Alert color="red" variant="light">
          {error instanceof Error ? error.message : "Failed to load the event catalog."}
        </Alert>
      </Box>
    );
  }

  return (
    <Box py="md">
      <Stack gap="lg">
        <Stack gap={4}>
          <Title order={1}>Events</Title>
          <Text size="sm" c="dimmed">
            Reference for every event the application publishes to its message bus. Events are
            delivered through Dapr pub/sub; a single live feed across all topics is available on
            the <strong>Bus Watcher</strong> page.
          </Text>
        </Stack>

        {data.transports.map((transport) => (
          <Paper key={transport.topic} withBorder radius="md" p="md">
            <Stack gap="sm">
              <Title order={4}>Transport — {transport.topic}</Title>
              <Stack gap="xs">
                <KeyValueRow label="Topic">
                  <Code>{transport.topic}</Code>
                  <Text size="xs" c="dimmed" mt={4}>
                    Events on this topic share the schema documented in their category below. The
                    specific kind of event is carried in the <Code>eventType</Code> field on the
                    payload.
                  </Text>
                </KeyValueRow>
                <KeyValueRow label="Broker">{transport.description}</KeyValueRow>
                <KeyValueRow label="Source">{transport.source}</KeyValueRow>
              </Stack>
            </Stack>
          </Paper>
        ))}

        <Paper withBorder radius="md" p="md">
          <Stack gap="sm">
            <Title order={4}>Common envelope</Title>
            <Text size="sm" c="dimmed">
              Every event — regardless of category — carries this envelope. Category-specific
              fields are documented per category below.
            </Text>
            <Table striped highlightOnHover withTableBorder>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th style={{ width: "20%" }}>Field</Table.Th>
                  <Table.Th style={{ width: "20%" }}>Type</Table.Th>
                  <Table.Th>Description</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {data.payloadFields.map((field) => (
                  <Table.Tr key={field.name}>
                    <Table.Td>
                      <Code>{field.name}</Code>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" c="dimmed">
                        {field.type}
                      </Text>
                    </Table.Td>
                    <Table.Td>{field.description}</Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Stack>
        </Paper>

        {data.categories.map((category) => (
          <Paper key={category.title} withBorder radius="md" p="md">
            <Stack gap="sm">
              <Title order={4}>{category.title}</Title>
              <Text size="sm" c="dimmed">
                {category.description}
              </Text>
              {category.payloadFields.length > 0 && (
                <Table striped highlightOnHover withTableBorder>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th style={{ width: "22%" }}>Field</Table.Th>
                      <Table.Th style={{ width: "20%" }}>Type</Table.Th>
                      <Table.Th>Description</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {category.payloadFields.map((field) => (
                      <Table.Tr key={field.name}>
                        <Table.Td>
                          <Code>{field.name}</Code>
                        </Table.Td>
                        <Table.Td>
                          <Text size="sm" c="dimmed">
                            {field.type}
                          </Text>
                        </Table.Td>
                        <Table.Td>{field.description}</Table.Td>
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              )}
              <Table withTableBorder>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th style={{ width: "22%" }}>Event</Table.Th>
                    <Table.Th>What it means / when it fires</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {category.events.map((evt) => (
                    <Table.Tr key={`${evt.topic}:${evt.eventType}`}>
                      <Table.Td>
                        <Code>{evt.eventType}</Code>
                      </Table.Td>
                      <Table.Td>
                        <Text fw={700}>{evt.summary}</Text>
                        <Text size="xs" c="dimmed" mt={4}>
                          {evt.firesWhen}
                        </Text>
                        {evt.payloadHighlights.length > 0 && (
                          <ul style={{ margin: "8px 0 0", paddingLeft: 20, fontSize: 13 }}>
                            {evt.payloadHighlights.map((line, idx) => (
                              <li key={idx}>{line}</li>
                            ))}
                          </ul>
                        )}
                      </Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </Stack>
          </Paper>
        ))}
      </Stack>
    </Box>
  );
}

function KeyValueRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "minmax(180px, 25%) 1fr", gap: 12 }}>
      <Text fw={600} size="sm">
        {label}
      </Text>
      <div>{children}</div>
    </div>
  );
}
