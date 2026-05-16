import { useEffect, useMemo, useState } from "react";
import {
  Alert,
  Badge,
  Box,
  Button,
  Checkbox,
  Group,
  Modal,
  Select,
  Stack,
  Text,
  Tooltip
} from "@mantine/core";
import { useMutation, useQuery } from "@tanstack/react-query";
import {
  PageSharePreviewResponse,
  previewPageShare,
  sharePage
} from "@/api/content";
import { useUsers } from "@/hooks/useUsers";
import { useMe } from "@/hooks/useMe";
import { useUserDirectory, userDisplayName } from "@/hooks/useUserDirectory";

type Props = {
  pageId: string;
  pageTitle: string;
  onClose: () => void;
};

// Share-page modal. Multi-select user picker; per-row green check / red
// "denied" indicator pulled from the share/preview endpoint. Owners get a
// "Grant view to selected" checkbox that lifts the per-page allow grants for
// any user without access. Non-owners can still send — but only users who
// already have access will receive a notification; the rest are listed as
// skipped before submit so the warning is visible.
export function ShareModal({ pageId, pageTitle, onClose }: Props) {
  const { data: users = [] } = useUsers();
  const directory = useUserDirectory();
  const meQuery = useMe();
  const meId = meQuery.data?.authenticated ? meQuery.data.userId : null;

  const [selected, setSelected] = useState<string[]>([]);
  const [grantAccess, setGrantAccess] = useState(false);
  const [flash, setFlash] = useState<{ kind: "success" | "error"; text: string } | null>(null);

  // Preview query — fires on every selection change. The endpoint is cheap
  // (one auth eval per id) and the result drives every per-row indicator,
  // so we don't want to debounce.
  const previewQuery = useQuery<PageSharePreviewResponse>({
    queryKey: ["page-share", "preview", pageId, [...selected].sort()],
    queryFn: () => previewPageShare(pageId, selected),
    enabled: selected.length > 0,
    staleTime: 30_000
  });

  // If selection empties out we still want to know whether the caller is the
  // project owner (so the checkbox/help text render correctly). One probe
  // with no user list still returns the isOwner flag.
  const ownerQuery = useQuery<PageSharePreviewResponse>({
    queryKey: ["page-share", "preview-owner", pageId],
    queryFn: () => previewPageShare(pageId, []),
    staleTime: 60_000
  });
  const isOwner = previewQuery.data?.isOwner ?? ownerQuery.data?.isOwner ?? false;

  const accessByUser = useMemo(() => {
    const m = new Map<string, boolean>();
    for (const u of previewQuery.data?.users ?? []) {
      m.set(u.userId.toLowerCase(), u.canView);
    }
    return m;
  }, [previewQuery.data]);

  const selectedSet = useMemo(
    () => new Set(selected.map((id) => id.toLowerCase())),
    [selected]
  );

  // Sort users by display name and drop the current user + anyone already
  // picked. Picking yourself is meaningless (you wouldn't notify yourself).
  const availableOptions = useMemo(() => {
    return users
      .filter((u) => !selectedSet.has(u.userId.toLowerCase()))
      .filter((u) => !meId || u.userId.toLowerCase() !== meId.toLowerCase())
      .map((u) => ({
        value: u.userId,
        label: userDisplayName(u) ?? u.username
      }))
      .sort((a, b) => a.label.localeCompare(b.label));
  }, [users, selectedSet, meId]);

  const deniedCount = useMemo(() => {
    if (previewQuery.data == null) return 0;
    return previewQuery.data.users.filter((u) => !u.canView).length;
  }, [previewQuery.data]);

  // When ownership is unknown, hide owner-only chrome to avoid flicker.
  const showGrantCheckbox = isOwner && deniedCount > 0;

  // If the user toggled grant on, then deselected every denied user, the
  // checkbox should auto-clear so the non-owner warning doesn't suddenly
  // appear on the next click.
  useEffect(() => {
    if (deniedCount === 0 && grantAccess) setGrantAccess(false);
  }, [deniedCount, grantAccess]);

  const shareMutation = useMutation({
    mutationFn: () =>
      sharePage(pageId, {
        userIds: selected,
        grantAccess: isOwner && grantAccess
      }),
    onSuccess: (res) => {
      const notified = res.notifiedUserIds.length;
      const skipped = res.skippedUserIds.length;
      const granted = res.grantedUserIds.length;
      const parts: string[] = [];
      if (notified > 0) parts.push(`Notified ${notified}`);
      if (granted > 0) parts.push(`granted access to ${granted}`);
      if (skipped > 0) parts.push(`skipped ${skipped} without access`);
      setFlash({
        kind: "success",
        text: parts.length > 0
          ? parts.join(", ") + "."
          : "No recipients."
      });
      // Clear selection so the modal can be reused, but keep it open so the
      // sharer sees the result line.
      setSelected([]);
    },
    onError: (err: unknown) => {
      const msg =
        typeof err === "object" && err !== null && "response" in err
          ? (err as { response?: { data?: { error?: string } } }).response?.data?.error
          : null;
      setFlash({ kind: "error", text: msg ?? "Share failed." });
    }
  });

  const canSubmit = selected.length > 0 && !shareMutation.isPending;

  return (
    <Modal
      opened
      onClose={onClose}
      title={
        <Text fw={700}>
          Share <Text component="span" c="dimmed">— {pageTitle}</Text>
        </Text>
      }
      size="lg"
      centered
    >
      <Stack gap="md">
        <Box>
          <Text size="sm" mb={6}>
            Pick the people you want to share this page with.
          </Text>
          <Select
            value={null}
            placeholder={
              availableOptions.length === 0
                ? "Everyone is already selected"
                : "Add a user…"
            }
            data={availableOptions}
            disabled={availableOptions.length === 0}
            searchable
            clearable={false}
            onChange={(id) => {
              if (!id) return;
              if (!selectedSet.has(id.toLowerCase())) {
                setSelected((prev) => [...prev, id]);
              }
            }}
          />
        </Box>

        {selected.length === 0 ? (
          <Text size="sm" c="dimmed">No users selected.</Text>
        ) : (
          <Stack gap={6}>
            {selected.map((id) => {
              const user = directory.get(id);
              const name = userDisplayName(user) ?? `${id.slice(0, 8)}…`;
              const access = accessByUser.get(id.toLowerCase());
              return (
                <Group
                  key={id}
                  justify="space-between"
                  wrap="nowrap"
                  style={{
                    border: "1px solid var(--mantine-color-default-border)",
                    borderRadius: 4,
                    padding: "6px 10px"
                  }}
                >
                  <Group gap="xs" wrap="nowrap" style={{ minWidth: 0 }}>
                    <AccessIndicator
                      state={
                        previewQuery.isPending && access === undefined
                          ? "loading"
                          : access === true
                            ? "allowed"
                            : access === false
                              ? "denied"
                              : "loading"
                      }
                    />
                    <Text size="sm" truncate>{name}</Text>
                  </Group>
                  <Button
                    variant="subtle"
                    color="gray"
                    size="compact-xs"
                    onClick={() =>
                      setSelected((prev) =>
                        prev.filter((x) => x.toLowerCase() !== id.toLowerCase())
                      )
                    }
                  >
                    Remove
                  </Button>
                </Group>
              );
            })}
          </Stack>
        )}

        {showGrantCheckbox && (
          <Checkbox
            checked={grantAccess}
            onChange={(e) => setGrantAccess(e.currentTarget.checked)}
            label={
              <span>
                Grant view access to this page for selected users without access
                <Text component="span" c="dimmed" size="sm" ml={6}>
                  (owner only — applies just to this page)
                </Text>
              </span>
            }
          />
        )}

        {!isOwner && deniedCount > 0 && (
          <Alert color="yellow" variant="light">
            {deniedCount === 1 ? "1 user" : `${deniedCount} users`} can't view
            this page yet. Only a project owner can grant access — they will
            not receive a share notification.
          </Alert>
        )}

        {flash && (
          <Alert
            color={flash.kind === "success" ? "green" : "red"}
            variant="light"
            role={flash.kind === "success" ? "status" : "alert"}
          >
            {flash.text}
          </Alert>
        )}

        <Group justify="flex-end" gap="sm">
          <Button variant="default" onClick={onClose}>Close</Button>
          <Button
            onClick={() => shareMutation.mutate()}
            disabled={!canSubmit}
            loading={shareMutation.isPending}
          >
            Share
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function AccessIndicator({
  state
}: {
  state: "loading" | "allowed" | "denied";
}) {
  if (state === "loading") {
    return (
      <Tooltip label="Checking access…" withArrow>
        <i className="fa fa-spinner fa-spin" style={{ color: "var(--mantine-color-dimmed)" }} />
      </Tooltip>
    );
  }
  if (state === "allowed") {
    return (
      <Tooltip label="Has view access" withArrow>
        <Badge color="green" variant="filled" radius="xl" px={6} py={0}>
          <i className="fa fa-check" style={{ fontSize: 10 }} />
        </Badge>
      </Tooltip>
    );
  }
  return (
    <Tooltip label="No view access" withArrow>
      <Badge color="red" variant="filled" radius="xl" px={6} py={0}>
        <i className="fa fa-ban" style={{ fontSize: 10 }} />
      </Badge>
    </Tooltip>
  );
}
