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
            Automation Dashboard
          </Title>
          <Text c="dimmed" size="sm">
            Build, save, and monitor BPMN automations. Use the workflow tools below to model,
            inspect, and trace runs.
          </Text>
        </div>

        <SimpleGrid cols={{ base: 1, sm: 2, lg: 4 }} spacing="md">
          <StatCard
            color="indigo"
            icon="fa-diagram-project"
            title="WORKFLOW DESIGN"
            big="Studio Ready"
            copy="Build, save, deploy, and launch BPMN flows"
          />
          <StatCard
            color="teal"
            icon="fa-list-check"
            title="EXECUTION TRACE"
            big="Live History"
            copy="Inspect active and completed workflow runs"
          />
          <StatCard
            // Not Mantine's orange scale: even orange.9 (#d9480f) is 4.30:1
            // against white, just under the 4.5:1 these labels need. This is
            // the same hue, dark enough to pass at 6.26:1.
            color="#a83e00"
            icon="fa-tower-broadcast"
            title="EVENT STREAM"
            big="Bus Watcher"
            copy="Observe integration traffic in real time"
          />
          <StatCard
            color="dark"
            icon="fa-layer-group"
            title="THEME STATUS"
            big="Mantine"
            copy="Migrated dashboard, BPMN modeler, and shell"
          />
        </SimpleGrid>

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

type StatProps = {
  color: string;
  icon: string;
  title: string;
  big: string;
  copy: string;
};

function StatCard({ color, icon, title, big, copy }: StatProps) {
  // A Mantine palette key gets the .9 shade — the lighter fills measured
  // below 4.5:1 against the white text. A raw hex is used as-is, for the one
  // hue whose darkest Mantine shade still does not clear the threshold.
  const fill = color.startsWith("#") ? color : `${color}.9`;
  return (
    // .9 rather than .7: white text on the lighter fills measured below
    // 4.5:1 on every card (axe flagged all four). The darker shade keeps the
    // colour identity and clears the threshold.
    <Card padding="lg" radius="md" bg={fill} c="white" style={{ overflow: "hidden" }}>
      <Group align="flex-start" wrap="nowrap" gap="md">
        <ThemeIcon variant="white" color={fill} size={48} radius="md">
          <i className={`fa ${icon} fa-fw`} style={{ fontSize: 24 }} />
        </ThemeIcon>
        <Stack gap={4} style={{ minWidth: 0 }}>
          {/* No opacity: white at 0.85 over these card fills measures below
              4.5:1. Size and weight already carry the hierarchy. */}
          <Text size="xs" fw={700} style={{ letterSpacing: 0.5 }}>
            {title}
          </Text>
          <Text fw={700} size="xl" lh={1.2}>
            {big}
          </Text>
          <Text size="sm">
            {copy}
          </Text>
        </Stack>
      </Group>
    </Card>
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
