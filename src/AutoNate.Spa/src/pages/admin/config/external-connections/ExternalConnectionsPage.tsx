import { FormEvent, useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ExternalConnection,
  ExternalConnectionMetadata,
  TestConnectionResult,
  createExternalConnection,
  deleteExternalConnection,
  listExternalConnections,
  setDefaultExternalConnection,
  testExternalConnection,
  updateExternalConnection
} from "@/api/externalConnections";

// One field a connector kind needs the admin to fill in (in addition to the
// always-present name/description/secret/enabled). Adding a new connector
// means adding a row to KINDS below — the modal renders these fields
// generically, so no other code touches the form.
type ConnectionFieldDef = {
  key: string;            // metadata.json key
  label: string;
  placeholder?: string;
  defaultValue?: string;
  hint?: string;          // small grey text under the input
};

type ConnectionKindDef = {
  value: string;
  label: string;
  // Helper text shown under the secret field. Defaults to a generic
  // "Stored encrypted; never echoed back." line.
  secretHint?: string;
  fields: ConnectionFieldDef[];
};

const KINDS: ConnectionKindDef[] = [
  {
    value: "LlmProvider:Anthropic",
    label: "Anthropic (Claude)",
    fields: [
      {
        key: "baseUrl",
        label: "Base URL",
        placeholder: "https://api.anthropic.com",
        defaultValue: "https://api.anthropic.com"
      },
      {
        key: "model",
        label: "Default model",
        placeholder: "claude-sonnet-4-6",
        defaultValue: "claude-sonnet-4-6",
        hint: "Conversations created against this connection use this model."
      }
    ]
  },
  {
    value: "LlmProvider:OpenAI",
    label: "OpenAI (GPT)",
    fields: [
      {
        key: "baseUrl",
        label: "Base URL",
        placeholder: "https://api.openai.com",
        defaultValue: "https://api.openai.com"
      },
      {
        key: "model",
        label: "Default model",
        placeholder: "gpt-4.1",
        defaultValue: "gpt-4.1",
        hint: "Conversations created against this connection use this model."
      }
    ]
  },
  {
    value: "WebSearchProvider:Tavily",
    label: "Tavily (web search)",
    fields: [
      {
        key: "baseUrl",
        label: "Base URL",
        placeholder: "https://api.tavily.com",
        defaultValue: "https://api.tavily.com",
        hint: "Override only if proxying through a custom endpoint."
      }
    ]
  }
];

type FormState = {
  id: string | null;
  kind: string;
  name: string;
  description: string;
  isEnabled: boolean;
  secret: string;
  // Field values keyed by ConnectionFieldDef.key. Empty strings are dropped
  // before submit so the backend metadata stays sparse.
  metadata: Record<string, string>;
};

function findKind(value: string): ConnectionKindDef | undefined {
  return KINDS.find((k) => k.value === value);
}

function defaultMetadataFor(kind: ConnectionKindDef): Record<string, string> {
  const out: Record<string, string> = {};
  for (const f of kind.fields) {
    if (f.defaultValue !== undefined) out[f.key] = f.defaultValue;
  }
  return out;
}

function makeEmptyForm(kindValue: string = KINDS[0].value): FormState {
  const kind = findKind(kindValue) ?? KINDS[0];
  return {
    id: null,
    kind: kind.value,
    name: "",
    description: "",
    isEnabled: true,
    secret: "",
    metadata: defaultMetadataFor(kind)
  };
}

export function ExternalConnectionsPage() {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<FormState | null>(null);
  const [testResults, setTestResults] = useState<Record<string, TestConnectionResult>>({});

  const listQuery = useQuery({
    queryKey: ["external-connections", "list"],
    queryFn: ({ signal }) => listExternalConnections(undefined, signal)
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["external-connections", "list"] });

  const createMutation = useMutation({
    mutationFn: createExternalConnection,
    onSuccess: () => { invalidate(); setEditing(null); }
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, ...rest }: { id: string } & Parameters<typeof updateExternalConnection>[1]) =>
      updateExternalConnection(id, rest),
    onSuccess: () => { invalidate(); setEditing(null); }
  });

  const deleteMutation = useMutation({
    mutationFn: deleteExternalConnection,
    onSuccess: invalidate
  });

  const setDefaultMutation = useMutation({
    mutationFn: setDefaultExternalConnection,
    onSuccess: invalidate
  });

  const testMutation = useMutation({
    mutationFn: testExternalConnection,
    onSuccess: (result, id) => {
      setTestResults((prev) => ({ ...prev, [id]: result }));
    }
  });

  const startNew = () => setEditing(makeEmptyForm());

  const startEdit = (row: ExternalConnection) => {
    const kind = findKind(row.kind);
    // Pre-populate every field this kind declares from the persisted
    // metadata; unknown / missing values become empty strings.
    const metadata: Record<string, string> = {};
    if (kind) {
      for (const f of kind.fields) {
        const v = row.metadata?.[f.key];
        metadata[f.key] = typeof v === "string" ? v : "";
      }
    }
    setEditing({
      id: row.id,
      kind: row.kind,
      name: row.name,
      description: row.description ?? "",
      isEnabled: row.isEnabled,
      secret: "",
      metadata
    });
  };

  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (!editing) return;

    // Drop empty strings so a blank field doesn't store metadata noise like
    // baseUrl: "". The backend just sees the keys the kind needs.
    const metadata: ExternalConnectionMetadata = {};
    for (const [k, v] of Object.entries(editing.metadata)) {
      if (v !== "") metadata[k] = v;
    }

    if (editing.id) {
      updateMutation.mutate({
        id: editing.id,
        name: editing.name,
        description: editing.description || null,
        isEnabled: editing.isEnabled,
        metadata,
        // Omit when blank so existing secret stays untouched.
        secret: editing.secret.length > 0 ? editing.secret : undefined
      });
    } else {
      createMutation.mutate({
        kind: editing.kind,
        name: editing.name,
        description: editing.description || null,
        isEnabled: editing.isEnabled,
        metadata,
        secret: editing.secret || undefined
      });
    }
  };

  return (
    <div className="panel panel-inverse">
      <div className="panel-heading">
        <h4 className="panel-title">External Connections</h4>
        <button type="button" className="btn btn-sm btn-primary ms-auto" onClick={startNew}>
          <i className="fa fa-plus me-1" /> New connection
        </button>
      </div>
      <div className="panel-body">
        {listQuery.isLoading && <p>Loading…</p>}
        {listQuery.isError && <p className="text-danger">Failed to load connections.</p>}
        {listQuery.data && listQuery.data.length === 0 && (
          <p className="text-muted">No external connections yet. Add one to wire an LLM or search provider into the agent.</p>
        )}
        {listQuery.data && listQuery.data.length > 0 && (
          <div className="table-responsive">
            <table className="table table-striped align-middle">
              <thead>
                <tr>
                  <th>Kind</th>
                  <th>Name</th>
                  <th>Default</th>
                  <th>Enabled</th>
                  <th>Secret</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {listQuery.data.map((row) => (
                  <tr key={row.id}>
                    <td><code>{row.kind}</code></td>
                    <td>
                      <div className="fw-semibold">{row.name}</div>
                      {row.description && <div className="text-muted small">{row.description}</div>}
                      {typeof row.metadata?.model === "string" && row.metadata.model !== "" && (
                        <div className="text-muted small">Model: {row.metadata.model}</div>
                      )}
                    </td>
                    <td>
                      {row.isDefault ? (
                        <span className="badge bg-success">Default</span>
                      ) : (
                        <button
                          type="button"
                          className="btn btn-sm btn-outline-secondary"
                          onClick={() => setDefaultMutation.mutate(row.id)}
                          disabled={setDefaultMutation.isPending}
                        >
                          Set default
                        </button>
                      )}
                    </td>
                    <td>
                      {row.isEnabled ? (
                        <span className="badge bg-primary">Enabled</span>
                      ) : (
                        <span className="badge bg-secondary">Disabled</span>
                      )}
                    </td>
                    <td>
                      {row.secretFingerprint
                        ? <code className="small">{row.secretFingerprint}</code>
                        : <span className="text-warning small">No secret</span>}
                    </td>
                    <td>
                      <div className="btn-group btn-group-sm">
                        <button type="button" className="btn btn-outline-primary" onClick={() => startEdit(row)}>
                          Edit
                        </button>
                        <button
                          type="button"
                          className="btn btn-outline-secondary"
                          onClick={() => testMutation.mutate(row.id)}
                          disabled={testMutation.isPending && testMutation.variables === row.id}
                        >
                          Test
                        </button>
                        <button
                          type="button"
                          className="btn btn-outline-danger"
                          onClick={() => {
                            if (window.confirm(`Delete "${row.name}"?`)) {
                              deleteMutation.mutate(row.id);
                            }
                          }}
                        >
                          Delete
                        </button>
                      </div>
                      {testResults[row.id] && (
                        <div className={`small mt-1 ${testResults[row.id].ok ? "text-success" : "text-danger"}`}>
                          {testResults[row.id].ok
                            ? `OK (${testResults[row.id].latencyMs}ms${testResults[row.id].modelEcho ? `, ${testResults[row.id].modelEcho}` : ""})`
                            : `Error: ${testResults[row.id].error}`}
                        </div>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {editing && (
        <ConnectionFormModal
          form={editing}
          onChange={setEditing}
          onSubmit={submit}
          onCancel={() => setEditing(null)}
          submitting={createMutation.isPending || updateMutation.isPending}
          submitError={(createMutation.error ?? updateMutation.error) as Error | null}
        />
      )}
    </div>
  );
}

type ConnectionFormModalProps = {
  form: FormState;
  onChange: (next: FormState) => void;
  onSubmit: (e: FormEvent<HTMLFormElement>) => void;
  onCancel: () => void;
  submitting: boolean;
  submitError: Error | null;
};

function ConnectionFormModal({ form, onChange, onSubmit, onCancel, submitting, submitError }: ConnectionFormModalProps) {
  const kindDef = findKind(form.kind);

  // When creating a new row, switching the kind dropdown resets the
  // dynamic-field values to that kind's defaults (so the admin doesn't
  // have to remember API URLs by hand). Edits never re-key, so we
  // skip this for existing rows.
  useEffect(() => {
    if (form.id !== null) return;
    if (!kindDef) return;
    onChange({ ...form, metadata: defaultMetadataFor(kindDef) });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [form.kind]);

  const update = (patch: Partial<FormState>) => onChange({ ...form, ...patch });
  const updateField = (fieldKey: string, value: string) =>
    onChange({ ...form, metadata: { ...form.metadata, [fieldKey]: value } });

  return (
    <>
      <div className="modal show d-block" role="dialog" aria-modal="true" tabIndex={-1}>
        <div className="modal-dialog modal-dialog-centered">
          <div className="modal-content">
            <form onSubmit={onSubmit}>
              <div className="modal-header">
                <h5 className="modal-title">{form.id ? "Edit connection" : "New connection"}</h5>
                <button type="button" className="btn-close" onClick={onCancel} aria-label="Close" />
              </div>
              <div className="modal-body">
                <div className="mb-3">
                  <label className="form-label">Kind</label>
                  <select
                    className="form-select"
                    value={form.kind}
                    onChange={(e) => update({ kind: e.target.value })}
                    disabled={form.id !== null}
                  >
                    {KINDS.map((k) => (
                      <option key={k.value} value={k.value}>{k.label}</option>
                    ))}
                  </select>
                  {form.id !== null && (
                    <small className="text-muted">Kind is locked once a connection exists.</small>
                  )}
                </div>

                <div className="mb-3">
                  <label className="form-label">Name</label>
                  <input
                    className="form-control"
                    value={form.name}
                    onChange={(e) => update({ name: e.target.value })}
                    placeholder="e.g. Production Anthropic"
                    required
                  />
                </div>

                <div className="mb-3">
                  <label className="form-label">Description</label>
                  <input
                    className="form-control"
                    value={form.description}
                    onChange={(e) => update({ description: e.target.value })}
                    placeholder="Optional"
                  />
                </div>

                {(kindDef?.fields ?? []).map((field) => (
                  <div className="mb-3" key={field.key}>
                    <label className="form-label">{field.label}</label>
                    <input
                      className="form-control"
                      value={form.metadata[field.key] ?? ""}
                      onChange={(e) => updateField(field.key, e.target.value)}
                      placeholder={field.placeholder}
                    />
                    {field.hint && <small className="text-muted">{field.hint}</small>}
                  </div>
                ))}

                <div className="mb-3">
                  <label className="form-label">API key</label>
                  <input
                    type="password"
                    className="form-control"
                    value={form.secret}
                    onChange={(e) => update({ secret: e.target.value })}
                    placeholder={form.id ? "Leave blank to keep existing" : "sk-…"}
                    autoComplete="off"
                  />
                  <small className="text-muted">
                    {kindDef?.secretHint ?? "Stored encrypted via DataProtection. Never echoed back."}
                  </small>
                </div>

                <div className="form-check">
                  <input
                    type="checkbox"
                    id="connection-enabled"
                    className="form-check-input"
                    checked={form.isEnabled}
                    onChange={(e) => update({ isEnabled: e.target.checked })}
                  />
                  <label className="form-check-label" htmlFor="connection-enabled">Enabled</label>
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
                  {submitting ? "Saving…" : form.id ? "Save changes" : "Create"}
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
