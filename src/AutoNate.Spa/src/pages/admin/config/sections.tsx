import { Box, Group, Paper, Stack, Text, Title } from "@mantine/core";
import SiteSettingsForm from "./SiteSettingsForm";

type StubProps = {
  title: string;
  blurb: string;
};

function Stub({ title, blurb }: StubProps) {
  return (
    <Box py="md">
      <Stack gap="lg">
        <Stack gap={4}>
          <Title order={1}>{title}</Title>
          <Text size="sm" c="dimmed">
            {blurb}
          </Text>
        </Stack>
        <Paper withBorder radius="md" p="md">
          <Group gap="xs" c="dimmed">
            <i className="fa fa-screwdriver-wrench" />
            <Text size="sm">This section is a stub. Functionality coming soon.</Text>
          </Group>
        </Paper>
      </Stack>
    </Box>
  );
}

export function ConfigIndex() {
  return (
    <Box py="md">
      <Stack gap="lg">
        <Stack gap={4}>
          <Title order={1}>Site Configuration</Title>
          <Text size="sm" c="dimmed">
            Manage sitewide settings and security from a single place. Choose a section from the
            navigation on the left to get started.
          </Text>
        </Stack>
        <Paper withBorder radius="md" p="md">
          <Text size="sm" c="dimmed">
            Select a category on the left to begin.
          </Text>
        </Paper>
      </Stack>
    </Box>
  );
}

export function SitewideGeneral() {
  return (
    <SiteSettingsForm
      group="general"
      title="General"
      blurb="Core sitewide settings and feature flags. Changes apply across the application."
    />
  );
}

export function SitewideFeatures() {
  return (
    <SiteSettingsForm
      group="features"
      title="Features"
      blurb="Toggle optional features and modules across the application."
    />
  );
}

export function SitewideChatbotSettings() {
  return (
    <SiteSettingsForm
      group="chatbot"
      title="Chatbot Settings"
      blurb="Configure agent capabilities. Changes apply on the next message."
    />
  );
}

export { ExternalConnectionsPage as SitewideExternalConnections } from "./external-connections/ExternalConnectionsPage";

export function SecurityManageUsers() {
  return (
    <Stub
      title="Manage Users"
      blurb="Create, update, and disable user accounts."
    />
  );
}

export function SecurityManageGroups() {
  return (
    <Stub
      title="Manage Groups"
      blurb="Organize users into groups for easier permission management."
    />
  );
}

export function SecurityManageRoles() {
  return (
    <Stub
      title="Manage Roles"
      blurb="Define named roles you can attach permissions to."
    />
  );
}

export function SecuritySetPermissions() {
  return (
    <Stub
      title="Set Permissions"
      blurb="Assign permissions to roles and configure their scopes."
    />
  );
}

export function SecurityPermissionChecker() {
  return (
    <Stub
      title="Permission Checker"
      blurb="Inspect why a user does or does not have a given permission."
    />
  );
}

export function FormsFormMappings() {
  return (
    <Stub
      title="Form Mappings"
      blurb="Map forms to record types and fields."
    />
  );
}
