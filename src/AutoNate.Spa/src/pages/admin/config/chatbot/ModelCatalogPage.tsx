import { FormEvent, useMemo, useState } from "react";
import type { DataTableColumn } from "@/components/data-table/DataTable";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Alert,
  Anchor,
  Box,
  Button,
  Code,
  Grid,
  Group,
  Modal,
  Text,
  TextInput,
  Textarea,
  Title
} from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import {
  AgentModel,
  RefreshResult,
  listAgentModels,
  refreshAgentModelCatalog,
  setAgentModelAvailable,
  setAgentModelUnavailable,
  setDefaultAgentModel,
  updateAgentModel
} from "@/api/agentModels";
import { DataTable } from "@/components/data-table/DataTable";
import { ProviderLogo } from "@/components/agent/ProviderLogo";

const COLUMN_WIDTHS = ["22%", "6%", "8%", "14%", "32%", "10%", "8%"];

function formatTokens(tokens: number): string {
  if (tokens >= 1_000_000) return `${(tokens / 1_000_000).toFixed(tokens % 1_000_000 === 0 ? 0 : 1)}M`;
  if (tokens >= 1_000) return `${Math.round(tokens / 1_000)}K`;
  return tokens.toString();
}

function formatCost(value: number | null, currency: string): string {
  if (value === null || value === undefined) return "—";
  const symbol = currency === "USD" ? "$" : `${currency} `;
  return `${symbol}${value.toFixed(2)} / Mtok`;
}

type FormState = {
  id: string | null;
  modelId: string;
  displayName: string;
  provider: string;
  contextWindowTokens: string;
  inputCost: string;
  outputCost: string;
  costCurrency: string;
  costPublishedAtUtc: string;
  description: string;
  sortOrder: string;
};

function fromRow(row: AgentModel): FormState {
  return {
    id: row.id,
    modelId: row.modelId,
    displayName: row.displayName,
    provider: row.provider,
    contextWindowTokens: row.contextWindowTokens.toString(),
    inputCost: row.inputCostPerMillionTokens?.toString() ?? "",
    outputCost: row.outputCostPerMillionTokens?.toString() ?? "",
    costCurrency: row.costCurrency,
    costPublishedAtUtc: row.costPublishedAtUtc ? row.costPublishedAtUtc.slice(0, 10) : "",
    description: row.description ?? "",
    sortOrder: row.sortOrder.toString()
  };
}

export default function ModelCatalogPage() {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<FormState | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [refreshSummary, setRefreshSummary] = useState<RefreshResult | null>(null);

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: ["admin", "agent-models"] });

  const updateMutation = useMutation({
    mutationFn: ({ id, ...rest }: { id: string } & Parameters<typeof updateAgentModel>[1]) => updateAgentModel(id, rest),
    onSuccess: () => { invalidate(); setEditing(null); }
  });
  const refreshMutation = useMutation({
    mutationFn: refreshAgentModelCatalog,
    onSuccess: (result) => { setRefreshSummary(result); invalidate(); },
    onError: (err: Error) => setError(err.message ?? "Refresh failed.")
  });

  const runAction = async (id: string, action: () => Promise<unknown>) => {
    setError(null);
    setBusyId(id);
    try {
      await action();
      invalidate();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setBusyId(null);
    }
  };

  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (!editing || !editing.id) return;
    // Only the description is admin-editable. Everything else is
    // provider-curated metadata refreshed via /refresh, so the PUT body
    // is intentionally narrow.
    updateMutation.mutate({
      id: editing.id,
      description: editing.description.trim() || null
    });
  };

  const columns = useMemo<DataTableColumn<AgentModel>[]>(() => [
    {
      id: "displayName",
      accessorKey: "displayName",
      header: "Model",
      cell: ({ row }) => {
        const m = row.original;
        return (
          <>
            <div className="fw-semibold">
              {m.displayName}
              {m.isDefault && (
                <span className="badge bg-success ms-2">Default</span>
              )}
            </div>
            <div className="small text-muted"><code>{m.modelId}</code></div>
          </>
        );
      }
    },
    {
      id: "provider",
      accessorKey: "provider",
      header: "Provider",
      cell: ({ row }) => <ProviderLogo provider={row.original.provider} />
    },
    {
      id: "contextWindowTokens",
      accessorKey: "contextWindowTokens",
      header: "Context",
      cell: ({ row }) => formatTokens(row.original.contextWindowTokens)
    },
    {
      id: "cost",
      // Sort on input cost — most common comparison axis. Nulls sink to
      // the bottom regardless of direction by mapping them to a sentinel
      // value bigger than any realistic price (no real model is over
      // $10K / Mtok).
      accessorFn: (m) => m.inputCostPerMillionTokens ?? Number.POSITIVE_INFINITY,
      header: "Cost",
      cell: ({ row }) => {
        const m = row.original;
        const hasInput = m.inputCostPerMillionTokens !== null && m.inputCostPerMillionTokens !== undefined;
        const hasOutput = m.outputCostPerMillionTokens !== null && m.outputCostPerMillionTokens !== undefined;
        if (!hasInput && !hasOutput && !m.costPublishedAtUtc) {
          return <span className="text-muted">—</span>;
        }
        return (
          <div className="small">
            <div>Input: {formatCost(m.inputCostPerMillionTokens, m.costCurrency)}</div>
            <div>Output: {formatCost(m.outputCostPerMillionTokens, m.costCurrency)}</div>
            {m.costPublishedAtUtc && (
              <div className="text-muted">as of {m.costPublishedAtUtc.slice(0, 10)}</div>
            )}
          </div>
        );
      }
    },
    {
      id: "description",
      accessorKey: "description",
      header: "Description",
      enableSorting: false,
      meta: { wrap: true },
      cell: ({ row }) => (
        <span className="small">
          {row.original.description ?? <span className="text-muted">—</span>}
        </span>
      )
    },
    {
      id: "agentUse",
      // Sort: no-connection rows sink, then off, then on. Picking distinct
      // numeric buckets keeps the sort stable across the discrete states.
      accessorFn: (m) => !m.providerHasConnection ? 0 : (m.isAvailable ? 2 : 1),
      header: "Agent Use",
      cell: ({ row }) => {
        const m = row.original;
        const busy = busyId === m.id;
        if (!m.providerHasConnection) {
          return (
            <span
              className="badge bg-light text-dark border"
              title={`No External Connection configured for ${m.provider} — model can't be used by the chatbot`}
            >
              N/A
            </span>
          );
        }
        const switchId = `agent-use-${m.id}`;
        return (
          <div className="form-check form-switch mb-0" onClick={(e) => e.stopPropagation()}>
            <input
              id={switchId}
              type="checkbox"
              role="switch"
              className="form-check-input"
              checked={m.isAvailable}
              disabled={busy}
              onChange={(e) => {
                const next = e.target.checked;
                void runAction(
                  m.id,
                  () => next ? setAgentModelAvailable(m.id) : setAgentModelUnavailable(m.id)
                );
              }}
              aria-label={m.isAvailable ? "Disable agent use" : "Enable agent use"}
            />
          </div>
        );
      }
    },
    {
      id: "actions",
      header: "Actions",
      enableSorting: false,
      enableGlobalFilter: false,
      cell: ({ row }) => {
        const m = row.original;
        const busy = busyId === m.id;
        // Models whose provider has no enabled connection can't be set as
        // default — that affordance is hidden entirely so admins aren't
        // tempted to click a button the server will reject. Available
        // toggling moved to the Agent Use column's switch.
        const canActivate = m.providerHasConnection;
        return (
          <Group gap="xs">
            <ActionIcon
              variant="subtle"
              color="gray"
              size="sm"
              title="Edit model"
              aria-label={`Edit ${m.displayName}`}
              disabled={busy}
              onClick={(e) => {
                e.stopPropagation();
                setEditing(fromRow(m));
              }}
            >
              <i className="fa fa-pen" />
            </ActionIcon>
            {canActivate && !m.isDefault && (
              <ActionIcon
                variant="subtle"
                color="green"
                size="sm"
                title="Set as default model"
                aria-label={`Set ${m.displayName} as default`}
                disabled={busy}
                onClick={(e) => {
                  e.stopPropagation();
                  void runAction(m.id, () => setDefaultAgentModel(m.id));
                }}
              >
                <i className="fa fa-star" />
              </ActionIcon>
            )}
          </Group>
        );
      }
    }
  ], [busyId]);

  return (
    <>
      <PageHeader
        title="Models"
        description={
          <>
            The catalogue of LLM models AutoNate can use. The agent loop reads context windows from
            this table to size history-trimming and summarization. The &quot;default&quot; model
            (per provider) is what chatbot conversations use when no explicit model is pinned. The
            &quot;available&quot; flag controls whether the agent can pick a model for autonomous
            task routing — routing parameters themselves are not yet exposed.
          </>
        }
      />

      {error && (
        <Alert color="red" variant="light" mb="md">
          {error}
        </Alert>
      )}

      {refreshSummary && (
        <Alert color="blue" variant="light" mb="md">
          <Text fw={600} mb={4}>
            Refresh complete
          </Text>
          {refreshSummary.providers.length === 0 && refreshSummary.skippedReasons.length === 0 && (
            <Text size="sm">No providers polled.</Text>
          )}
          {refreshSummary.providers.map((p) => (
            <Text key={p.connectionId} size="sm">
              <strong>{p.provider}</strong>:{" "}
              {p.error ? (
                <Text component="span" c="red">
                  {p.error}
                </Text>
              ) : p.addedModelIds.length === 0 ? (
                <>No new models ({p.providerModelCount} total upstream).</>
              ) : (
                <>
                  Added {p.addedModelIds.length}: <Code>{p.addedModelIds.join(", ")}</Code>
                </>
              )}
            </Text>
          ))}
          {refreshSummary.skippedReasons.map((reason, i) => (
            <Text key={i} size="sm" c="dimmed">
              {reason}
            </Text>
          ))}
          <Anchor
            component="button"
            type="button"
            size="sm"
            mt={4}
            onClick={() => setRefreshSummary(null)}
          >
            Dismiss
          </Anchor>
        </Alert>
      )}

      <DataTable<AgentModel>
        mode="client"
        loadAll={() => listAgentModels()}
        queryKey={["admin", "agent-models"]}
        columns={columns}
        rowKey={(m) => m.id}
        columnWidths={COLUMN_WIDTHS}
        initialSort={[{ id: "provider", desc: false }]}
        searchPlaceholder="Search models…"
        emptyMessage="No models catalogued yet. Configure an LLM External Connection, then click 'Refresh from providers' to import their model list."
        loadingMessage="Loading models…"
        globalFilterFn={(m, search) => {
          const needle = search.toLowerCase();
          return `${m.displayName} ${m.modelId} ${m.provider} ${m.description ?? ""}`.toLowerCase().includes(needle);
        }}
        toolbarBeforeSearch={
          <ActionIcon
            variant="subtle"
            color="gray"
            size="sm"
            onClick={() => refreshMutation.mutate()}
            disabled={refreshMutation.isPending}
            title={refreshMutation.isPending
              ? "Refreshing from providers…"
              : "Refresh from providers"}
            aria-label="Refresh from providers"
          >
            <i className={`fa fa-arrows-rotate${refreshMutation.isPending ? " fa-spin" : ""}`} />
          </ActionIcon>
        }
      />

      {editing && (
        <ModelEditModal
          form={editing}
          onChange={setEditing}
          onSubmit={submit}
          onCancel={() => setEditing(null)}
          submitting={updateMutation.isPending}
          submitError={updateMutation.error as Error | null}
        />
      )}
    </>
  );
}

type ModelEditModalProps = {
  form: FormState;
  onChange: (next: FormState) => void;
  onSubmit: (e: FormEvent<HTMLFormElement>) => void;
  onCancel: () => void;
  submitting: boolean;
  submitError: Error | null;
};

function ModelEditModal({ form, onChange, onSubmit, onCancel, submitting, submitError }: ModelEditModalProps) {
  const update = (patch: Partial<FormState>) => onChange({ ...form, ...patch });

  const readOnlyStyles = { input: { background: "transparent", border: 0, padding: 0 } } as const;
  return (
    <Modal opened onClose={onCancel} title="Edit model" size="lg" centered>
      <Box component="form" onSubmit={onSubmit}>
        <Grid>
          <Grid.Col span={{ base: 12, md: 6 }}>
            <TextInput label="Model id" readOnly value={form.modelId} styles={readOnlyStyles} />
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 6 }}>
            <TextInput
              label="Display name"
              readOnly
              value={form.displayName}
              styles={readOnlyStyles}
            />
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 4 }}>
            <TextInput label="Provider" readOnly value={form.provider} styles={readOnlyStyles} />
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 4 }}>
            <TextInput
              label="Context window (tokens)"
              readOnly
              value={form.contextWindowTokens}
              styles={readOnlyStyles}
            />
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 4 }}>
            <TextInput label="Sort order" readOnly value={form.sortOrder} styles={readOnlyStyles} />
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 4 }}>
            <TextInput
              label="Input cost / Mtok"
              readOnly
              value={form.inputCost || "—"}
              styles={readOnlyStyles}
            />
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 4 }}>
            <TextInput
              label="Output cost / Mtok"
              readOnly
              value={form.outputCost || "—"}
              styles={readOnlyStyles}
            />
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 4 }}>
            <TextInput
              label="Cost currency"
              readOnly
              value={form.costCurrency}
              styles={readOnlyStyles}
            />
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 4 }}>
            <TextInput
              label="Cost published"
              readOnly
              value={form.costPublishedAtUtc || "—"}
              styles={readOnlyStyles}
            />
          </Grid.Col>
          <Grid.Col span={12}>
            <Textarea
              label="Description / what it's good for"
              rows={3}
              value={form.description}
              onChange={(e) => update({ description: e.currentTarget.value })}
              placeholder="Describe what this model is good for…"
              description="Description is the only admin-editable field — every other value is curated by the provider and refreshed via the toolbar."
            />
          </Grid.Col>
        </Grid>

        {submitError && (
          <Alert color="red" variant="light" mt="md">
            {submitError.message ?? "Save failed."}
          </Alert>
        )}

        <Group justify="flex-end" mt="md" gap="xs">
          <Button variant="default" onClick={onCancel} disabled={submitting}>
            Cancel
          </Button>
          <Button type="submit" loading={submitting}>
            Save changes
          </Button>
        </Group>
      </Box>
    </Modal>
  );
}
