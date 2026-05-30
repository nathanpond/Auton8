import { useMemo } from "react";
import { Link } from "react-router-dom";
import { Anchor, Badge, Button } from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import {
  DataTable,
  type DataTableColumn,
  type DataTablePageRequest,
  type DataTablePageResult
} from "@/components/data-table/DataTable";
import { ProjectDto, listProjectsPage } from "@/api/content";

const COLUMN_WIDTHS = ["28%", "auto", "12%", "20%"];
const QUERY_KEY = ["documents", "home", "projects"] as const;

// Landing page for the Documents feature. Pure project picker — clicking a
// project lands the user on /documents/p/:projectId, where the folder tree +
// breadcrumb + folder-view live. Mirrors AllProjects.tsx's data-table shape
// so the SPA reads as one consistent product surface.

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

export default function DocumentsHomePage() {
  const columns = useMemo<DataTableColumn<ProjectDto>[]>(
    () => [
      {
        id: "name",
        accessorKey: "name",
        header: "Project",
        cell: ({ row }) => (
          <Anchor component={Link} to={`/documents/p/${row.original.id}`} fw={600}>
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
      }
    ],
    []
  );

  const loadPage = async (req: DataTablePageRequest): Promise<DataTablePageResult<ProjectDto>> => {
    const result = await listProjectsPage({
      page: req.page,
      pageSize: req.pageSize,
      search: req.search || undefined
    });
    return { items: result.items, totalCount: result.totalCount };
  };

  return (
    <div>
      <PageHeader
        title="Documents"
        description="Pick a project to browse its folders and documents."
        actions={
          <Button
            component={Link}
            to="/documents/templates"
            variant="light"
            leftSection={<i className="fa fa-copy" aria-hidden />}
          >
            Template gallery
          </Button>
        }
      />
      <DataTable<ProjectDto>
        columns={columns}
        loadPage={loadPage}
        loadAll={loadAllProjects}
        queryKey={QUERY_KEY}
        rowKey={(p) => p.id}
        columnWidths={COLUMN_WIDTHS}
        searchEnabled
      />
    </div>
  );
}

function formatDateTime(iso: string): string {
  if (!iso) return "—";
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return iso;
  }
}
