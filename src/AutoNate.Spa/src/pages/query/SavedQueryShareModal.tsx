import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Alert,
  Anchor,
  Button,
  CopyButton,
  Divider,
  Group,
  Modal,
  NumberInput,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
  Tooltip
} from "@mantine/core";
import { notifications } from "@mantine/notifications";
import {
  IssuedShareToken,
  SavedQuery,
  SavedQueryShareToken,
  issueSavedQueryShare,
  listSavedQueryShares,
  revokeSavedQueryShare
} from "@/api/savedQueries";

type Props = {
  savedQuery: SavedQuery | null;
  opened: boolean;
  onClose: () => void;
};

// Phase 3 share UI (docs/plans/2026-05-30-data-stores-implementation.md).
// One modal per saved query: list issued tokens, generate a new one with
// optional expiry + max-uses + label, revoke from the row. The raw token
// is shown ONCE immediately after issuance and never round-trips through
// the list — the SPA stores it in component state long enough to copy
// to the clipboard.
export default function SavedQueryShareModal({ savedQuery, opened, onClose }: Props) {
  const queryClient = useQueryClient();
  const [label, setLabel] = useState("");
  const [maxUses, setMaxUses] = useState<number | undefined>(undefined);
  const [expiresAt, setExpiresAt] = useState("");
  const [hasExpiry, setHasExpiry] = useState(false);
  const [justIssued, setJustIssued] = useState<IssuedShareToken | null>(null);

  const id = savedQuery?.id ?? null;

  const tokensQuery = useQuery({
    queryKey: ["saved-queries", id, "shares"],
    enabled: opened && id !== null,
    queryFn: ({ signal }) => listSavedQueryShares(id!, signal)
  });

  useEffect(() => {
    if (!opened) {
      setLabel("");
      setMaxUses(undefined);
      setExpiresAt("");
      setHasExpiry(false);
      setJustIssued(null);
    }
  }, [opened]);

  const issueMutation = useMutation({
    mutationFn: () =>
      issueSavedQueryShare(id!, {
        expiresAtUtc: hasExpiry && expiresAt ? new Date(expiresAt).toISOString() : null,
        maxUses: maxUses ?? null,
        label: label.trim() || null
      }),
    onSuccess: (issued) => {
      setJustIssued(issued);
      setLabel("");
      setMaxUses(undefined);
      setExpiresAt("");
      setHasExpiry(false);
      queryClient.invalidateQueries({ queryKey: ["saved-queries", id, "shares"] });
      notifications.show({ message: "Share link generated.", color: "green" });
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { reason?: string } } })?.response?.data?.reason ??
        (err instanceof Error ? err.message : "Failed to issue share link.");
      notifications.show({ message, color: "red" });
    }
  });

  const revokeMutation = useMutation({
    mutationFn: (tokenId: string) => revokeSavedQueryShare(id!, tokenId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["saved-queries", id, "shares"] });
      notifications.show({ message: "Share link revoked.", color: "green" });
    },
    onError: (err: unknown) => {
      const message = err instanceof Error ? err.message : "Revoke failed.";
      notifications.show({ message, color: "red" });
    }
  });

  return (
    <Modal opened={opened} onClose={onClose} title={`Share "${savedQuery?.name ?? ""}"`} size="lg" centered>
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          Generate a link that runs this query for anyone who visits the URL — no AutoNate account
          required. The query executes under your identity, so the result rows reflect what you
          can see, not what the visitor can see. Revoke any link at any time.
        </Text>

        {justIssued ? (
          <Alert color="teal" title="Copy this link now — it won't be shown again">
            <Stack gap={4}>
              <Anchor href={justIssued.shareUrl} target="_blank" rel="noreferrer" size="sm">
                {justIssued.shareUrl}
              </Anchor>
              <Group gap={4}>
                <CopyButton value={justIssued.shareUrl}>
                  {({ copied, copy }) => (
                    <Button size="xs" onClick={copy} variant={copied ? "filled" : "light"}>
                      {copied ? "Copied" : "Copy link"}
                    </Button>
                  )}
                </CopyButton>
              </Group>
            </Stack>
          </Alert>
        ) : null}

        <Divider label="Generate a new link" labelPosition="left" />

        <Stack gap="xs">
          <TextInput
            label="Label"
            description="Remind yourself who you sent this to."
            placeholder="Q3 sales — marketing team"
            value={label}
            onChange={(e) => setLabel(e.currentTarget.value)}
          />
          <NumberInput
            label="Max uses"
            description="Leave blank for unlimited."
            placeholder="Unlimited"
            min={1}
            value={maxUses}
            onChange={(v) => setMaxUses(typeof v === "number" ? v : undefined)}
          />
          <Switch
            label="Expires"
            checked={hasExpiry}
            onChange={(e) => setHasExpiry(e.currentTarget.checked)}
          />
          {hasExpiry ? (
            <TextInput
              label="Expires at"
              description="Local datetime (browser converts to UTC on issue)."
              type="datetime-local"
              value={expiresAt}
              onChange={(e) => setExpiresAt(e.currentTarget.value)}
            />
          ) : null}
          <Group justify="flex-end">
            <Button
              onClick={() => issueMutation.mutate()}
              loading={issueMutation.isPending}
              disabled={id === null}
            >
              Generate link
            </Button>
          </Group>
        </Stack>

        <Divider label="Existing links" labelPosition="left" />

        {tokensQuery.isLoading ? (
          <Text c="dimmed" size="sm">
            Loading…
          </Text>
        ) : (tokensQuery.data ?? []).length === 0 ? (
          <Text c="dimmed" size="sm">
            No share links yet.
          </Text>
        ) : (
          <Table verticalSpacing="xs" striped>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Label</Table.Th>
                <Table.Th>Issued</Table.Th>
                <Table.Th>Expires</Table.Th>
                <Table.Th>Uses</Table.Th>
                <Table.Th>Status</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {(tokensQuery.data ?? []).map((t) => (
                <Table.Tr key={t.id}>
                  <Table.Td>{t.label ?? <Text c="dimmed">—</Text>}</Table.Td>
                  <Table.Td>{new Date(t.issuedAtUtc).toLocaleString()}</Table.Td>
                  <Table.Td>
                    {t.expiresAtUtc ? new Date(t.expiresAtUtc).toLocaleString() : <Text c="dimmed">Never</Text>}
                  </Table.Td>
                  <Table.Td>
                    {t.useCount}
                    {t.maxUses ? ` / ${t.maxUses}` : ""}
                  </Table.Td>
                  <Table.Td>{statusLabel(t)}</Table.Td>
                  <Table.Td>
                    {t.revokedAtUtc === null ? (
                      <Tooltip label="Revoke this link">
                        <ActionIcon
                          color="red"
                          variant="subtle"
                          aria-label={`Revoke ${t.label ?? t.id}`}
                          onClick={() => revokeMutation.mutate(t.id)}
                          loading={revokeMutation.isPending && revokeMutation.variables === t.id}
                        >
                          <i className="fa fa-trash" />
                        </ActionIcon>
                      </Tooltip>
                    ) : null}
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Stack>
    </Modal>
  );
}

function statusLabel(t: SavedQueryShareToken): string {
  if (t.revokedAtUtc) return "revoked";
  if (t.expiresAtUtc && new Date(t.expiresAtUtc) <= new Date()) return "expired";
  if (t.maxUses && t.useCount >= t.maxUses) return "exhausted";
  return "active";
}
