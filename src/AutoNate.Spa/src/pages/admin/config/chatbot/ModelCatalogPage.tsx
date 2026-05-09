import { FormEvent, useMemo, useState } from "react";
import { ColumnDef } from "@tanstack/react-table";
import { useMutation, useQueryClient } from "@tanstack/react-query";
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

  const columns = useMemo<ColumnDef<AgentModel>[]>(() => [
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
          <div className="data-table-row-actions">
            <button
              type="button"
              className="btn btn-icon"
              title="Edit model"
              aria-label={`Edit ${m.displayName}`}
              disabled={busy}
              onClick={(e) => { e.stopPropagation(); setEditing(fromRow(m)); }}
            >
              <i className="fa fa-pen"></i>
            </button>
            {canActivate && !m.isDefault && (
              <button
                type="button"
                className="btn btn-icon btn-icon-success"
                title="Set as default model"
                aria-label={`Set ${m.displayName} as default`}
                disabled={busy}
                onClick={(e) => { e.stopPropagation(); void runAction(m.id, () => setDefaultAgentModel(m.id)); }}
              >
                <i className="fa fa-star"></i>
              </button>
            )}
          </div>
        );
      }
    }
  ], [busyId]);

  return (
    <>
      <div className="page-head">
        <h1 className="page-header mb-1">Models</h1>
        <p className="page-head-copy">
          The catalogue of LLM models AutoNate can use. The agent loop reads
          context windows from this table to size history-trimming and
          summarization. The "default" model (per provider) is what chatbot
          conversations use when no explicit model is pinned. The "available"
          flag controls whether the agent can pick a model for autonomous
          task routing — routing parameters themselves are not yet exposed.
        </p>
      </div>

      {error && <div className="alert alert-danger">{error}</div>}

      {refreshSummary && (
        <div className="alert alert-info">
          <div className="fw-semibold mb-1">Refresh complete</div>
          {refreshSummary.providers.length === 0 && refreshSummary.skippedReasons.length === 0 && (
            <div>No providers polled.</div>
          )}
          {refreshSummary.providers.map((p) => (
            <div key={p.connectionId} className="small">
              <strong>{p.provider}</strong>:{" "}
              {p.error
                ? <span className="text-danger">{p.error}</span>
                : p.addedModelIds.length === 0
                  ? <>No new models ({p.providerModelCount} total upstream).</>
                  : <>Added {p.addedModelIds.length}: <code>{p.addedModelIds.join(", ")}</code></>}
            </div>
          ))}
          {refreshSummary.skippedReasons.map((reason, i) => (
            <div key={i} className="small text-muted">{reason}</div>
          ))}
          <button
            type="button"
            className="btn btn-sm btn-link p-0 mt-1"
            onClick={() => setRefreshSummary(null)}
          >
            Dismiss
          </button>
        </div>
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
          <button
            type="button"
            className="btn btn-icon"
            onClick={() => refreshMutation.mutate()}
            disabled={refreshMutation.isPending}
            title={refreshMutation.isPending
              ? "Refreshing from providers…"
              : "Refresh from providers"}
            aria-label="Refresh from providers"
          >
            <i className={`fa fa-arrows-rotate${refreshMutation.isPending ? " fa-spin" : ""}`} />
          </button>
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

  return (
    <>
      <div className="modal show d-block" role="dialog" aria-modal="true" tabIndex={-1}>
        <div className="modal-dialog modal-dialog-centered modal-lg">
          <div className="modal-content">
            <form onSubmit={onSubmit}>
              <div className="modal-header">
                <h5 className="modal-title">Edit model</h5>
                <button type="button" className="btn-close" onClick={onCancel} aria-label="Close" />
              </div>
              <div className="modal-body">
                <div className="row g-3">
                  <div className="col-md-6">
                    <label className="form-label">Model id</label>
                    <input type="text" readOnly className="form-control-plaintext" value={form.modelId} />
                  </div>
                  <div className="col-md-6">
                    <label className="form-label">Display name</label>
                    <input type="text" readOnly className="form-control-plaintext" value={form.displayName} />
                  </div>
                  <div className="col-md-4">
                    <label className="form-label">Provider</label>
                    <input type="text" readOnly className="form-control-plaintext" value={form.provider} />
                  </div>
                  <div className="col-md-4">
                    <label className="form-label">Context window (tokens)</label>
                    <input type="text" readOnly className="form-control-plaintext" value={form.contextWindowTokens} />
                  </div>
                  <div className="col-md-4">
                    <label className="form-label">Sort order</label>
                    <input type="text" readOnly className="form-control-plaintext" value={form.sortOrder} />
                  </div>
                  <div className="col-md-4">
                    <label className="form-label">Input cost / Mtok</label>
                    <input type="text" readOnly className="form-control-plaintext" value={form.inputCost || "—"} />
                  </div>
                  <div className="col-md-4">
                    <label className="form-label">Output cost / Mtok</label>
                    <input type="text" readOnly className="form-control-plaintext" value={form.outputCost || "—"} />
                  </div>
                  <div className="col-md-4">
                    <label className="form-label">Cost currency</label>
                    <input type="text" readOnly className="form-control-plaintext" value={form.costCurrency} />
                  </div>
                  <div className="col-md-4">
                    <label className="form-label">Cost published</label>
                    <input type="text" readOnly className="form-control-plaintext" value={form.costPublishedAtUtc || "—"} />
                  </div>
                  <div className="col-12">
                    <label className="form-label">Description / what it's good for</label>
                    <textarea
                      className="form-control"
                      rows={3}
                      value={form.description}
                      onChange={(e) => update({ description: e.target.value })}
                      placeholder="Describe what this model is good for…"
                    />
                    <small className="text-muted">
                      Description is the only admin-editable field — every other value is curated by the provider and refreshed via the toolbar.
                    </small>
                  </div>
                </div>

                {submitError && (
                  <div className="alert alert-danger mt-3">
                    {submitError.message ?? "Save failed."}
                  </div>
                )}
              </div>
              <div className="modal-footer">
                <button type="button" className="btn btn-secondary" onClick={onCancel} disabled={submitting}>
                  Cancel
                </button>
                <button type="submit" className="btn btn-primary" disabled={submitting}>
                  {submitting ? "Saving…" : "Save changes"}
                </button>
              </div>
            </form>
          </div>
        </div>
      </div>
      <div className="modal-backdrop show" />
    </>
  );
}
