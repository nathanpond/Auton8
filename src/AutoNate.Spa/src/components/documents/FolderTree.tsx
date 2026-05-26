import { useState } from "react";
import { ActionIcon, Box, Group, Loader, Menu, Text, Tooltip, UnstyledButton } from "@mantine/core";
import { FolderDto } from "@/api/documents";
import { useFolderChildren, useProjectRootFolders } from "@/hooks/useDocuments";

// Collapsible, lazy-loaded folder tree for the Documents sidebar.
// Root list comes from useProjectRootFolders(projectId); each expanded node
// fetches its own children via useFolderChildren so we never materialise the
// entire project's folder graph upfront.

type FolderTreeProps = {
  projectId: string;
  selectedFolderId: string | null;
  onSelectFolder: (folderId: string | null) => void;
  onCreateChild?: (parent: FolderDto | null) => void;
  onRenameFolder?: (folder: FolderDto) => void;
  onDeleteFolder?: (folder: FolderDto) => void;
};

export default function FolderTree({
  projectId,
  selectedFolderId,
  onSelectFolder,
  onCreateChild,
  onRenameFolder,
  onDeleteFolder
}: FolderTreeProps) {
  const { data: roots = [], isLoading, error } = useProjectRootFolders(projectId);

  return (
    <Box style={{ display: "flex", flexDirection: "column", minHeight: 0 }}>
      <Group justify="space-between" px="sm" py="xs">
        <UnstyledButton
          onClick={() => onSelectFolder(null)}
          style={{
            fontWeight: 600,
            color:
              selectedFolderId === null
                ? "var(--mantine-color-blue-7)"
                : "var(--mantine-color-text)"
          }}
        >
          <Group gap={6}>
            <i className="fa fa-folder-tree" aria-hidden />
            <Text size="sm">Project root</Text>
          </Group>
        </UnstyledButton>
        {onCreateChild ? (
          <Tooltip label="New folder at root" withArrow>
            <ActionIcon
              size="sm"
              variant="subtle"
              aria-label="New folder at root"
              onClick={() => onCreateChild(null)}
            >
              <i className="fa fa-plus" aria-hidden />
            </ActionIcon>
          </Tooltip>
        ) : null}
      </Group>

      {isLoading ? (
        <Group justify="center" py="sm">
          <Loader size="xs" />
        </Group>
      ) : null}
      {error ? (
        <Text size="xs" c="red" px="sm">
          Failed to load folders.
        </Text>
      ) : null}
      {!isLoading && roots.length === 0 ? (
        <Text size="xs" c="dimmed" px="sm" py="xs">
          No folders yet.
        </Text>
      ) : null}

      <Box style={{ overflowY: "auto", minHeight: 0 }}>
        {roots.map((root) => (
          <FolderNode
            key={root.id}
            folder={root}
            depth={0}
            selectedFolderId={selectedFolderId}
            onSelectFolder={onSelectFolder}
            onCreateChild={onCreateChild}
            onRenameFolder={onRenameFolder}
            onDeleteFolder={onDeleteFolder}
          />
        ))}
      </Box>
    </Box>
  );
}

type FolderNodeProps = {
  folder: FolderDto;
  depth: number;
  selectedFolderId: string | null;
  onSelectFolder: (folderId: string | null) => void;
  onCreateChild?: (parent: FolderDto | null) => void;
  onRenameFolder?: (folder: FolderDto) => void;
  onDeleteFolder?: (folder: FolderDto) => void;
};

// Each node owns its expanded state; the React Query hook is only enabled
// once `expanded === true` so closed branches never round-trip.
function FolderNode({
  folder,
  depth,
  selectedFolderId,
  onSelectFolder,
  onCreateChild,
  onRenameFolder,
  onDeleteFolder
}: FolderNodeProps) {
  const [expanded, setExpanded] = useState(false);
  const { data: childResponse, isFetching } = useFolderChildren(expanded ? folder.id : null);
  // Tree shows folders only — documents render inside the right-side grid.
  const children = childResponse?.folders;

  const isSelected = selectedFolderId === folder.id;
  // Indent each level by 16px; chevron column reserves another 18px so labels
  // line up regardless of whether the row is a leaf.
  const indent = depth * 16;

  return (
    <Box>
      <Group
        gap={4}
        wrap="nowrap"
        px="sm"
        py={4}
        style={{
          paddingLeft: 8 + indent,
          background: isSelected ? "var(--mantine-color-blue-light)" : undefined,
          borderLeft: isSelected
            ? "2px solid var(--mantine-color-blue-7)"
            : "2px solid transparent"
        }}
      >
        <ActionIcon
          size="xs"
          variant="subtle"
          aria-label={expanded ? "Collapse folder" : "Expand folder"}
          onClick={() => setExpanded((e) => !e)}
        >
          <i
            className={expanded ? "fa fa-chevron-down" : "fa fa-chevron-right"}
            aria-hidden
          />
        </ActionIcon>
        <UnstyledButton
          onClick={() => onSelectFolder(folder.id)}
          style={{ flex: 1, minWidth: 0, overflow: "hidden", textAlign: "left" }}
        >
          <Group gap={6} wrap="nowrap">
            <i
              className={folder.icon ? folder.icon : "fa fa-folder"}
              aria-hidden
              style={{ color: "var(--mantine-color-yellow-6)" }}
            />
            <Text
              size="sm"
              truncate
              fw={isSelected ? 600 : 400}
              c={folder.isArchived ? "dimmed" : undefined}
            >
              {folder.name}
            </Text>
          </Group>
        </UnstyledButton>
        <Menu position="bottom-end" shadow="sm">
          <Menu.Target>
            <ActionIcon size="sm" variant="subtle" aria-label="Folder menu">
              <i className="fa fa-ellipsis-vertical" aria-hidden />
            </ActionIcon>
          </Menu.Target>
          <Menu.Dropdown>
            {onCreateChild ? (
              <Menu.Item
                leftSection={<i className="fa fa-folder-plus" aria-hidden />}
                onClick={() => {
                  setExpanded(true);
                  onCreateChild(folder);
                }}
              >
                New subfolder
              </Menu.Item>
            ) : null}
            {onRenameFolder ? (
              <Menu.Item
                leftSection={<i className="fa fa-pen-to-square" aria-hidden />}
                onClick={() => onRenameFolder(folder)}
              >
                Rename
              </Menu.Item>
            ) : null}
            {onDeleteFolder ? (
              <>
                <Menu.Divider />
                <Menu.Item
                  color="red"
                  leftSection={<i className="fa fa-trash" aria-hidden />}
                  onClick={() => onDeleteFolder(folder)}
                >
                  Delete folder
                </Menu.Item>
              </>
            ) : null}
          </Menu.Dropdown>
        </Menu>
      </Group>

      {expanded ? (
        <Box>
          {isFetching && !children ? (
            <Group justify="center" py={4}>
              <Loader size="xs" />
            </Group>
          ) : null}
          {children && children.length === 0 ? (
            <Text size="xs" c="dimmed" px="sm" py={2} style={{ paddingLeft: 8 + indent + 24 }}>
              Empty
            </Text>
          ) : null}
          {(children ?? []).map((child) => (
            <FolderNode
              key={child.id}
              folder={child}
              depth={depth + 1}
              selectedFolderId={selectedFolderId}
              onSelectFolder={onSelectFolder}
              onCreateChild={onCreateChild}
              onRenameFolder={onRenameFolder}
              onDeleteFolder={onDeleteFolder}
            />
          ))}
        </Box>
      ) : null}
    </Box>
  );
}
