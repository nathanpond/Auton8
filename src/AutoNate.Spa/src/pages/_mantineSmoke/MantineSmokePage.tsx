import { useState } from "react";
import {
  Box,
  Button,
  Container,
  Group,
  Modal,
  Stack,
  TextInput,
  Title,
  Text,
  Paper,
  Divider
} from "@mantine/core";
import { useForm } from "@mantine/form";
import { modals } from "@mantine/modals";
import { notifications } from "@mantine/notifications";
import { DataTable, type DataTableSortStatus } from "mantine-datatable";
import { DataTable as RichDataTable } from "@/components/data-table/DataTable";
import type { ColumnDef } from "@tanstack/react-table";
import { useMemo } from "react";

type SampleRow = {
  id: number;
  name: string;
  status: "active" | "paused" | "errored";
  count: number;
};

const SAMPLE_ROWS: SampleRow[] = [
  { id: 1, name: "Alpha", status: "active", count: 42 },
  { id: 2, name: "Bravo", status: "paused", count: 17 },
  { id: 3, name: "Charlie", status: "active", count: 99 },
  { id: 4, name: "Delta", status: "errored", count: 3 },
  { id: 5, name: "Echo", status: "active", count: 256 }
];

export default function MantineSmokePage() {
  const [modalOpen, setModalOpen] = useState(false);
  const [sortStatus, setSortStatus] = useState<DataTableSortStatus<SampleRow>>({
    columnAccessor: "name",
    direction: "asc"
  });

  const sorted = [...SAMPLE_ROWS].sort((a, b) => {
    const dir = sortStatus.direction === "asc" ? 1 : -1;
    const key = sortStatus.columnAccessor as keyof SampleRow;
    const av = a[key];
    const bv = b[key];
    if (typeof av === "number" && typeof bv === "number") return (av - bv) * dir;
    return String(av).localeCompare(String(bv)) * dir;
  });

  const form = useForm({
    initialValues: { email: "", name: "", count: "" },
    validate: {
      email: (v) => (/^\S+@\S+$/.test(v) ? null : "Invalid email"),
      name: (v) => (v.trim().length >= 2 ? null : "Name must be at least 2 chars"),
      count: (v) => (Number.isInteger(Number(v)) ? null : "Must be an integer")
    }
  });

  const toggleColorScheme = () => {
    const html = document.documentElement;
    const next = html.getAttribute("data-mantine-color-scheme") === "dark" ? "light" : "dark";
    html.setAttribute("data-mantine-color-scheme", next);
  };

  return (
    <Container size="md" py="xl">
      <Stack gap="lg">
        <Title order={1}>Mantine Smoke Test</Title>
        <Text c="dimmed">
          Validates MantineProvider, ModalsProvider, Notifications, useForm, DataTable, and the
          SiteAppearance theme bridge. This page is for Phase I sign-off only.
        </Text>

        <Paper p="md" withBorder>
          <Stack gap="sm">
            <Title order={3}>Buttons + theme bridge</Title>
            <Group>
              <Button>Default (brand)</Button>
              <Button variant="light">Light</Button>
              <Button variant="outline">Outline</Button>
              <Button color="red">Red</Button>
              <Button onClick={toggleColorScheme} variant="default">
                Toggle color scheme
              </Button>
            </Group>
            <Text size="sm" c="dimmed">
              The first button uses the SiteAppearance primary accent color via the theme bridge.
            </Text>
          </Stack>
        </Paper>

        <Paper p="md" withBorder>
          <Stack gap="sm">
            <Title order={3}>Modals + notifications</Title>
            <Group>
              <Button onClick={() => setModalOpen(true)}>Open Modal</Button>
              <Button
                variant="light"
                onClick={() => {
                  modals.openConfirmModal({
                    title: "Confirm action",
                    children: <Text size="sm">Are you sure?</Text>,
                    labels: { confirm: "Confirm", cancel: "Cancel" },
                    onConfirm: () => {
                      notifications.show({
                        title: "Confirmed",
                        message: "openConfirmModal + notifications.show work.",
                        color: "green"
                      });
                    }
                  });
                }}
              >
                Confirm Modal
              </Button>
              <Button
                variant="light"
                onClick={() =>
                  notifications.show({
                    title: "Hello",
                    message: "Mantine notifications are mounted.",
                    color: "blue"
                  })
                }
              >
                Notify
              </Button>
            </Group>
          </Stack>
        </Paper>

        <Paper p="md" withBorder>
          <Stack gap="sm">
            <Title order={3}>Form (@mantine/form)</Title>
            <Box
              component="form"
              onSubmit={form.onSubmit((values) => {
                notifications.show({
                  title: "Form submitted",
                  message: JSON.stringify(values),
                  color: "green"
                });
              })}
            >
              <Stack gap="sm">
                <TextInput label="Email" placeholder="you@example.com" {...form.getInputProps("email")} />
                <TextInput label="Name" placeholder="Ada Lovelace" {...form.getInputProps("name")} />
                <TextInput label="Count" placeholder="0" {...form.getInputProps("count")} />
                <Group justify="flex-end">
                  <Button type="submit">Submit</Button>
                </Group>
              </Stack>
            </Box>
          </Stack>
        </Paper>

        <Paper p="md" withBorder>
          <Stack gap="sm">
            <Title order={3}>Rich DataTable wrapper</Title>
            <Text size="sm" c="dimmed">
              Static data through the @/components/data-table/DataTable wrapper to
              reproduce / verify the home-panel rendering path without auth.
            </Text>
            <RichSmokeTable />
            <RichSmokeMixedTable />
          </Stack>
        </Paper>

        <Paper p="md" withBorder>
          <Stack gap="sm">
            <Title order={3}>DataTable (mantine-datatable)</Title>
            <DataTable
              withTableBorder
              borderRadius="sm"
              striped
              highlightOnHover
              records={sorted}
              columns={[
                { accessor: "id", sortable: true, width: 60 },
                { accessor: "name", sortable: true },
                { accessor: "status", sortable: true },
                { accessor: "count", sortable: true, textAlign: "right" }
              ]}
              sortStatus={sortStatus}
              onSortStatusChange={setSortStatus}
            />
          </Stack>
        </Paper>

        <Divider />
        <Text size="xs" c="dimmed">
          If everything above renders cleanly with no console errors, Phase I is functioning.
        </Text>
      </Stack>

      <Modal opened={modalOpen} onClose={() => setModalOpen(false)} title="Hello from Mantine">
        <Text>This Modal is rendered by @mantine/core via MantineProvider.</Text>
      </Modal>
    </Container>
  );
}

function RichSmokeTable() {
  const columns = useMemo<ColumnDef<SampleRow>[]>(
    () => [
      { id: "id", accessorKey: "id", header: "ID" },
      { id: "name", accessorKey: "name", header: "Name" },
      { id: "status", accessorKey: "status", header: "Status" },
      { id: "count", accessorKey: "count", header: "Count" }
    ],
    []
  );
  const loadAll = useMemo(() => async () => SAMPLE_ROWS, []);
  return (
    <RichDataTable<SampleRow>
      queryKey={["__mantine_smoke", "rich-table"]}
      mode="client"
      loadAll={loadAll}
      columns={columns}
      columnWidths={["12%", "30%", "30%", "28%"]}
      rowKey={(r) => String(r.id)}
      searchPlaceholder="Search rows…"
      emptyMessage="No rows."
      initialSort={[{ id: "name", desc: false }]}
    />
  );
}

// Mirror of MyTasksPanel's column shape — accessorFn, custom cell renderers,
// non-sortable "actions" column with no accessor — so we can reproduce any
// rendering bug here without authentication.
type RichTaskRow =
  | { kind: "record"; id: string; sortKey: number; key: string; name: string; status: string }
  | { kind: "workflow"; id: string; sortKey: number; processName: string; activeNode: string };

const RICH_ROWS: RichTaskRow[] = [
  { kind: "record", id: "record:1", sortKey: 100, key: "ACME-1", name: "Acme widget", status: "active" },
  { kind: "workflow", id: "workflow:wf-1", sortKey: 90, processName: "Onboarding", activeNode: "Review" },
  { kind: "record", id: "record:2", sortKey: 80, key: "ACME-2", name: "Beta widget", status: "paused" },
  { kind: "workflow", id: "workflow:wf-2", sortKey: 70, processName: "Procurement", activeNode: "Approve" }
];

function RichSmokeMixedTable() {
  const columns = useMemo<ColumnDef<RichTaskRow>[]>(
    () => [
      {
        id: "name",
        accessorFn: (r: RichTaskRow) =>
          r.kind === "record" ? `${r.key} ${r.name}` : r.processName,
        header: "Name",
        cell: ({ row }) => {
          const r = row.original;
          return r.kind === "record" ? `${r.key} — ${r.name}` : r.processName;
        }
      },
      {
        id: "status",
        accessorFn: (r: RichTaskRow) => (r.kind === "record" ? r.status : r.activeNode),
        header: "Status"
      },
      {
        id: "lastUpdated",
        accessorFn: (r: RichTaskRow) => r.sortKey,
        header: "Last Updated"
      },
      {
        id: "actions",
        header: "Actions",
        enableSorting: false,
        cell: ({ row }) => {
          const r = row.original;
          if (r.kind !== "workflow") return null;
          return <Button size="xs">Open</Button>;
        }
      }
    ],
    []
  );
  const loadAll = useMemo(() => async () => RICH_ROWS, []);
  return (
    <RichDataTable<RichTaskRow>
      queryKey={["__mantine_smoke", "rich-mixed"]}
      mode="client"
      loadAll={loadAll}
      columns={columns}
      columnWidths={["38%", "20%", "20%", "22%"]}
      rowKey={(r) => r.id}
      searchPlaceholder="Search…"
      emptyMessage="No tasks."
      initialSort={[{ id: "lastUpdated", desc: true }]}
    />
  );
}
