import { ReactNode } from "react";
import { Group, Stack, Text, Title } from "@mantine/core";

type PageHeaderProps = {
  title: ReactNode;
  description?: ReactNode;
  /** Optional right-aligned slot for action buttons / badges. */
  actions?: ReactNode;
};

// Drop-in replacement for the ColorAdmin `.page-head` / `.page-header`
// / `.page-head-copy` block used at the top of admin and content pages.
// Consistent typography (`<Title>` + dimmed `<Text>`) with an optional
// right-aligned actions slot for buttons / badges.
export default function PageHeader({ title, description, actions }: PageHeaderProps) {
  return (
    <Group justify="space-between" align="flex-start" wrap="wrap" gap="md" mb="md">
      <Stack gap={4} style={{ flex: 1, minWidth: 0 }}>
        <Title order={1}>{title}</Title>
        {description && (
          <Text size="sm" c="dimmed">
            {description}
          </Text>
        )}
      </Stack>
      {actions}
    </Group>
  );
}
