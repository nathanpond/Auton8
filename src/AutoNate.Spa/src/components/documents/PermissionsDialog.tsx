import { toast } from "@/components/notifications/toast";
import { useMemo, useState } from "react";
import {
  ActionIcon,
  Badge,
  Button,
  Divider,
  Group,
  Loader,
  Modal,
  Select,
  Stack,
  Text,
  Tooltip
} from "@mantine/core";
import type { PermissionGrantDto, PrincipalKind, ResourceKind } from "@/api/resourcePermissions";
import {
  useCreateResourcePermission,
  useDeleteResourcePermission,
  useResourcePermissions
} from "@/hooks/useResourcePermissions";
import { useUsers } from "@/hooks/useUsers";
import { useGroups, useRoles } from "@/hooks/useAdmin";

// Phase 9 — self-service permission overrides for a folder/document. Lists
// the allow-grants targeting this resource and lets an editor share it with
// a user/group/role for one of the grantable actions. The backend forces
// effect=allow + the resource selector and rejects actions the caller
// doesn't hold, so this UI only picks principal + action.

type Props = {
  kind: ResourceKind;
  resourceId: string;
  resourceName: string;
  opened: boolean;
  onClose: () => void;
};

const PRINCIPAL_KINDS: { value: PrincipalKind; label: string }[] = [
  { value: "user", label: "User" },
  { value: "group", label: "Group" },
  { value: "role", label: "Role" }
];

function actionLabel(action: string): string {
  return action.charAt(0).toUpperCase() + action.slice(1);
}

export default function PermissionsDialog({
  kind,
  resourceId,
  resourceName,
  opened,
  onClose
}: Props) {
  const { data, isLoading } = useResourcePermissions(kind, resourceId);
  const { data: users = [] } = useUsers();
  const { data: groups = [] } = useGroups();
  const { data: roles = [] } = useRoles();
  const create = useCreateResourcePermission();
  const remove = useDeleteResourcePermission();

  const [principalKind, setPrincipalKind] = useState<PrincipalKind>("user");
  const [principalId, setPrincipalId] = useState<string | null>(null);
  const [action, setAction] = useState<string | null>(null);

  const grantableActions = data?.grantableActions ?? [];
  const overrides = data?.items ?? [];

  // Options for the principal Select, by kind.
  const principalOptions = useMemo(() => {
    if (principalKind === "user") {
      return users.map((u) => ({ value: u.userId, label: u.username }));
    }
    if (principalKind === "group") {
      return groups.map((g) => ({ value: g.id, label: g.name }));
    }
    return roles.map((r) => ({ value: r.id, label: r.isSystem ? `${r.name} (system)` : r.name }));
  }, [principalKind, users, groups, roles]);

  const resolvePrincipalName = (g: PermissionGrantDto): string => {
    if (g.principalKind === "user") {
      return users.find((u) => u.userId === g.principalId)?.username ?? g.principalId;
    }
    if (g.principalKind === "group") {
      return groups.find((x) => x.id === g.principalId)?.name ?? g.principalId;
    }
    return roles.find((r) => r.id === g.principalId)?.name ?? g.principalId;
  };

  const submit = async () => {
    if (!principalId || !action) {
      toast.error("Pick a principal and an action.");
      return;
    }
    try {
      await create.mutateAsync({ kind, resourceId, principalKind, principalId, action });
      toast.success("Access granted.");
      setPrincipalId(null);
      setAction(null);
    } catch (err) {
      toast.error(extractErrorMessage(err) ?? "Failed to grant access.");
    }
  };

  return (
    <Modal opened={opened} onClose={onClose} title={`Permissions — ${resourceName}`} size="lg">
      <Stack>
        <Text size="sm" c="dimmed">
          Share this {kind === "folders" ? "folder (and everything inside it)" : "document"} by
          granting a user, group, or role one of the actions you can perform on it.
        </Text>

        <Divider label="Current overrides" labelPosition="left" />
        {isLoading ? (
          <Group justify="center" py="sm">
            <Loader size="xs" />
          </Group>
        ) : overrides.length === 0 ? (
          <Text size="sm" c="dimmed">
            No overrides yet — access follows project roles.
          </Text>
        ) : (
          <Stack gap={4}>
            {overrides.map((g) => (
              <Group
                key={g.id}
                justify="space-between"
                wrap="nowrap"
                px="xs"
                py={6}
                style={{ border: "1px solid var(--mantine-color-gray-3)", borderRadius: 4 }}
              >
                <Group gap={6} wrap="nowrap" style={{ minWidth: 0 }}>
                  <Badge size="xs" variant="light" color="gray">
                    {g.principalKind}
                  </Badge>
                  <Text size="sm" truncate>
                    {resolvePrincipalName(g)}
                  </Text>
                  <Badge size="xs" variant="light" color="blue">
                    {actionLabel(g.action)}
                  </Badge>
                </Group>
                <Tooltip label="Revoke" withArrow openDelay={350}>
                  <ActionIcon
                    size="sm"
                    variant="subtle"
                    color="red"
                    aria-label="Revoke override"
                    onClick={() =>
                      remove.mutate(
                        { kind, resourceId, grantId: g.id },
                        {
                          onSuccess: () =>
                            toast.success("Override revoked."),
                          onError: (err) =>
                            toast.error(extractErrorMessage(err) ?? "Failed to revoke.")
                        }
                      )
                    }
                  >
                    <i className="fa fa-trash" aria-hidden />
                  </ActionIcon>
                </Tooltip>
              </Group>
            ))}
          </Stack>
        )}

        <Divider label="Grant access" labelPosition="left" />
        <Group grow align="flex-end">
          <Select
            label="Principal type"
            data={PRINCIPAL_KINDS}
            value={principalKind}
            onChange={(v) => {
              if (v) {
                setPrincipalKind(v as PrincipalKind);
                setPrincipalId(null);
              }
            }}
            allowDeselect={false}
          />
          <Select
            label={principalKind === "user" ? "User" : principalKind === "group" ? "Group" : "Role"}
            placeholder="Search…"
            data={principalOptions}
            value={principalId}
            onChange={setPrincipalId}
            searchable
            nothingFoundMessage="No match"
          />
          <Select
            label="Action"
            placeholder="Pick…"
            data={grantableActions.map((a) => ({ value: a, label: actionLabel(a) }))}
            value={action}
            onChange={setAction}
            allowDeselect={false}
          />
        </Group>
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            Done
          </Button>
          <Button onClick={submit} loading={create.isPending} disabled={!principalId || !action}>
            Grant
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function extractErrorMessage(err: unknown): string | null {
  if (
    typeof err === "object" &&
    err &&
    "response" in err &&
    (err as { response?: { data?: { error?: string } } }).response?.data?.error
  ) {
    return (err as { response: { data: { error: string } } }).response.data.error;
  }
  if (typeof err === "object" && err && "message" in err) {
    return String((err as { message: unknown }).message);
  }
  return null;
}
