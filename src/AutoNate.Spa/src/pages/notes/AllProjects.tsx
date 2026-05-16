import { useMemo } from "react";
import { Link, useNavigate } from "react-router-dom";
import { ActionIcon, Anchor, Badge, Group, Tooltip } from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import {
  DataTable,
  type DataTableColumn,
  type DataTablePageRequest
} from "@/components/data-table/DataTable";
import { ProjectDto, listProjectsPage } from "@/api/content";
import { useCreateProject } from "@/hooks/useContent";
import { NewProjectModal } from "./NewProjectModal";
import { useDisclosure } from "@mantine/hooks";

const COLUMN_WIDTHS = ["28%", "auto", "12%", "18%", "18%"];

// Pulls every project by walking the paginated endpoint at the server's max
// page size. Used as the auto-mode `loadAll` so the DataTable can drop to
// client mode (instant sort/search) for small installs.
async function loadAllProjects(signal?: AbortSignal): Promise<ProjectDto[]> {
  const PAGE = 200;
  const out: ProjectDto[] = [];
  let page = 0;
  while (true) {
    const res = await listProjectsPage({ page, pageSize: PAGE }, signal);
    out.push(...res.items);
    if (out.length >= res.totalCount) break;
    if (res.items.length === 0) break;
    page += 1;
  }
  return out;
}

export default function AllProjects() {
  const navigate = useNavigate();
  const [modalOpen, modalActions] = useDisclosure(false);
  const createProject = useCreateProject();

  const columns = useMemo<DataTableColumn<ProjectDto>[]>(
    () => [
      {
        id: "name",
        accessorKey: "name",
        header: "Name",
        cell: ({ row }) => (
          <Anchor component={Link} to={`/notes/${row.original.locator}`} fw={600}>
            {row.original.name}
          </Anchor>
        )
      },
      {
        id: "description",
        accessorKey: "description",
        header: "Description",
        meta: { wrap: true },
        cell: ({ row }) =>
          row.original.description ?? (
            <span style={{ color: "var(--mantine-color-dimmed)" }}>—</span>
          )
      },
      {
        id: "status",
        header: "Status",
        accessorFn: (p) => (p.isArchived ? "Archived" : "Active"),
        cell: ({ row }) =>
          row.original.isArchived ? (
            <Badge color="gray" variant="light">
              Archived
            </Badge>
          ) : (
            <Badge color="green" variant="light">
              Active
            </Badge>
          )
      },
      {
        id: "updatedAtUtc",
        accessorKey: "updatedAtUtc",
        header: "Updated",
        cell: ({ row }) => <span>{formatDateTime(row.original.updatedAtUtc)}</span>
      },
      {
        id: "createdAtUtc",
        accessorKey: "createdAtUtc",
        header: "Created",
        cell: ({ row }) => <span>{formatDateTime(row.original.createdAtUtc)}</span>
      }
    ],
    []
  );

  const loadPage = async (req: DataTablePageRequest) => {
    const result = await listProjectsPage({
      page: req.page,
      pageSize: req.pageSize,
      search: req.search || undefined
    });
    return { items: result.items, totalCount: result.totalCount };
  };

  return (
    <>
      <PageHeader
        title="All projects"
        description={
          <>
            Every project you can access. Open one to jump back into{" "}
            <Link to="/notes">notes</Link>.
          </>
        }
      />

      <DataTable<ProjectDto>
        mode="auto"
        autoThreshold={1000}
        loadAll={loadAllProjects}
        loadPage={loadPage}
        queryKey={["content", "projects", "all"]}
        columns={columns}
        rowKey={(p) => p.id}
        columnWidths={COLUMN_WIDTHS}
        initialSort={[{ id: "updatedAtUtc", desc: true }]}
        searchPlaceholder="Search projects…"
        emptyMessage="No projects found."
        loadingMessage="Loading projects…"
        onRowClick={(p) => navigate(`/notes/${p.locator}`)}
        getRowAriaLabel={(p) => `Open ${p.name}`}
        globalFilterFn={(p, search) => {
          const needle = search.toLowerCase();
          return (
            p.name.toLowerCase().includes(needle) ||
            (p.description ?? "").toLowerCase().includes(needle)
          );
        }}
        toolbarBeforeSearch={
          <Tooltip label="Add project" withArrow>
            <ActionIcon
              size="lg"
              variant="filled"
              aria-label="Add project"
              onClick={modalActions.open}
            >
              <i className="fa fa-plus" />
            </ActionIcon>
          </Tooltip>
        }
        toolbarRight={
          <Group gap="xs">
            <Anchor component={Link} to="/notes" size="sm">
              <i className="fa fa-arrow-left" style={{ marginRight: 6 }} />
              Back to notes
            </Anchor>
          </Group>
        }
      />

      {modalOpen && (
        <NewProjectModal
          onClose={() => {
            if (createProject.isPending) return;
            modalActions.close();
          }}
          onCreate={async (vars) => {
            const project = await createProject.mutateAsync(vars);
            modalActions.close();
            navigate(`/notes/${project.locator}`);
          }}
          submitting={createProject.isPending}
        />
      )}
    </>
  );
}

function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? (iso ?? "—") : d.toLocaleString();
}
