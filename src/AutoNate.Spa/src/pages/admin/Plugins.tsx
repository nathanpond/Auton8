import { useMemo, useState } from "react";
import { ColumnDef } from "@tanstack/react-table";
import { Alert, Badge, Button, Code, Group, Text } from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import {
  useDeletePlugin,
  useDisablePlugin,
  useEnablePlugin,
} from "@/hooks/usePlugins";
import { Plugin, listPlugins } from "@/api/plugins";
import { DataTable } from "@/components/data-table/DataTable";
import UploadPluginModal from "./UploadPluginModal";

const COLUMN_WIDTHS = ["32%", "14%", "14%", "22%", "18%"];

export default function Plugins() {
  const enable = useEnablePlugin();
  const disable = useDisablePlugin();
  const remove = useDeletePlugin();
  const [showUpload, setShowUpload] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const runAction = async (id: string, action: () => Promise<unknown>) => {
    setError(null);
    setBusyId(id);
    try {
      await action();
    } catch (err) {
      setError(describeError(err));
    } finally {
      setBusyId(null);
    }
  };

  const columns = useMemo<ColumnDef<Plugin>[]>(
    () => [
      {
        id: "name",
        accessorKey: "name",
        header: "Name",
        cell: ({ row }) => (
          <>
            <strong>{row.original.name}</strong>
            {row.original.lastError && (
              <Text size="sm" c="red" title={row.original.lastError} component="div">
                Last error:{" "}
                {row.original.lastError.length > 80
                  ? row.original.lastError.slice(0, 80) + "…"
                  : row.original.lastError}
              </Text>
            )}
          </>
        )
      },
      {
        id: "version",
        accessorKey: "version",
        header: "Version",
        cell: ({ row }) => <Code>{row.original.version}</Code>
      },
      {
        id: "status",
        accessorKey: "status",
        header: "Status",
        cell: ({ row }) => renderStatusBadge(row.original)
      },
      {
        id: "uploadedAt",
        header: "Uploaded",
        accessorFn: (p) => p.uploadedAt,
        cell: ({ row }) => (
          <Text size="sm" c="dimmed" component="span">
            {new Date(row.original.uploadedAt).toLocaleString()}
          </Text>
        )
      },
      {
        id: "actions",
        header: "Actions",
        enableSorting: false,
        enableGlobalFilter: false,
        cell: ({ row }) => {
          const p = row.original;
          return (
            <Group gap="xs">
              {p.status === "Disabled" && (
                <Button
                  size="xs"
                  color="green"
                  disabled={busyId === p.id}
                  onClick={(e) => {
                    e.stopPropagation();
                    void runAction(p.id, () => enable.mutateAsync(p.id));
                  }}
                >
                  Enable
                </Button>
              )}
              {p.status === "Enabled" && (
                <Button
                  size="xs"
                  color="yellow"
                  disabled={busyId === p.id}
                  onClick={(e) => {
                    e.stopPropagation();
                    void runAction(p.id, () => disable.mutateAsync(p.id));
                  }}
                >
                  Disable
                </Button>
              )}
              <Button
                size="xs"
                variant="outline"
                color="red"
                disabled={busyId === p.id}
                onClick={(e) => {
                  e.stopPropagation();
                  if (
                    confirm(
                      `Delete plugin '${p.name}'? This removes the row and all uploaded files.`
                    )
                  ) {
                    void runAction(p.id, () => remove.mutateAsync(p.id));
                  }
                }}
              >
                Delete
              </Button>
            </Group>
          );
        }
      }
    ],
    [busyId, enable, disable, remove]
  );

  return (
    <>
      <PageHeader
        title="Plugins"
        description="Runtime-loaded plugins that extend AutoNate via hooks. Upload, enable, disable, or delete here."
      />

      {error && (
        <Alert color="red" variant="light" mb="md">
          {error}
        </Alert>
      )}

      <DataTable<Plugin>
        mode="client"
        loadAll={() => listPlugins()}
        queryKey={["admin", "plugins"]}
        columns={columns}
        rowKey={(p) => p.id}
        columnWidths={COLUMN_WIDTHS}
        initialSort={[{ id: "name", desc: false }]}
        searchPlaceholder="Search plugins…"
        emptyMessage="No plugins installed yet."
        loadingMessage="Loading plugins…"
        globalFilterFn={(p, search) => {
          const needle = search.toLowerCase();
          return `${p.name} ${p.version}`.toLowerCase().includes(needle);
        }}
        toolbarRight={
          <Button
            leftSection={<i className="fa fa-plus" />}
            onClick={() => setShowUpload(true)}
          >
            Upload plugin
          </Button>
        }
      />

      {showUpload && <UploadPluginModal onClose={() => setShowUpload(false)} />}
    </>
  );
}

function renderStatusBadge(p: Plugin) {
  if (p.lastError) {
    return (
      <Badge color="red" variant="filled">
        Error
      </Badge>
    );
  }
  switch (p.status) {
    case "Enabled":
      return (
        <Badge color="green" variant="filled">
          Enabled
        </Badge>
      );
    case "Disabled":
      return (
        <Badge color="gray" variant="filled">
          Disabled
        </Badge>
      );
    case "DeletedPending":
      return (
        <Badge color="yellow" variant="filled">
          Deleted (pending)
        </Badge>
      );
  }
}

function describeError(err: unknown): string {
  if (typeof err === "object" && err && "response" in err) {
    const resp = (err as { response?: { data?: { error?: string; message?: string } } }).response;
    return resp?.data?.error ?? resp?.data?.message ?? String(err);
  }
  return err instanceof Error ? err.message : String(err);
}
