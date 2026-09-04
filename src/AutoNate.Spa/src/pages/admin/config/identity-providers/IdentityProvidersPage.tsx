import { FormEvent, useEffect, useMemo, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Alert,
  Badge,
  Button,
  Card,
  Code,
  Group,
  List,
  Modal,
  NativeSelect,
  Stack,
  Switch,
  Text,
  Textarea,
  TextInput,
  Title,
  Tooltip
} from "@mantine/core";
import {
  DataTable,
  type DataTableColumn
} from "@/components/data-table/DataTable";
import {
  IdentityProvider,
  IdentityProviderKind,
  IdentityProviderTestResult,
  createIdentityProvider,
  deleteIdentityProvider,
  listIdentityProviders,
  setIdentityProviderEnabled,
  testIdentityProvider,
  updateIdentityProvider
} from "@/api/identityProviders";

const QUERY_KEY = ["identity-providers"] as const;

// Provider, kind, secret, enabled, actions.
const COLUMN_WIDTHS = ["34%", "14%", "20%", "14%", "18%"];

const loadAll = () => listIdentityProviders();

type FormState = {
  kind: IdentityProviderKind;
  displayName: string;
  slug: string;
  oidcAuthority: string;
  oidcClientId: string;
  oidcScopes: string;
  samlEntityId: string;
  samlMetadataUrl: string;
  samlMetadataXml: string;
  samlSigningCertificate: string;
  secret: string;
};

const emptyForm: FormState = {
  kind: "oidc",
  displayName: "",
  slug: "",
  oidcAuthority: "",
  oidcClientId: "",
  oidcScopes: "",
  samlEntityId: "",
  samlMetadataUrl: "",
  samlMetadataXml: "",
  samlSigningCertificate: "",
  secret: ""
};

/**
 * Identity providers admin screen.
 *
 * Two things about this page are deliberate rather than incidental:
 *
 * A provider is created **disabled**. Adding a route into the system and
 * opening it should be two decisions, and the enable toggle in the table is
 * where the second one happens.
 *
 * The secret is write-only. The form never receives one back from the server —
 * it cannot, the API has no field for it — so editing shows whether a secret is
 * set and offers to replace it, and leaving the box empty on an edit keeps what
 * is stored rather than clearing it.
 */
export default function IdentityProvidersPage() {
  const queryClient = useQueryClient();
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<IdentityProvider | null>(null);
  const [form, setForm] = useState<FormState>(emptyForm);
  const [error, setError] = useState<string | null>(null);
  const [testResult, setTestResult] = useState<
    { id: string; result: IdentityProviderTestResult } | null
  >(null);

  // DataTable owns the query — it is a loading component, not a presentational
  // one, so it takes a loader and a queryKey rather than rows.
  const invalidate = () => queryClient.invalidateQueries({ queryKey: QUERY_KEY });

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (editing) {
        return updateIdentityProvider(editing.id, {
          displayName: form.displayName,
          oidcAuthority: form.oidcAuthority || null,
          oidcClientId: form.oidcClientId || null,
          oidcScopes: form.oidcScopes || null,
          samlEntityId: form.samlEntityId || null,
          samlMetadataUrl: form.samlMetadataUrl || null,
          samlMetadataXml: form.samlMetadataXml || null,
          samlSigningCertificate: form.samlSigningCertificate || null,
          // Omitted entirely when blank: an edit that does not touch the
          // secret must not clear it.
          ...(form.secret ? { secret: form.secret } : {})
        });
      }
      return createIdentityProvider({
        kind: form.kind,
        displayName: form.displayName,
        slug: form.slug || undefined,
        oidcAuthority: form.oidcAuthority || null,
        oidcClientId: form.oidcClientId || null,
        oidcScopes: form.oidcScopes || null,
        samlEntityId: form.samlEntityId || null,
        samlMetadataUrl: form.samlMetadataUrl || null,
        samlMetadataXml: form.samlMetadataXml || null,
        samlSigningCertificate: form.samlSigningCertificate || null,
        secret: form.secret || null
      });
    },
    onSuccess: () => {
      setModalOpen(false);
      setError(null);
      void invalidate();
    },
    onError: (e: unknown) => setError(readError(e))
  });

  const enabledMutation = useMutation({
    mutationFn: ({ id, enabled }: { id: string; enabled: boolean }) =>
      setIdentityProviderEnabled(id, enabled),
    onSuccess: () => void invalidate(),
    onError: (e: unknown) => setError(readError(e))
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteIdentityProvider(id),
    onSuccess: () => void invalidate(),
    onError: (e: unknown) => setError(readError(e))
  });

  const testMutation = useMutation({
    mutationFn: (id: string) => testIdentityProvider(id),
    onSuccess: (result, id) => setTestResult({ id, result }),
    onError: (e: unknown) => setError(readError(e))
  });

  useEffect(() => {
    if (!modalOpen) {
      setForm(emptyForm);
      setEditing(null);
    }
  }, [modalOpen]);

  const openCreate = () => {
    setEditing(null);
    setForm(emptyForm);
    setError(null);
    setModalOpen(true);
  };

  const openEdit = (provider: IdentityProvider) => {
    setEditing(provider);
    setForm({
      kind: provider.kind,
      displayName: provider.displayName,
      slug: provider.slug,
      oidcAuthority: provider.oidcAuthority ?? "",
      oidcClientId: provider.oidcClientId ?? "",
      oidcScopes: provider.oidcScopes ?? "",
      samlEntityId: provider.samlEntityId ?? "",
      samlMetadataUrl: provider.samlMetadataUrl ?? "",
      // Inline metadata is not echoed back by the API — only whether it is
      // present — so the box starts empty and filling it replaces what is
      // stored.
      samlMetadataXml: "",
      samlSigningCertificate: provider.samlSigningCertificate ?? "",
      secret: ""
    });
    setError(null);
    setModalOpen(true);
  };

  const columns = useMemo<DataTableColumn<IdentityProvider>[]>(
    () => [
      {
        id: "displayName",
        accessorKey: "displayName",
        header: "Provider",
        cell: ({ row }) => (
          <Stack gap={2}>
            <Text fw={500}>{row.original.displayName}</Text>
            <Text size="xs" c="dimmed">
              /{row.original.slug}
            </Text>
          </Stack>
        )
      },
      {
        id: "kind",
        accessorKey: "kind",
        header: "Kind",
        cell: ({ row }) => (
          <Badge variant="light">{row.original.kind.toUpperCase()}</Badge>
        )
      },
      {
        id: "secret",
        header: "Secret",
        cell: ({ row }) =>
          row.original.hasSecret ? (
            <Tooltip label={row.original.secretFingerprint ?? ""}>
              <Badge variant="light" color="green">
                Set
              </Badge>
            </Tooltip>
          ) : (
            <Badge variant="light" color="gray">
              Not set
            </Badge>
          )
      },
      {
        id: "enabled",
        header: "Enabled",
        cell: ({ row }) => (
          <Switch
            checked={row.original.isEnabled}
            aria-label={`${row.original.isEnabled ? "Disable" : "Enable"} ${row.original.displayName}`}
            onChange={(event) =>
              enabledMutation.mutate({
                id: row.original.id,
                enabled: event.currentTarget.checked
              })
            }
          />
        )
      },
      {
        id: "actions",
        header: "",
        cell: ({ row }) => (
          <Group gap="xs" justify="flex-end" wrap="nowrap">
            <Tooltip label="Test configuration">
              <ActionIcon
                variant="subtle"
                aria-label={`Test ${row.original.displayName}`}
                loading={
                  testMutation.isPending && testMutation.variables === row.original.id
                }
                onClick={() => testMutation.mutate(row.original.id)}
              >
                <i className="fa fa-plug" aria-hidden="true" />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="Edit">
              <ActionIcon
                variant="subtle"
                aria-label={`Edit ${row.original.displayName}`}
                onClick={() => openEdit(row.original)}
              >
                <i className="fa fa-pen" aria-hidden="true" />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="Delete">
              <ActionIcon
                variant="subtle"
                color="red"
                aria-label={`Delete ${row.original.displayName}`}
                onClick={() => deleteMutation.mutate(row.original.id)}
              >
                <i className="fa fa-trash" aria-hidden="true" />
              </ActionIcon>
            </Tooltip>
          </Group>
        )
      }
    ],
    [deleteMutation, enabledMutation, testMutation]
  );

  const isOidc = form.kind === "oidc";

  return (
    <Stack gap="md">
      <Group justify="space-between" align="flex-start">
        <Stack gap={4}>
          <Title order={2}>Identity providers</Title>
          <Text size="sm" c="dimmed">
            Federated sign-in. A provider is created disabled — configure it,
            test it, then enable it.
          </Text>
        </Stack>
        <Button onClick={openCreate}>Add provider</Button>
      </Group>

      {error && (
        <Alert color="red" title="Something went wrong" withCloseButton onClose={() => setError(null)}>
          {error}
        </Alert>
      )}

      {testResult && (
        <Alert
          color={testResult.result.success ? "green" : "red"}
          title={testResult.result.success ? "Configuration looks usable" : "Configuration problem"}
          withCloseButton
          onClose={() => setTestResult(null)}
        >
          <Stack gap="xs">
            <Text size="sm">{testResult.result.summary}</Text>
            {testResult.result.findings.length > 0 && (
              <List size="sm" spacing={2}>
                {testResult.result.findings.map((f) => (
                  <List.Item key={f}>
                    <Code>{f}</Code>
                  </List.Item>
                ))}
              </List>
            )}
          </Stack>
        </Alert>
      )}

      <Card withBorder padding={0}>
        <DataTable<IdentityProvider>
          mode="client"
          loadAll={loadAll}
          queryKey={QUERY_KEY}
          columns={columns}
          rowKey={(r) => r.id}
          columnWidths={COLUMN_WIDTHS}
          searchPlaceholder="Search providers…"
          emptyMessage="No identity providers configured. Sign-in uses local accounts only."
          loadingMessage="Loading identity providers…"
          initialSort={[{ id: "displayName", desc: false }]}
          globalFilterFn={(r, search) => {
            const needle = search.toLowerCase();
            return (
              r.displayName.toLowerCase().includes(needle) ||
              r.slug.toLowerCase().includes(needle) ||
              r.kind.toLowerCase().includes(needle)
            );
          }}
        />
      </Card>

      <Modal
        opened={modalOpen}
        onClose={() => setModalOpen(false)}
        title={editing ? `Edit ${editing.displayName}` : "Add identity provider"}
        size="lg"
      >
        <form
          onSubmit={(event: FormEvent) => {
            event.preventDefault();
            saveMutation.mutate();
          }}
        >
          <Stack gap="sm">
            <NativeSelect
              label="Kind"
              description={
                editing
                  ? "The protocol cannot be changed after creation — the stored fields differ."
                  : undefined
              }
              disabled={!!editing}
              value={form.kind}
              onChange={(e) =>
                setForm((f) => ({
                  ...f,
                  kind: e.currentTarget.value as IdentityProviderKind
                }))
              }
              data={[
                { value: "oidc", label: "OpenID Connect" },
                { value: "saml", label: "SAML 2.0" }
              ]}
            />

            <TextInput
              label="Display name"
              description="Appears on the login page button."
              required
              value={form.displayName}
              onChange={(e) =>
                setForm((f) => ({ ...f, displayName: e.currentTarget.value }))
              }
            />

            {!editing && (
              <TextInput
                label="Slug"
                description="Used in the callback path. Derived from the display name when left blank."
                value={form.slug}
                onChange={(e) => setForm((f) => ({ ...f, slug: e.currentTarget.value }))}
              />
            )}

            {isOidc ? (
              <>
                <TextInput
                  label="Authority"
                  description="Issuer URL, or the full .well-known/openid-configuration URL."
                  value={form.oidcAuthority}
                  onChange={(e) =>
                    setForm((f) => ({ ...f, oidcAuthority: e.currentTarget.value }))
                  }
                />
                <TextInput
                  label="Client ID"
                  value={form.oidcClientId}
                  onChange={(e) =>
                    setForm((f) => ({ ...f, oidcClientId: e.currentTarget.value }))
                  }
                />
                <TextInput
                  label="Scopes"
                  description="Space-separated. Defaults are used when blank."
                  value={form.oidcScopes}
                  onChange={(e) =>
                    setForm((f) => ({ ...f, oidcScopes: e.currentTarget.value }))
                  }
                />
              </>
            ) : (
              <>
                <TextInput
                  label="IdP entity ID"
                  value={form.samlEntityId}
                  onChange={(e) =>
                    setForm((f) => ({ ...f, samlEntityId: e.currentTarget.value }))
                  }
                />
                <TextInput
                  label="Metadata URL"
                  description="Fetched when you test the configuration. Leave blank if pasting metadata below."
                  value={form.samlMetadataUrl}
                  onChange={(e) =>
                    setForm((f) => ({ ...f, samlMetadataUrl: e.currentTarget.value }))
                  }
                />
                <Textarea
                  label="Metadata XML"
                  description={
                    editing && editing.hasSamlMetadataXml
                      ? "Metadata is stored. Paste to replace it; leave blank to keep it."
                      : "Paste the IdP's metadata document if there is no URL to fetch."
                  }
                  autosize
                  minRows={3}
                  maxRows={8}
                  value={form.samlMetadataXml}
                  onChange={(e) =>
                    setForm((f) => ({ ...f, samlMetadataXml: e.currentTarget.value }))
                  }
                />
                <Textarea
                  label="Signing certificate"
                  description="Base64 certificate used to validate assertions."
                  autosize
                  minRows={2}
                  maxRows={6}
                  value={form.samlSigningCertificate}
                  onChange={(e) =>
                    setForm((f) => ({
                      ...f,
                      samlSigningCertificate: e.currentTarget.value
                    }))
                  }
                />
              </>
            )}

            <TextInput
              type="password"
              label={isOidc ? "Client secret" : "Provider secret"}
              description={
                editing
                  ? editing.hasSecret
                    ? `A secret is set (${editing.secretFingerprint}). Type a new one to replace it, or leave blank to keep it.`
                    : "No secret is set. Type one to add it."
                  : "Stored encrypted. It cannot be read back once saved."
              }
              value={form.secret}
              onChange={(e) => setForm((f) => ({ ...f, secret: e.currentTarget.value }))}
            />

            <Group justify="flex-end" mt="sm">
              <Button variant="default" onClick={() => setModalOpen(false)}>
                Cancel
              </Button>
              <Button type="submit" loading={saveMutation.isPending}>
                {editing ? "Save" : "Create"}
              </Button>
            </Group>
          </Stack>
        </form>
      </Modal>
    </Stack>
  );
}

function readError(error: unknown): string {
  const response = (error as { response?: { data?: { error?: string } } })?.response;
  return (
    response?.data?.error ??
    (error as Error)?.message ??
    "Unexpected error."
  );
}
