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
  Box,
  Button,
  Group,
  NativeSelect,
  Paper,
  Stack,
  Text,
  Textarea,
  TextInput,
  Title
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
import { listTransformers } from "@/api/transformers";
import { listAnalyzers } from "@/api/analyzers";

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
      navigate(`/admin/config/pipelines/${id}/runs`);
    }
  });

  const selectedNode = useMemo(
    () => nodes.find((n) => n.id === selectedNodeId) ?? null,
    [nodes, selectedNodeId]
  );

  function updateSelectedNode(updater: (data: PipelineNodeData) => PipelineNodeData) {
    if (!selectedNodeId) return;
    setNodes((ns) =>
      ns.map((n) => (n.id === selectedNodeId ? { ...n, data: updater(n.data) } : n))
    );
  }

  if (!id) return null;

  return (
    <Stack gap="sm" style={{ height: "calc(100vh - 220px)" }}>
      <Group justify="space-between" align="center">
        <Title order={2}>{pipelineQuery.data?.name ?? "Pipeline"}</Title>
        <Group>
          <Button variant="default" onClick={() => navigate("/admin/config/pipelines")}>
            Back to list
          </Button>
          <Button
            variant="default"
            onClick={() => navigate(`/admin/config/pipelines/${id}/runs`)}
          >
            Run history
          </Button>
          <Button
            variant="default"
            loading={updateMutation.isPending}
            onClick={() => updateMutation.mutate()}
          >
            Save
          </Button>
          <Button
            color="green"
            leftSection={<i className="fa fa-play" />}
            loading={runMutation.isPending}
            onClick={() => runMutation.mutate()}
          >
            Run
          </Button>
        </Group>
      </Group>

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
                    }))
                  ]}
                  value={selectedNode.data.key}
                  onChange={(e) =>
                    updateSelectedNode((d) => ({ ...d, key: e.currentTarget.value, label: e.currentTarget.value }))
                  }
                />
              ) : selectedNode.data.kind === "analyzer" ? (
                <NativeSelect
                  label="Analyzer"
                  data={[
                    { value: "", label: "Select…" },
                    ...(analyzersQuery.data ?? []).map((a) => ({
                      value: a.key,
                      label: a.displayName
                    }))
                  ]}
                  value={selectedNode.data.key}
                  onChange={(e) =>
                    updateSelectedNode((d) => ({ ...d, key: e.currentTarget.value, label: e.currentTarget.value }))
                  }
                />
              ) : (
                <TextInput
                  label="Dataset name"
                  value={selectedNode.data.key}
                  onChange={(e) =>
                    updateSelectedNode((d) => ({ ...d, key: e.currentTarget.value, label: e.currentTarget.value }))
                  }
                />
              )}
              <Textarea
                label="Config (JSON)"
                description="Flat string→string map. Each transformer/analyzer documents its own keys."
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
