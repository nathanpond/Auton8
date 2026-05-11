import {
  Avatar,
  Box,
  Group,
  Paper,
  SimpleGrid,
  Stack,
  Text,
  Title
} from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import { useMe } from "@/hooks/useMe";

export default function UserProfile() {
  const { data, isLoading } = useMe();

  if (isLoading) {
    return (
      <Box py="md">
        <Text c="dimmed">Loading...</Text>
      </Box>
    );
  }

  if (!data || data.authenticated !== true) {
    return (
      <Box py="md">
        <Text c="dimmed">No authenticated user.</Text>
      </Box>
    );
  }

  const displayName = `${data.firstName ?? ""} ${data.lastName ?? ""}`.trim();

  return (
    <Box py="md">
      <PageHeader
        title="User Profile"
        description="Review the local account details for the signed-in user."
      />

      <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="md">
        <Paper withBorder radius="md" p="lg">
          <Group gap="md" mb="lg">
            <Avatar size={56} radius="xl" color="gray">
              <i className="fa fa-user" style={{ fontSize: 24 }} />
            </Avatar>
            <Stack gap={2}>
              <Title order={3} m={0}>
                {displayName || data.username}
              </Title>
              <Text size="sm" c="dimmed">
                {data.username}
              </Text>
            </Stack>
          </Group>

          <Stack gap="xs">
            <Field label="First Name" value={data.firstName} />
            <Field label="Last Name" value={data.lastName} />
            <Field label="Email" value={data.email} />
            <Field label="User ID" value={data.userId} breakAll />
            {data.idpKey && <Field label="IdP Key" value={data.idpKey} breakAll />}
            <Field label="Auth Source" value={data.authSource} />
          </Stack>
        </Paper>
      </SimpleGrid>
    </Box>
  );
}

function Field({
  label,
  value,
  breakAll
}: {
  label: string;
  value: string | null | undefined;
  breakAll?: boolean;
}) {
  return (
    <Group
      gap="md"
      align="flex-start"
      wrap="nowrap"
      style={{
        display: "grid",
        gridTemplateColumns: "minmax(140px, 33%) 1fr"
      }}
    >
      <Text size="sm" fw={600}>
        {label}
      </Text>
      <Text size="sm" style={breakAll ? { wordBreak: "break-all" } : undefined}>
        {value ?? "—"}
      </Text>
    </Group>
  );
}
