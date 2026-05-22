import { ActionIcon, Group, Menu, Select, Tooltip } from "@mantine/core";
import type { Dashboard } from "@/api/dashboards";

type Props = {
  dashboards: Dashboard[];
  selectedId: string | null;
  onSelect: (id: string) => void;
  onCreate: () => void;
  onRename: () => void;
  onDelete: () => void;
};

export function DashboardSelector({
  dashboards,
  selectedId,
  onSelect,
  onCreate,
  onRename,
  onDelete
}: Props) {
  const options = dashboards.map((d) => ({ value: d.id, label: d.name }));
  return (
    <Group gap="xs" align="center">
      <Select
        data={options}
        value={selectedId}
        onChange={(v) => v && onSelect(v)}
        placeholder="Select a dashboard"
        searchable
        nothingFoundMessage="No dashboards"
        style={{ minWidth: 240 }}
        aria-label="Dashboard"
      />
      <Tooltip label="New dashboard">
        <ActionIcon variant="default" aria-label="New dashboard" onClick={onCreate}>
          <i className="fa fa-plus" />
        </ActionIcon>
      </Tooltip>
      <Menu position="bottom-start" withinPortal>
        <Menu.Target>
          <ActionIcon variant="default" aria-label="Dashboard actions" disabled={!selectedId}>
            <i className="fa fa-gear" />
          </ActionIcon>
        </Menu.Target>
        <Menu.Dropdown>
          <Menu.Item leftSection={<i className="fa fa-pen" />} onClick={onRename}>
            Rename…
          </Menu.Item>
          <Menu.Item
            leftSection={<i className="fa fa-trash" />}
            color="red"
            onClick={onDelete}
          >
            Delete…
          </Menu.Item>
        </Menu.Dropdown>
      </Menu>
    </Group>
  );
}
