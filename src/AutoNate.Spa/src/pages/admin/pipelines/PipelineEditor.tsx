import { useCallback, useEffect, useMemo, useState } from "react";
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  ReactFlowProvider,
  addEdge,
  useEdgesState,
  useNodesState,
  type Connection,
  type Edge,
  type Node,
  type NodeProps,
  type NodeTypes
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Badge,
  Box,
  Button,
  Group,
  Modal,
  NativeSelect,
  NumberInput,
  Paper,
  Select,
  Stack,
  Switch,
  Text,
  Textarea,
  TextInput,
  Title,
  Tooltip
} from "@mantine/core";
import { notifications } from "@mantine/notifications";
import {
  PipelineEdge as PipelineEdgeShape,
  PipelineGraph,
  PipelineNode as PipelineNodeShape,
  getPipeline,
  runPipeline,
  updatePipeline
} from "@/api/pipelines";
import {
  ConfigFieldSchema,
  TransformerConfigSchema,
  getTransformerSchema,
  listTransformers
} from "@/api/transformers";
import { getAnalyzerSchema, listAnalyzers } from "@/api/analyzers";
import { listCodeTransformers } from "@/api/codeTransformers";
import { listDatasets } from "@/api/datasets";
import CronExpressionBuilder from "@/components/CronExpressionBuilder";

// Per-node form data shape stored in `node.data`. The graph round-trip
// keeps these fields in sync with the backend PipelineNode shape; React
// Flow keeps positions in `node.position`. React Flow's `NodeProps<T>`
// constrains `T` to extend `Record<string, unknown>`, so the index
// signature on this type is load-bearing — without it the
// `NodeProps<Node<PipelineNodeData>>` constraint check fails.
type PipelineNodeData = {
  label: string;
  kind: PipelineNodeShape["kind"];
  key: string;
  config: Record<string, string>;
  [key: string]: unknown;
};

type PipelineFlowNode = Node<PipelineNodeData>;

const NODE_KIND_LABELS: Record<PipelineNodeShape["kind"], string> = {
  "dataset-source": "Dataset source",
  transformer: "Transformer",
  analyzer: "Analyzer",
  "dataset-sink": "Dataset sink"
};

// Phase 5 v1 node UI is one form per node — a card with the kind name,
// a key (dataset name / transformer key / analyzer key), and a JSON-as-
// key/value config blob. The Phase 5.1 follow-up surfaces kind-specific
// schemas from /api/transformers/{key}/schema; today the editor is
// generic so the React Flow plumbing lands without per-kind forms.
function NodeCard({ data }: NodeProps<PipelineFlowNode>) {
  return (
    <Paper p="xs" withBorder shadow="xs" style={{ minWidth: 180, background: "var(--mantine-color-body)" }}>
      <Stack gap={4}>
        <Group gap="xs" wrap="nowrap">
          <Text size="xs" c="dimmed">{NODE_KIND_LABELS[data.kind]}</Text>
        </Group>
        <Text fw={500} size="sm">{data.label || data.key || "(unnamed)"}</Text>
      </Stack>
    </Paper>
  );
}

// React Flow's `NodeTypes` is a string→component map; `as const` would
// over-narrow the keys away from string. Plain `{...}` keeps it
// assignable to `NodeTypes`.
const NODE_TYPES: NodeTypes = {
  "dataset-source": NodeCard,
  transformer: NodeCard,
  analyzer: NodeCard,
  "dataset-sink": NodeCard
};

function makeNodeId() {
  return `node_${Math.random().toString(36).slice(2, 10)}`;
}

function decodeGraph(json: string): { nodes: Node<PipelineNodeData>[]; edges: Edge[] } {
  let parsed: PipelineGraph;
  try {
    parsed = JSON.parse(json);
  } catch {
    parsed = { nodes: [], edges: [] };
  }
  const nodes = (parsed.nodes ?? []).map<Node<PipelineNodeData>>((n) => ({
    id: n.id,
    type: n.kind,
    position: n.position ?? { x: 0, y: 0 },
    data: {
      label: n.key,
      kind: n.kind,
      key: n.key,
      config: (n.config as Record<string, string>) ?? {}
    }
  }));
  const edges = (parsed.edges ?? []).map<Edge>((e) => ({
    id: e.id,
    source: e.source,
    target: e.target
  }));
  return { nodes, edges };
}

function encodeGraph(nodes: Node<PipelineNodeData>[], edges: Edge[]): PipelineGraph {
  return {
    nodes: nodes.map<PipelineNodeShape>((n) => ({
      id: n.id,
      kind: n.data.kind,
      key: n.data.key,
      config: n.data.config && Object.keys(n.data.config).length > 0 ? n.data.config : null,
      position: { x: n.position?.x ?? 0, y: n.position?.y ?? 0 }
    })),
    edges: edges.map<PipelineEdgeShape>((e) => ({
      id: e.id,
      source: e.source,
      target: e.target
    }))
  };
}

export default function PipelineEditor() {
  return (
    <ReactFlowProvider>
      <PipelineEditorInner />
    </ReactFlowProvider>
  );
}

// Schema-driven config form for a transformer / analyzer node (audit
// fix archived-7). Each field renders to the Mantine control the schema's
// `type` declares; the value flows back as a string so the runtime
// can read it through its existing IReadOnlyDictionary<string, string>
// surface unchanged. Defaults are shown in placeholder text rather
// than auto-written into the config dict, so an unset field stays
// absent (the backend OptionalConfig(...) ?? default kicks in).
function SchemaFormFields({
  schema,
  config,
  onChange
}: {
  schema: TransformerConfigSchema;
  config: Record<string, string>;
  onChange: (name: string, value: string) => void;
}) {
  return (
    <Stack gap="xs" aria-label="Node config fields">
      {schema.fields.length === 0 ? (
        <Text size="xs" c="dimmed">
          {schema.displayName} takes no configuration.
        </Text>
      ) : null}
      {schema.fields.map((field) => (
        <SchemaField
          key={field.name}
          field={field}
          value={config[field.name] ?? ""}
          onChange={(v) => onChange(field.name, v)}
        />
      ))}
    </Stack>
  );
}

function SchemaField({
  field,
  value,
  onChange
}: {
  field: ConfigFieldSchema;
  value: string;
  onChange: (next: string) => void;
}) {
  const description = field.description ?? undefined;
  const requiredAsterisk = field.required ? ` *` : "";
  const labelText = `${field.label}${requiredAsterisk}`;
  const placeholder =
    field.placeholder ?? (field.defaultValue ? `default: ${field.defaultValue}` : undefined);

  if (field.type === "select" && field.options) {
    return (
      <NativeSelect
        label={labelText}
        description={description}
        data={field.options.map((opt) => ({ value: opt, label: opt }))}
        value={value === "" ? field.defaultValue ?? field.options[0] ?? "" : value}
        onChange={(e) => onChange(e.currentTarget.value)}
      />
    );
  }

  if (field.type === "boolean") {
    // Backend reads booleans as "false" being the literal opt-out and
    // anything-else (including absent) being true. Store the explicit
    // string so the user's choice round-trips through JSON unchanged.
    const checked = value === "" ? field.defaultValue !== "false" : value !== "false";
    return (
      <Switch
        label={labelText}
        description={description}
        checked={checked}
        onChange={(e) => onChange(e.currentTarget.checked ? "true" : "false")}
      />
    );
  }

  if (field.type === "number") {
    const numeric = value === "" ? "" : Number(value);
    return (
      <NumberInput
        label={labelText}
        description={description}
        placeholder={placeholder}
        value={Number.isFinite(numeric) ? (numeric as number) : ""}
        onChange={(v) => onChange(v === "" || v === null ? "" : String(v))}
      />
    );
  }

  // text + columns both render as a TextInput. "columns" is a hint to
  // the user (comma-separated list) — backend's SplitColumnList handles
  // the parse. No client-side validation beyond required-check.
  return (
    <TextInput
      label={labelText}
      description={description}
      placeholder={placeholder}
      value={value}
      onChange={(e) => onChange(e.currentTarget.value)}
    />
  );
}

function PipelineEditorInner() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const pipelineQuery = useQuery({
    queryKey: ["pipeline", id],
    queryFn: ({ signal }) => getPipeline(id!, signal),
    enabled: !!id
  });

  const transformersQuery = useQuery({
    queryKey: ["transformers", "list"],
    queryFn: ({ signal }) => listTransformers(signal)
  });

  const analyzersQuery = useQuery({
    queryKey: ["analyzers", "list"],
    queryFn: ({ signal }) => listAnalyzers(signal)
  });

  // Phase 6 code transformers — user-authored JS / Python that run in the
  // `services/executor/` sidecar. The backend's TransformerNodeRunner /
  // AnalyzerNodeRunner resolves a node's `key` against the built-in registry
  // first and falls through to the code-transformer store by name; without
  // this list in the dropdown, a code transformer authored on
  // /code-transformers can't be referenced from a pipeline node at all.
  // Filtered into the transformer vs analyzer dropdown by `kind`.
  const codeTransformersQuery = useQuery({
    queryKey: ["code-transformers", "list"],
    queryFn: ({ signal }) => listCodeTransformers(signal)
  });

  // Populates the dataset-source / dataset-sink Autocomplete. Both runners
  // resolve `node.key` via IDatasetStore.GetByNameAsync — typos surface as
  // a run-time "dataset X does not exist" instead of a save-time error, so
  // the dropdown is the only place the author gets an authoritative list.
  // Sink is filtered to Cached datasets (mode === 2) because DatasetSinkRunner
  // rejects Virtual targets at run time.
  const datasetsQuery = useQuery({
    queryKey: ["datasets", "list"],
    queryFn: ({ signal }) => listDatasets(signal)
  });

  const [nodes, setNodes, onNodesChange] = useNodesState<PipelineFlowNode>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([]);
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);

  useEffect(() => {
    if (pipelineQuery.data) {
      const decoded = decodeGraph(pipelineQuery.data.graphJson);
      setNodes(decoded.nodes);
      setEdges(decoded.edges);
    }
  }, [pipelineQuery.data, setNodes, setEdges]);

  const onConnect = useCallback(
    (connection: Connection) =>
      setEdges((eds) =>
        addEdge(
          { ...connection, id: `edge_${Math.random().toString(36).slice(2, 10)}` },
          eds
        )
      ),
    [setEdges]
  );

  const addNode = useCallback(
    (kind: PipelineNodeShape["kind"]) => {
      const id = makeNodeId();
      setNodes((ns) => [
        ...ns,
        {
          id,
          type: kind,
          position: { x: 80 + ns.length * 40, y: 80 + ns.length * 40 },
          data: { label: "", kind, key: "", config: {} }
        }
      ]);
      setSelectedNodeId(id);
    },
    [setNodes]
  );

  const updateMutation = useMutation({
    mutationFn: () =>
      updatePipeline(id!, { graph: encodeGraph(nodes, edges) }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["pipeline", id] });
      notifications.show({ message: "Pipeline saved.", color: "green" });
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { reason?: string } } })?.response?.data?.reason ??
        (err instanceof Error ? err.message : "Save failed.");
      notifications.show({ message, color: "red" });
    }
  });

  const runMutation = useMutation({
    mutationFn: () => runPipeline(id!),
    onSuccess: () => {
      notifications.show({ message: "Pipeline run queued.", color: "green" });
      navigate(`/pipelines/${id}/runs`);
    }
  });

  // Metadata edit modal — separate mutation from the graph save so a name /
  // schedule change doesn't require the user to also re-save the React Flow
  // graph (which would also bump updated_at and overwrite any unrelated
  // graph edits in flight). Backend treats each PUT field independently:
  // null = leave unchanged, empty string = clear (cron + description), so
  // we send the trimmed value as-is.
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [settingsName, setSettingsName] = useState("");
  const [settingsDescription, setSettingsDescription] = useState("");
  const [settingsCron, setSettingsCron] = useState("");
  const [settingsError, setSettingsError] = useState<string | null>(null);

  function openSettings() {
    setSettingsName(pipelineQuery.data?.name ?? "");
    setSettingsDescription(pipelineQuery.data?.description ?? "");
    setSettingsCron(pipelineQuery.data?.scheduleCron ?? "");
    setSettingsError(null);
    setSettingsOpen(true);
  }

  const metadataMutation = useMutation({
    mutationFn: () =>
      updatePipeline(id!, {
        name: settingsName.trim(),
        description: settingsDescription.trim(),
        scheduleCron: settingsCron.trim()
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["pipeline", id] });
      queryClient.invalidateQueries({ queryKey: ["pipelines", "list"] });
      setSettingsOpen(false);
      notifications.show({ message: "Pipeline settings saved.", color: "green" });
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { reason?: string } } })?.response?.data?.reason ??
        (err instanceof Error ? err.message : "Save failed.");
      setSettingsError(message);
    }
  });

  const selectedNode = useMemo(
    () => nodes.find((n) => n.id === selectedNodeId) ?? null,
    [nodes, selectedNodeId]
  );

  // Per-node schema fetch (audit fix archived-7). Only fires when the selected
  // node is a transformer/analyzer with a chosen key — code-transformer
  // names also flow through these endpoints and 404 (no built-in
  // schema), at which point the editor falls back to the JSON Textarea.
  const schemaKind = selectedNode?.data.kind ?? null;
  const schemaKey = selectedNode?.data.key ?? "";
  const schemaQuery = useQuery({
    queryKey: ["node-schema", schemaKind, schemaKey],
    queryFn: ({ signal }) =>
      schemaKind === "transformer"
        ? getTransformerSchema(schemaKey, signal)
        : schemaKind === "analyzer"
        ? getAnalyzerSchema(schemaKey, signal)
        : Promise.resolve(null),
    enabled:
      schemaKey !== "" && (schemaKind === "transformer" || schemaKind === "analyzer"),
    staleTime: 5 * 60 * 1000
  });

  // Save-time dataset reference validation. PipelineGraphValidator on the
  // server only checks structure (cycles, dangling edges, unknown kinds);
  // missing/invalid dataset names blow up at run time inside
  // DatasetSourceRunner / DatasetSinkRunner. Catch them here so the author
  // can't save a graph that's guaranteed to fail. While the dataset list
  // is loading we skip validation (treat as "pending") rather than blocking
  // save on a transient null.
  const datasetErrors = useMemo(() => {
    if (!datasetsQuery.isSuccess) return [] as { nodeId: string; message: string }[];
    const sourceNames = new Set(datasetsQuery.data.map((d) => d.name));
    const sinkNames = new Set(
      datasetsQuery.data.filter((d) => d.mode === 2).map((d) => d.name)
    );
    const errs: { nodeId: string; message: string }[] = [];
    for (const n of nodes) {
      if (n.data.kind === "dataset-source") {
        if (n.data.key === "")
          errs.push({ nodeId: n.id, message: `Node ${n.id}: dataset source has no dataset selected.` });
        else if (!sourceNames.has(n.data.key))
          errs.push({
            nodeId: n.id,
            message: `Node ${n.id}: dataset "${n.data.key}" does not exist.`
          });
      } else if (n.data.kind === "dataset-sink") {
        if (n.data.key === "")
          errs.push({ nodeId: n.id, message: `Node ${n.id}: dataset sink has no dataset selected.` });
        else if (!sinkNames.has(n.data.key))
          errs.push({
            nodeId: n.id,
            message: `Node ${n.id}: "${n.data.key}" is not an existing Cached dataset.`
          });
      }
    }
    return errs;
  }, [nodes, datasetsQuery.isSuccess, datasetsQuery.data]);

  function updateSelectedNode(updater: (data: PipelineNodeData) => PipelineNodeData) {
    if (!selectedNodeId) return;
    setNodes((ns) =>
      ns.map((n) => (n.id === selectedNodeId ? { ...n, data: updater(n.data) } : n))
    );
  }

  function updateSelectedNodeConfig(name: string, value: string) {
    updateSelectedNode((d) => {
      const next = { ...(d.config ?? {}) };
      if (value === "") delete next[name];
      else next[name] = value;
      return { ...d, config: next };
    });
  }

  if (!id) return null;

  return (
    <Stack gap="sm" style={{ height: "calc(100vh - 220px)" }}>
      <Group justify="space-between" align="center">
        <Group gap="sm" align="center">
          <Title order={2}>{pipelineQuery.data?.name ?? "Pipeline"}</Title>
          {pipelineQuery.data?.scheduleCron ? (
            <Badge variant="light" size="sm" leftSection={<i className="fa fa-clock" />}>
              {pipelineQuery.data.scheduleCron}
            </Badge>
          ) : (
            <Badge variant="default" size="sm" color="gray">
              manual
            </Badge>
          )}
        </Group>
        <Group>
          <Button variant="default" onClick={() => navigate("/pipelines")}>
            Back to list
          </Button>
          <Button
            variant="default"
            leftSection={<i className="fa fa-gear" />}
            onClick={openSettings}
          >
            Settings
          </Button>
          <Button
            variant="default"
            onClick={() => navigate(`/pipelines/${id}/runs`)}
          >
            Run history
          </Button>
          <Tooltip
            label={datasetErrors.map((e) => e.message).join("\n")}
            multiline
            w={320}
            disabled={datasetErrors.length === 0}
            withinPortal
          >
            <Button
              variant="default"
              loading={updateMutation.isPending}
              onClick={() => updateMutation.mutate()}
              disabled={datasetErrors.length > 0}
            >
              Save
            </Button>
          </Tooltip>
          <Tooltip
            label={datasetErrors.map((e) => e.message).join("\n")}
            multiline
            w={320}
            disabled={datasetErrors.length === 0}
            withinPortal
          >
            <Button
              color="green"
              leftSection={<i className="fa fa-play" />}
              loading={runMutation.isPending}
              onClick={() => runMutation.mutate()}
              disabled={datasetErrors.length > 0}
            >
              Run
            </Button>
          </Tooltip>
        </Group>
      </Group>

      <Modal
        opened={settingsOpen}
        onClose={() => setSettingsOpen(false)}
        title="Pipeline settings"
        centered
      >
        <form
          onSubmit={(e) => {
            e.preventDefault();
            if (!settingsName.trim()) {
              setSettingsError("Name is required.");
              return;
            }
            setSettingsError(null);
            metadataMutation.mutate();
          }}
        >
          <Stack gap="sm">
            <TextInput
              label="Name"
              required
              value={settingsName}
              onChange={(e) => setSettingsName(e.currentTarget.value)}
              data-autofocus
            />
            <TextInput
              label="Description"
              value={settingsDescription}
              onChange={(e) => setSettingsDescription(e.currentTarget.value)}
            />
            <CronExpressionBuilder
              label="Schedule"
              description="Optional. Pick a preset or choose Custom to type a cron. v1 only triggers schedules of the form `*/N * * * *`."
              value={settingsCron}
              onChange={setSettingsCron}
            />
            {settingsError ? <Alert color="red">{settingsError}</Alert> : null}
            <Group justify="flex-end" mt="sm">
              <Button variant="default" onClick={() => setSettingsOpen(false)}>
                Cancel
              </Button>
              <Button type="submit" loading={metadataMutation.isPending}>
                Save settings
              </Button>
            </Group>
          </Stack>
        </form>
      </Modal>

      {pipelineQuery.error ? (
        <Alert color="red">Failed to load pipeline.</Alert>
      ) : null}

      <Group align="stretch" gap="sm" style={{ flex: 1, minHeight: 0 }}>
        <Paper p="xs" withBorder style={{ width: 200 }}>
          <Stack gap={4}>
            <Text fw={600} size="sm">Palette</Text>
            <Button size="xs" variant="light" onClick={() => addNode("dataset-source")}>
              + Dataset source
            </Button>
            <Button size="xs" variant="light" onClick={() => addNode("transformer")}>
              + Transformer
            </Button>
            <Button size="xs" variant="light" onClick={() => addNode("analyzer")}>
              + Analyzer
            </Button>
            <Button size="xs" variant="light" onClick={() => addNode("dataset-sink")}>
              + Dataset sink
            </Button>
            <Text size="xs" c="dimmed" mt="sm">
              Click a node to edit. Drag between handles to wire.
            </Text>
          </Stack>
        </Paper>

        <Box style={{ flex: 1, minWidth: 0, border: "1px solid var(--mantine-color-default-border)" }}>
          <ReactFlow
            nodes={nodes}
            edges={edges}
            onNodesChange={onNodesChange}
            onEdgesChange={onEdgesChange}
            onConnect={onConnect}
            onNodeClick={(_, n) => setSelectedNodeId(n.id)}
            onPaneClick={() => setSelectedNodeId(null)}
            nodeTypes={NODE_TYPES}
            fitView
          >
            <Background />
            <Controls />
            <MiniMap />
          </ReactFlow>
        </Box>

        <Paper p="xs" withBorder style={{ width: 280 }}>
          {selectedNode ? (
            <Stack gap="xs">
              <Text fw={600} size="sm">
                Edit {NODE_KIND_LABELS[selectedNode.data.kind]}
              </Text>
              {selectedNode.data.kind === "transformer" ? (
                <NativeSelect
                  label="Transformer"
                  data={[
                    { value: "", label: "Select…" },
                    ...(transformersQuery.data ?? []).map((t) => ({
                      value: t.key,
                      label: t.displayName
                    })),
                    // Code transformers share the node's `key` field with
                    // built-ins; the backend resolver tries the built-in
                    // registry first and falls through to the code store by
                    // name, so a name collision lets the built-in shadow the
                    // code transformer. The "(code)" suffix makes the
                    // collision visible in the dropdown if it happens.
                    ...(codeTransformersQuery.data ?? [])
                      .filter((c) => c.kind === "transformer")
                      .map((c) => ({
                        value: c.name,
                        label: `${c.name} (code)`
                      }))
                  ]}
                  value={selectedNode.data.key}
                  onChange={(e) => {
                    const v = e.currentTarget.value;
                    updateSelectedNode((d) => ({ ...d, key: v, label: v }));
                  }}
                />
              ) : selectedNode.data.kind === "analyzer" ? (
                <NativeSelect
                  label="Analyzer"
                  data={[
                    { value: "", label: "Select…" },
                    ...(analyzersQuery.data ?? []).map((a) => ({
                      value: a.key,
                      label: a.displayName
                    })),
                    ...(codeTransformersQuery.data ?? [])
                      .filter((c) => c.kind === "analyzer")
                      .map((c) => ({
                        value: c.name,
                        label: `${c.name} (code)`
                      }))
                  ]}
                  value={selectedNode.data.key}
                  onChange={(e) => {
                    const v = e.currentTarget.value;
                    updateSelectedNode((d) => ({ ...d, key: v, label: v }));
                  }}
                />
              ) : (
                (() => {
                  // dataset-sink rejects Virtual targets at run time (sink
                  // truncate-and-reload only works on Cached). Filter the
                  // sink dropdown so the author can't pick something the
                  // runner will reject; for dataset-source any dataset is
                  // valid.
                  const isSink = selectedNode.data.kind === "dataset-sink";
                  const validNames = (datasetsQuery.data ?? [])
                    .filter((ds) => (isSink ? ds.mode === 2 : true))
                    .map((ds) => ds.name);
                  const currentKey = selectedNode.data.key;
                  // If the pipeline was authored before this dataset got
                  // deleted (or before the sink-must-be-Cached rule applied),
                  // the saved key won't be in the option list. Include it
                  // anyway so the user can SEE what's stored and flag it as
                  // invalid via the error slot — silently blanking would
                  // hide the breakage.
                  const staleKey =
                    datasetsQuery.isSuccess &&
                    currentKey !== "" &&
                    !validNames.includes(currentKey);
                  const data = staleKey ? [...validNames, currentKey] : validNames;
                  return (
                    <Select
                      label="Dataset"
                      description={
                        isSink
                          ? "Sink target — must be an existing Cached dataset."
                          : "Source — must reference an existing dataset."
                      }
                      placeholder="Type to search…"
                      searchable
                      nothingFoundMessage="No matching dataset"
                      data={data}
                      value={currentKey === "" ? null : currentKey}
                      onChange={(v) =>
                        updateSelectedNode((d) => ({
                          ...d,
                          key: v ?? "",
                          label: v ?? ""
                        }))
                      }
                      error={
                        staleKey
                          ? isSink
                            ? `"${currentKey}" is not an existing Cached dataset.`
                            : `Dataset "${currentKey}" does not exist.`
                          : null
                      }
                    />
                  );
                })()
              )}
              {/*
                Audit fix archived-7 — kind-specific form when /api/transformers/
                {key}/schema (or /analyzers/{key}/schema) returns a schema
                for the picked key. Code transformers and plugin-
                contributed kinds return 404 and fall through to the
                freeform JSON Textarea below. The two branches share the
                same `data.config` map so toggling between editors
                preserves whatever the author already typed.
              */}
              {(selectedNode.data.kind === "transformer" ||
                selectedNode.data.kind === "analyzer") &&
              schemaQuery.data ? (
                <SchemaFormFields
                  schema={schemaQuery.data}
                  config={selectedNode.data.config ?? {}}
                  onChange={updateSelectedNodeConfig}
                />
              ) : (
                <Textarea
                  label="Config (JSON)"
                  description={
                    selectedNode.data.kind === "transformer" ||
                    selectedNode.data.kind === "analyzer"
                      ? "No published schema for this kind. Flat string→string map; consult the kind's docs for valid keys."
                      : "Flat string→string map. Each transformer/analyzer documents its own keys."
                  }
                  autosize
                  minRows={4}
                  value={JSON.stringify(selectedNode.data.config ?? {}, null, 2)}
                  onChange={(e) => {
                    try {
                      const parsed = JSON.parse(e.currentTarget.value);
                      updateSelectedNode((d) => ({ ...d, config: parsed }));
                    } catch {
                      // Ignore invalid JSON until the user fixes it; the save
                      // path validates on submit.
                    }
                  }}
                  styles={{
                    input: { fontFamily: "var(--mantine-font-family-monospace)", fontSize: 13 }
                  }}
                />
              )}
              <Button
                color="red"
                variant="light"
                onClick={() => {
                  setNodes((ns) => ns.filter((n) => n.id !== selectedNodeId));
                  setEdges((es) => es.filter((e) => e.source !== selectedNodeId && e.target !== selectedNodeId));
                  setSelectedNodeId(null);
                }}
              >
                Remove node
              </Button>
            </Stack>
          ) : (
            <Text size="xs" c="dimmed">Select a node to edit.</Text>
          )}
        </Paper>
      </Group>
    </Stack>
  );
}
