import {
  Box,
  Button,
  Code,
  Divider,
  Group,
  Modal,
  ScrollArea,
  SimpleGrid,
  Table,
  Text,
  Title
} from "@mantine/core";
import { usePageTemplates } from "@/hooks/usePageTemplates";

type Props = {
  onClose: () => void;
};

export default function PagesMenusHelpModal({ onClose }: Props) {
  const { data: templates = [], isLoading } = usePageTemplates();

  const sortedTemplates = [...templates].sort((a, b) => a.name.localeCompare(b.name));

  return (
    <Modal opened onClose={onClose} title="How Pages / Menus works" size="xl">
      <Box>
        <Text>
          Every navigation surface on the site is a <strong>menu</strong>. A menu is a tree
          of <strong>menu items</strong>; each item is one of a few types and either links to
          a URL or organizes its siblings. Reorder items by dragging, edit any item by
          clicking the pencil, delete any item with the trash icon.
        </Text>

        <Divider my="md" />

        <Title order={6}>1. The five system menus</Title>
        <ul>
          <li><strong>Main Menu</strong> — the horizontal nav across the top of every page.</li>
          <li>
            <strong>Icon Menu</strong> — the icon strip on the right side of the top bar.
            Each top-level item is an icon; <Code>group</Code> items open a dropdown.
          </li>
          <li>
            <strong>User Menu</strong> — the dropdown that opens beside the signed-in user&apos;s
            name.
          </li>
          <li>
            <strong>Site Configuration</strong> — the left-hand sidebar shown inside the Site
            Configuration area.
          </li>
          <li>
            <strong>Standalone Pages</strong> — a hidden container. Items here are
            URL-reachable but never appear in any visible nav. Use it to expose a page
            template by URL without taking up nav space.
          </li>
        </ul>
        <Text>
          The five system menus can&apos;t be deleted (the lock icon on their tab marks them), but
          their <em>contents</em> are fully editable — you can add, remove, reorder, and
          retype anything inside.
        </Text>

        <Divider my="md" />

        <Title order={6}>2. Menu item types</Title>
        <SimpleGrid cols={{ base: 1, sm: 4 }} spacing="xs">
          <Box><strong>Group</strong></Box>
          <Box style={{ gridColumn: "span 3" }}>
            Header that contains child items. Renders as a dropdown in the main and icon
            menus; renders as a collapsible section in the Site Configuration sidebar.
          </Box>

          <Box><strong>Template</strong></Box>
          <Box style={{ gridColumn: "span 3" }}>
            Mounts a built-in <strong>page template</strong> at a URL. Pick a template, then
            specify the URL where it should be mounted — every template menu item owns its
            own path. <em>See the template catalog below.</em>
          </Box>

          <Box><strong>Route</strong></Box>
          <Box style={{ gridColumn: "span 3" }}>
            Navigates to a hardcoded route in the SPA (e.g. <Code>/records/CAR</Code>,{" "}
            <Code>/workflow</Code>). Set an <em>alias URL</em> to make the menu point at a
            friendlier path (e.g. <Code>/cars</Code>) that renders the same target component.
          </Box>

          <Box><strong>Page</strong></Box>
          <Box style={{ gridColumn: "span 3" }}>
            Defines a brand new URL with custom content. Use HTML for static markup, or JSX
            to write a full React component (hooks, state, API calls) — inline, no rebuild
            required.
          </Box>

          <Box><strong>Link</strong></Box>
          <Box style={{ gridColumn: "span 3" }}>An external URL. Optionally opens in a new tab.</Box>

          <Box><strong>Action</strong></Box>
          <Box style={{ gridColumn: "span 3" }}>
            Triggers a built-in action. Today this is just <Code>logout</Code> (POST{" "}
            <Code>/account/logout</Code>) used by the user menu.
          </Box>

          <Box><strong>Separator</strong></Box>
          <Box style={{ gridColumn: "span 3" }}>
            A divider line. Only renders inside vertical menus (dropdowns and the sidebar) —
            top-level separators are skipped.
          </Box>
        </SimpleGrid>

        <Divider my="md" />

        <Title order={6}>3. Permissions on items</Title>
        <Text>
          Each item has an optional <Code>permission_required</Code> in <Code>kind.action</Code>{" "}
          form (e.g. <Code>siteconfig.edit</Code>). When set, the item is hidden from any
          user who doesn&apos;t have that permission. The check is performed by the backend when
          serving the menu tree — so an admin removing a user&apos;s access can hide entire
          sections of the nav for that user without touching this page.
        </Text>

        <Divider my="md" />

        <Title order={6}>4. Page templates catalog</Title>
        <Text>
          Page templates are the built-in screens that ship with the application. A template
          is only reachable when an admin places it on a menu (any menu — including{" "}
          <strong>Standalone Pages</strong>). The same template can be mounted at multiple
          paths on different menus.
        </Text>

        {isLoading ? (
          <Text c="dimmed" size="sm" mt="xs">Loading templates…</Text>
        ) : sortedTemplates.length === 0 ? (
          <Text c="dimmed" size="sm" mt="xs">No page templates are registered.</Text>
        ) : (
          <ScrollArea.Autosize mah={500} mt="xs">
            <Table withTableBorder striped verticalSpacing="xs">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th style={{ width: 220 }}>Thumbnail</Table.Th>
                  <Table.Th style={{ width: "20%" }}>Template</Table.Th>
                  <Table.Th style={{ width: "15%" }}>Category</Table.Th>
                  <Table.Th>Description</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {sortedTemplates.map((t) => (
                  <Table.Tr key={t.key}>
                    <Table.Td>
                      {t.thumbnailUrl ? (
                        <img
                          src={t.thumbnailUrl}
                          alt={`${t.name} thumbnail`}
                          width={200}
                          height={150}
                          style={{
                            objectFit: "cover",
                            border: "1px solid var(--mantine-color-default-border)",
                            borderRadius: 4
                          }}
                        />
                      ) : (
                        <Box
                          style={{
                            width: 200,
                            height: 150,
                            display: "flex",
                            alignItems: "center",
                            justifyContent: "center",
                            color: "var(--mantine-color-dimmed)",
                            background: "var(--mantine-color-default-hover)",
                            border: "1px solid var(--mantine-color-default-border)",
                            borderRadius: 4,
                            fontSize: 12
                          }}
                        >
                          no thumbnail
                        </Box>
                      )}
                    </Table.Td>
                    <Table.Td>{t.name}</Table.Td>
                    <Table.Td>
                      <Text size="sm" c="dimmed">
                        {t.category ?? <em>(none)</em>}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" c="dimmed">
                        {t.description ?? <em>(no description)</em>}
                      </Text>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </ScrollArea.Autosize>
        )}

        <Divider my="md" />

        <Title order={6}>5. Tips</Title>
        <ul>
          <li>
            <strong>Drag</strong> the grip handle on the left of any row to reorder. Drag
            right to nest under the row above; drag left to un-nest.
          </li>
          <li>
            <strong>Reordering is staged.</strong> Click <em>Save order</em> in the panel
            header to persist; <em>Cancel</em> to revert.
          </li>
          <li>
            <strong>Editing a single item</strong> (icon, type, path, permission) saves
            immediately when you click <em>Apply</em> in the edit modal.
          </li>
          <li>
            <strong>Hidden items</strong> (the eye/visibility flag) stay in the tree but
            don&apos;t render for users — handy for prepping a menu before going live with it.
          </li>
        </ul>
      </Box>
      <Group justify="flex-end" mt="md">
        <Button onClick={onClose}>Close</Button>
      </Group>
    </Modal>
  );
}
