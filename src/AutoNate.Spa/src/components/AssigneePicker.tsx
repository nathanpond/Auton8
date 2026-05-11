import { useMemo } from "react";
import { Badge, Box, CloseButton, Group, Select, Text } from "@mantine/core";
import { useUserDirectory, userDisplayName } from "@/hooks/useUserDirectory";
import { useUsers } from "@/hooks/useUsers";

type Props = {
  value: string[];
  onChange: (ids: string[]) => void;
  disabled?: boolean;
};

export default function AssigneePicker({ value, onChange, disabled }: Props) {
  const { data: users = [] } = useUsers();
  const directory = useUserDirectory();

  const selectedLower = useMemo(() => new Set(value.map((id) => id.toLowerCase())), [value]);

  const available = useMemo(
    () =>
      users
        .filter((u) => !selectedLower.has(u.userId.toLowerCase()))
        .sort((a, b) => {
          const an = userDisplayName(a) ?? a.username;
          const bn = userDisplayName(b) ?? b.username;
          return an.localeCompare(bn);
        }),
    [users, selectedLower]
  );

  const remove = (id: string) => {
    onChange(value.filter((v) => v.toLowerCase() !== id.toLowerCase()));
  };

  return (
    <Box>
      <Group gap="xs" wrap="wrap" mb="xs">
        {value.length === 0 ? (
          <Text size="sm" c="dimmed">
            No one assigned
          </Text>
        ) : (
          value.map((id) => {
            const u = directory.get(id);
            const name = userDisplayName(u) ?? `${id.substring(0, 8)}`;
            return (
              <Badge
                key={id}
                color="gray"
                variant="filled"
                size="lg"
                rightSection={
                  <CloseButton
                    size="xs"
                    iconSize={12}
                    onClick={() => remove(id)}
                    aria-label={`Remove ${name}`}
                    disabled={disabled}
                    style={{ color: "inherit" }}
                  />
                }
              >
                {name}
              </Badge>
            );
          })
        )}
      </Group>
      <Select
        value={null}
        onChange={(id) => {
          if (!id) return;
          if (!selectedLower.has(id.toLowerCase())) {
            onChange([...value, id]);
          }
        }}
        placeholder={available.length === 0 ? "All users assigned" : "Add assignee…"}
        disabled={disabled || available.length === 0}
        data={available.map((u) => ({
          value: u.userId,
          label: userDisplayName(u) ?? u.username
        }))}
        searchable
        clearable={false}
      />
    </Box>
  );
}
