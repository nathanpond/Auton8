import { toast } from "@/components/notifications/toast";
import { FormEvent, useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Alert,
  Badge,
  Button,
  Divider,
  Group,
  List,
  Modal,
  NativeSelect,
  Stack,
  Text,
  Textarea,
  TextInput,
  Title,
  Tooltip
} from "@mantine/core";
import {
  GroupMapping,
  PreviewedGroup,
  createGroupMapping,
  deleteGroupMapping,
  listGroupMappings,
  previewGroupMappings
} from "@/api/identityProviders";
import { Group as AuthGroup, listGroups } from "@/api/admin";

type Props = {
  providerId: string | null;
  providerName: string;
  /** OIDC calls it a claim; SAML calls it an attribute. Same field. */
  claimWord: string;
  onClose: () => void;
};

const EXAMPLE_CLAIMS = `{\n  "groups": ["engineering", "on-call"]\n}`;

/**
 * Claim → group mappings for one provider, with a preview (#92).
 *
 * The mapping is the whole gate: a group created at the identity provider has
 * no effect in Auton8 until someone here maps it. That is what stops federation
 * becoming a second bulk-grant path, where anyone who can create a group at the
 * IdP can grant themselves access here.
 */
export function GroupMappingsModal({ providerId, providerName, claimWord, onClose }: Props) {
  const queryClient = useQueryClient();
  const [claimType, setClaimType] = useState("groups");
  const [claimValue, setClaimValue] = useState("");
  const [groupId, setGroupId] = useState("");
  const [claimsJson, setClaimsJson] = useState(EXAMPLE_CLAIMS);
  const [preview, setPreview] = useState<PreviewedGroup[] | null>(null);
  const [previewError, setPreviewError] = useState<string | null>(null);

  const mappingsKey = useMemo(
    () => ["identity-provider-group-mappings", providerId] as const,
    [providerId]
  );

  const mappings = useQuery({
    queryKey: mappingsKey,
    queryFn: ({ signal }) => listGroupMappings(providerId!, signal),
    enabled: providerId !== null
  });

  const groups = useQuery({
    queryKey: ["admin-groups", "for-mapping"] as const,
    queryFn: ({ signal }) => listGroups(false, signal),
    enabled: providerId !== null
  });

  // A stale preview beside a changed mapping table reads as the truth and is
  // not, which is the one way this screen could mislead.
  useEffect(() => {
    setPreview(null);
    setPreviewError(null);
  }, [providerId]);

  const createMutation = useMutation({
    mutationFn: () =>
      createGroupMapping(providerId!, { claimType, claimValue, groupId }),
    onSuccess: async () => {
      toast.success("Mapping added.");
      setClaimValue("");
      setPreview(null);
      await queryClient.invalidateQueries({ queryKey: mappingsKey });
    },
    onError: (error) => toast.error(readError(error))
  });

  const deleteMutation = useMutation({
    mutationFn: (mappingId: string) => deleteGroupMapping(providerId!, mappingId),
    onSuccess: async () => {
      toast.success("Mapping removed.");
      setPreview(null);
      await queryClient.invalidateQueries({ queryKey: mappingsKey });
    },
    onError: (error) => toast.error(readError(error))
  });

  const previewMutation = useMutation({
    mutationFn: (claims: Record<string, string[]>) =>
      previewGroupMappings(providerId!, claims),
    onSuccess: (result) => {
      setPreview(result);
      setPreviewError(null);
    },
    onError: (error) => {
      setPreview(null);
      setPreviewError(readError(error));
    }
  });

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    if (!claimValue.trim() || !groupId) return;
    createMutation.mutate();
  };

  const runPreview = () => {
    let parsed: unknown;
    try {
      parsed = JSON.parse(claimsJson);
    } catch {
      setPreview(null);
      setPreviewError(
        "That is not valid JSON. Expected an object of claim type to a list of values."
      );
      return;
    }

    const claims = normaliseClaims(parsed);
    if (!claims) {
      setPreview(null);
      setPreviewError(
        `Expected an object whose values are strings or lists of strings, like ${EXAMPLE_CLAIMS.replace(/\n\s*/g, " ")}.`
      );
      return;
    }

    previewMutation.mutate(claims);
  };

  const groupOptions = [
    { value: "", label: "Select a group…" },
    ...(groups.data ?? []).map((g: AuthGroup) => ({ value: g.id, label: g.name }))
  ];

  return (
    <Modal
      opened={providerId !== null}
      onClose={onClose}
      title={`Group mappings — ${providerName}`}
      size="lg"
    >
      <Stack gap="md">
        <Alert color="blue" variant="light" icon={<i className="fa fa-circle-info" />}>
          A {claimWord} value grants membership of the group it is mapped to, and nothing
          else — a mapping cannot grant a role directly, so the group → role path stays the
          single place access is decided. An unmapped {claimWord} grants nothing.
        </Alert>

        <MappingList
          mappings={mappings.data}
          isLoading={mappings.isLoading}
          claimWord={claimWord}
          onDelete={(id) => deleteMutation.mutate(id)}
          deletingId={deleteMutation.isPending ? deleteMutation.variables : undefined}
        />

        <Divider />

        <form onSubmit={onSubmit}>
          <Stack gap="sm">
            <Title order={6}>Add a mapping</Title>
            <Group grow align="flex-end">
              <TextInput
                label={`${capitalise(claimWord)} type`}
                description="Usually 'groups' for OIDC; for SAML, the attribute name."
                value={claimType}
                onChange={(e) => {
                  const value = e.currentTarget.value;
                  setClaimType(value);
                }}
              />
              <TextInput
                label={`${capitalise(claimWord)} value`}
                description="Matched exactly. No wildcards."
                value={claimValue}
                onChange={(e) => {
                  const value = e.currentTarget.value;
                  setClaimValue(value);
                }}
              />
            </Group>
            <NativeSelect
              label="Grants membership of"
              data={groupOptions}
              value={groupId}
              onChange={(e) => {
                const value = e.currentTarget.value;
                setGroupId(value);
              }}
            />
            <Group justify="flex-end">
              <Button
                type="submit"
                loading={createMutation.isPending}
                disabled={!claimValue.trim() || !groupId}
              >
                Add mapping
              </Button>
            </Group>
          </Stack>
        </form>

        <Divider />

        <Stack gap="sm">
          <Title order={6}>What would these {claimWord}s grant?</Title>
          <Text size="xs" c="dimmed">
            Answered by the same code a real sign-in runs, so you can check a mapping
            without asking someone to sign in over and over.
          </Text>
          <Textarea
            label={`${capitalise(claimWord)}s`}
            autosize
            minRows={3}
            maxRows={10}
            styles={{ input: { fontFamily: "var(--mantine-font-family-monospace)" } }}
            value={claimsJson}
            onChange={(e) => {
              const value = e.currentTarget.value;
              setClaimsJson(value);
            }}
          />
          <Group justify="flex-end">
            <Button
              variant="default"
              onClick={runPreview}
              loading={previewMutation.isPending}
            >
              Preview
            </Button>
          </Group>

          {previewError !== null && (
            <Alert color="red" variant="light" title="That could not be read">
              {previewError}
            </Alert>
          )}

          {preview !== null &&
            (preview.length === 0 ? (
              <Alert color="gray" variant="light">
                Nothing. None of those {claimWord}s is mapped, which is not an error — an
                identity provider hands over every group a person belongs to, and most of
                them mean nothing here.
              </Alert>
            ) : (
              <Alert color="green" variant="light" title="Would grant">
                <List size="sm">
                  {preview.map((g) => (
                    <List.Item key={g.id}>
                      {g.name}
                      {g.isArchived && (
                        <Badge ml="xs" size="xs" color="gray">
                          archived
                        </Badge>
                      )}
                    </List.Item>
                  ))}
                </List>
              </Alert>
            ))}
        </Stack>
      </Stack>
    </Modal>
  );
}

function MappingList({
  mappings,
  isLoading,
  claimWord,
  onDelete,
  deletingId
}: {
  mappings: GroupMapping[] | undefined;
  isLoading: boolean;
  claimWord: string;
  onDelete: (id: string) => void;
  deletingId: string | undefined;
}) {
  if (isLoading) return <Text size="sm">Loading…</Text>;

  if (!mappings || mappings.length === 0) {
    return (
      <Alert color="gray" variant="light">
        No mappings. Until one exists, everybody signing in through this provider arrives
        with no groups and no access — which is deliberate, not a gap: access here is
        granted here.
      </Alert>
    );
  }

  return (
    <Stack gap="xs">
      {mappings.map((m) => (
        <Group key={m.id} justify="space-between" wrap="nowrap">
          <Text size="sm">
            <Text span ff="monospace">
              {m.claimType}
            </Text>{" "}
            = <Text span ff="monospace">{m.claimValue}</Text> → <b>{m.groupName}</b>
            {m.groupIsArchived && (
              <Badge ml="xs" size="xs" color="gray">
                archived
              </Badge>
            )}
          </Text>
          <Tooltip label={`Remove this ${claimWord} mapping`}>
            <ActionIcon
              variant="subtle"
              color="red"
              aria-label={`Remove mapping ${m.claimType}=${m.claimValue}`}
              loading={deletingId === m.id}
              onClick={() => onDelete(m.id)}
            >
              <i className="fa fa-trash" aria-hidden="true" />
            </ActionIcon>
          </Tooltip>
        </Group>
      ))}
    </Stack>
  );
}

/**
 * Accepts either shape an identity provider might produce.
 *
 * A single-valued claim arrives as a bare string and a multi-valued one as a
 * list; pasting a real token's payload should work without the administrator
 * having to reshape it first.
 */
function normaliseClaims(parsed: unknown): Record<string, string[]> | null {
  if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) return null;

  const claims: Record<string, string[]> = {};
  for (const [key, value] of Object.entries(parsed as Record<string, unknown>)) {
    if (typeof value === "string") {
      claims[key] = [value];
    } else if (Array.isArray(value) && value.every((v) => typeof v === "string")) {
      claims[key] = value as string[];
    } else {
      return null;
    }
  }
  return claims;
}

const capitalise = (s: string) => s.charAt(0).toUpperCase() + s.slice(1);

function readError(error: unknown): string {
  const response = (error as { response?: { data?: { error?: string } } })?.response;
  return response?.data?.error ?? (error as Error)?.message ?? "Unexpected error.";
}
