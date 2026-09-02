import { ReactNode } from "react";
import { Link } from "react-router-dom";
import {
  Box,
  Card,
  Group,
  SimpleGrid,
  Stack,
  Text,
  ThemeIcon,
  Title,
  UnstyledButton
} from "@mantine/core";
import MyTasksPanel from "./MyTasksPanel";
import TeamTasksPanel from "./TeamTasksPanel";
import WatchedRecordsPanel from "./WatchedRecordsPanel";

export default function Home() {
  return (
    <Box py="md">
      <Stack gap="lg">
        <div>
          <Title order={1} mb={4}>
            Home
          </Title>
          <Text c="dimmed" size="sm">
            Your work across records, documents, and automations. Jump to an area below, or pick
            up the tasks and records waiting on you.
          </Text>
        </div>

        <SimpleGrid cols={{ base: 1, sm: 2, md: 3 }} spacing="md">
          <QuickLink to="/workflow" icon="fa-diagram-project" iconColor="blue" title="Open Workflow Studio">
            Create new process drafts, iterate on BPMN diagrams, and deploy directly to Flowable.
          </QuickLink>
          <QuickLink
            to="/workflow-executions"
            icon="fa-list-check"
            iconColor="teal"
            title="Review Executions"
          >
            Track the newest runs, inspect their current step, and drill into full diagram state.
          </QuickLink>
          <QuickLink to="/record-types" icon="fa-table-list" iconColor="grape" title="Browse Records">
            Define record types and their fields, then search and edit the records built on them.
          </QuickLink>
          <QuickLink to="/documents" icon="fa-file-word" iconColor="indigo" title="Edit Documents">
            Draft documents with tracked changes and comments, bound to live record data.
          </QuickLink>
          <QuickLink to="/bus-watcher" icon="fa-tower-broadcast" iconColor="orange" title="Monitor Bus Traffic">
            Watch the app&apos;s workflow event stream and validate published payloads as they arrive.
          </QuickLink>
        </SimpleGrid>

        <Stack gap="md">
          <MyTasksPanel />
          <TeamTasksPanel />
          <WatchedRecordsPanel />
        </Stack>
      </Stack>
    </Box>
  );
}

type LinkProps = {
  to: string;
  icon: string;
  iconColor: string;
  title: string;
  children: ReactNode;
};

function QuickLink({ to, icon, iconColor, title, children }: LinkProps) {
  return (
    <UnstyledButton component={Link} to={to} style={{ display: "block", height: "100%" }}>
      <Card
        padding="lg"
        radius="md"
        withBorder
        h="100%"
        style={{ transition: "transform 120ms ease, box-shadow 120ms ease" }}
        className="hover:shadow-md"
      >
        <Group gap="sm" mb="xs">
          <ThemeIcon variant="light" color={iconColor} size="lg" radius="md">
            <i className={`fa ${icon}`} />
          </ThemeIcon>
          <Text fw={600}>{title}</Text>
        </Group>
        <Text size="sm" c="dimmed">
          {children}
        </Text>
      </Card>
    </UnstyledButton>
  );
}
