import { useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Anchor,
  Badge,
  Box,
  Button,
  Group,
  Modal,
  Stack,
  Text,
  TextInput,
  Tooltip
} from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import { DataTable, type DataTableColumn } from "@/components/data-table/DataTable";
import {
  createDecisionTable,
  deleteDecisionTable,
  listDecisionTables,
  type DecisionTable
} from "@/api/decisionTables";
import { toast } from "@/components/notifications/toast";
import { describeError } from "@/lib/describeError";

const COLUMN_WIDTHS = ["24%", "20%", "14%", "16%", "18%", "8%"];

export const DECISION_TABLES_QUERY_KEY = ["decision-tables"] as const;

export default function DecisionTablesList() {
  const qc = useQueryClient();
  const navigate = useNavigate();
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState("");
  const [key, setKey] = useState("");

  const create = useMutation({
    mutationFn: () =>
      createDecisionTable({
        decisionKey: key.trim(),
        name: name.trim(),
        hitPolicy: "FIRST",
        // A new table starts with one of each, because a table with no input
        // matches everything and one with no output decides nothing -- both are
        // refused at save, and handing an author an empty grid that cannot be
        // saved is a worse first experience than a starting point they edit.
        inputs: [{ id: crypto.randomUUID(), label: "Input 1", name: "input1", typeRef: "string" }],
        outputs: [{ id: crypto.randomUUID(), label: "Output 1", name: "output1", typeRef: "string" }],
        rules: []
      }),
    onSuccess: (table) => {
      qc.invalidateQueries({ queryKey: DECISION_TABLES_QUERY_KEY });
      setCreating(false);
      setName("");
      setKey("");
      navigate(`/admin/config/decision-tables/${table.id}`);
    },
    onError: (err) => toast.error(`Could not create the decision table. ${describeError(err)}`)
  });

  const remove = useMutation({
    mutationFn: (id: string) => deleteDecisionTable(id),
    onSuccess: () => {
      toast.success("Decision table deleted.");
      qc.invalidateQueries({ queryKey: DECISION_TABLES_QUERY_KEY });
    },
    onError: (err) => toast.error(`Could not delete the decision table. ${describeError(err)}`)
  });

  const columns = useMemo<DataTableColumn<DecisionTable>[]>(
    () => [
      {
        id: "name",
        accessorKey: "name",
        header: "Name",
        cell: ({ row }) => (
          <Anchor component={Link} to={`/admin/config/decision-tables/${row.original.id}`}>
            {row.original.name}
          </Anchor>
        )
      },
      { id: "key", accessorKey: "decisionKey", header: "Key" },
      { id: "hitPolicy", accessorKey: "hitPolicy", header: "Hit policy" },
      {
        id: "rules",
        header: "Rules",
        accessorFn: (t) => t.rules.length,
        cell: ({ row }) => <Text size="sm">{row.original.rules.length}</Text>
      },
      {
        id: "status",
        header: "Status",
        accessorFn: (t) => (t.publishedVersionNumber === null ? "Never published" : "Published"),
        cell: ({ row }) =>
          row.original.publishedVersionNumber === null ? (
            <Badge color="gray" variant="light">
              Never published
            </Badge>
          ) : (
            <Group gap={6} wrap="nowrap">
              <Badge color="green" variant="light">
                v{row.original.publishedVersionNumber}
              </Badge>
              {row.original.isDraft && (
                <Tooltip label="Edited since it was last published">
                  <Badge color="yellow" variant="light">
                    Draft changes
                  </Badge>
                </Tooltip>
              )}
            </Group>
          )
      },
      {
        id: "actions",
        header: "",
        enableSorting: false,
        cell: ({ row }) => (
          <Tooltip label={`Delete ${row.original.name}`}>
            <ActionIcon
              variant="subtle"
              color="red"
              aria-label={`Delete ${row.original.name}`}
              loading={remove.isPending}
              onClick={() => {
                if (
                  window.confirm(
                    `Delete "${row.original.name}"? Its published versions go with it, and any ` +
                      `deployed process that references ${row.original.decisionKey} will stop working.`
                  )
                ) {
                  remove.mutate(row.original.id);
                }
              }}
            >
              <i className="fa fa-trash" />
            </ActionIcon>
          </Tooltip>
        )
      }
    ],
    [remove]
  );

  return (
    <Box py="md">
      <PageHeader
        title="Decision Tables"
        description="Rules a workflow's business rule task can run, versioned like workflow models."
        actions={
          <Button leftSection={<i className="fa fa-plus" />} onClick={() => setCreating(true)}>
            New decision table
          </Button>
        }
      />

      <DataTable<DecisionTable>
        queryKey={DECISION_TABLES_QUERY_KEY}
        loadAll={() => listDecisionTables()}
        rowKey={(t) => t.id}
        columns={columns}
        columnWidths={COLUMN_WIDTHS}
        searchEnabled
        searchPlaceholder="Search decision tables"
      />

      <Modal opened={creating} onClose={() => setCreating(false)} title="New decision table">
        <Stack gap="sm">
          <TextInput
            label="Name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Invoice routing"
          />
          <TextInput
            label="Key"
            value={key}
            onChange={(e) => setKey(e.target.value)}
            placeholder="invoiceRouting"
            description="A business rule task references the table by this. Letters, digits and underscores."
          />
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setCreating(false)}>
              Cancel
            </Button>
            <Button
              loading={create.isPending}
              disabled={name.trim() === "" || key.trim() === ""}
              onClick={() => create.mutate()}
            >
              Create
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Box>
  );
}
