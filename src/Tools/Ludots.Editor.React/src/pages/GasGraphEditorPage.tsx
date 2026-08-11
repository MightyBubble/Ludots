import React from 'react';
import { Link } from 'react-router-dom';
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  Handle,
  Position,
  applyNodeChanges,
  type Node,
  type Edge,
  type NodeProps,
  type NodeChange,
  MarkerType,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';

type GraphNodeConfig = {
  id: string;
  op: string;
  next?: string | null;
  inputs?: string[];
  teamId?: number;
  attribute?: string | null;
  tag?: string | null;
  collectionKey?: string | null;
  effectTemplate?: string | null;
  floatValue?: number;
  intValue?: number;
  boolValue?: boolean;
  limit?: number;
  radiusCm?: number;
  rangeCm?: number;
  directionDeg?: number;
  halfAngleDeg?: number;
  lengthCm?: number;
  halfWidthCm?: number;
  halfHeightCm?: number;
  rotationDeg?: number;
  hexRadius?: number;
  layerMask?: number;
  relationshipMode?: string | null;
  sort?: string | null;
  relationshipType?: string | null;
  metric?: string | null;
  flag?: string | null;
  reason?: string | null;
  payloadPreset?: string | null;
  builtinHandler?: string | null;
  descending?: boolean;
  slot?: number;
  template?: string | null;
  blackboardKey?: string | null;
  configKey?: string | null;
  validOutput?: string | null;
  droppedOutput?: string | null;
  queryCapacityPolicy?: string | null;
};

type GasNodeData = GraphNodeConfig & { label: string };

type GraphOutputConfig = {
  id: string;
  destination?: string;
  type?: string;
  source?: string;
  key?: string;
};

type GraphConfig = {
  id: string;
  kind: string;
  entry: string;
  nodes: GraphNodeConfig[];
  outputs?: GraphOutputConfig[];
};

type GraphDiagnostic = {
  severity: string;
  code: string;
  message: string;
  graphId: string;
  nodeId?: string | null;
};

type ValidateResponse = {
  ok: boolean;
  source?: string;
  diagnostics?: GraphDiagnostic[];
  instructionCount?: number;
  error?: string;
};

const DEFAULT_MOD_ID = 'UiPlayerAggregateGraphMvpShowcaseMod';
const DEFAULT_GRAPH_ID = 'ui.panel.player.resource.aggregate';

function GasNode({ data, selected }: NodeProps<Node<GasNodeData>>) {
  return (
    <div
      className={`min-w-[160px] rounded border px-3 py-2 text-xs shadow ${
        selected ? 'border-sky-400 bg-slate-800' : 'border-slate-600 bg-slate-900'
      }`}
    >
      <Handle type="target" position={Position.Left} className="!bg-slate-400" />
      <div className="font-semibold text-slate-100">{data.label}</div>
      <div className="mt-1 text-[10px] text-sky-300">{data.op}</div>
      <Handle type="source" position={Position.Right} className="!bg-slate-400" />
    </div>
  );
}

const nodeTypes = { gas: GasNode };

function omitUndefined<T extends Record<string, unknown>>(value: T): T {
  const out: Record<string, unknown> = {};
  for (const [key, entry] of Object.entries(value)) {
    if (entry !== undefined) out[key] = entry;
  }
  return out as T;
}

function toWireNode(n: GraphNodeConfig): GraphNodeConfig {
  return omitUndefined({
    id: n.id,
    op: n.op,
    next: n.next ?? undefined,
    inputs: n.inputs && n.inputs.length > 0 ? n.inputs : undefined,
    teamId: n.teamId,
    attribute: n.attribute ?? undefined,
    tag: n.tag ?? undefined,
    collectionKey: n.collectionKey ?? undefined,
    effectTemplate: n.effectTemplate ?? undefined,
    floatValue: n.floatValue,
    intValue: n.intValue,
    boolValue: n.boolValue,
    limit: n.limit,
    radiusCm: n.radiusCm,
    rangeCm: n.rangeCm,
    directionDeg: n.directionDeg,
    halfAngleDeg: n.halfAngleDeg,
    lengthCm: n.lengthCm,
    halfWidthCm: n.halfWidthCm,
    halfHeightCm: n.halfHeightCm,
    rotationDeg: n.rotationDeg,
    hexRadius: n.hexRadius,
    layerMask: n.layerMask,
    relationshipMode: n.relationshipMode ?? undefined,
    sort: n.sort ?? undefined,
    relationshipType: n.relationshipType ?? undefined,
    metric: n.metric ?? undefined,
    flag: n.flag ?? undefined,
    reason: n.reason ?? undefined,
    payloadPreset: n.payloadPreset ?? undefined,
    builtinHandler: n.builtinHandler ?? undefined,
    descending: n.descending,
    slot: n.slot,
    template: n.template ?? undefined,
    blackboardKey: n.blackboardKey ?? undefined,
    configKey: n.configKey ?? undefined,
    validOutput: n.validOutput ?? undefined,
    droppedOutput: n.droppedOutput ?? undefined,
    queryCapacityPolicy: n.queryCapacityPolicy ?? undefined,
  });
}

function graphToFlow(graph: GraphConfig): { nodes: Node<GasNodeData>[]; edges: Edge[] } {
  const nodes: Node<GasNodeData>[] = graph.nodes.map((n, index) => ({
    id: n.id,
    type: 'gas',
    position: { x: 40 + index * 220, y: 80 + (index % 2) * 40 },
    data: { ...n, label: n.id },
  }));

  const edges: Edge[] = [];
  for (const n of graph.nodes) {
    if (!n.next) continue;
    edges.push({
      id: `${n.id}->${n.next}`,
      source: n.id,
      target: n.next,
      markerEnd: { type: MarkerType.ArrowClosed },
      label: 'next',
      style: { stroke: '#64748b' },
    });
  }

  return { nodes, edges };
}

function flowNodesToGraph(graph: GraphConfig, nodes: Node<GasNodeData>[]): GraphConfig {
  const byId = new Map(nodes.map((n) => [n.id, n.data]));
  return {
    id: graph.id,
    kind: graph.kind,
    entry: graph.entry,
    outputs: graph.outputs,
    nodes: graph.nodes.map((n) => {
      const edited = byId.get(n.id);
      if (!edited) return toWireNode(n);
      const { label: _label, ...rest } = edited;
      return toWireNode({
        ...n,
        ...rest,
        id: n.id,
        op: rest.op || n.op,
        next: rest.next ?? null,
      });
    }),
  };
}

export const GasGraphEditorPage: React.FC = () => {
  const [modId, setModId] = React.useState(DEFAULT_MOD_ID);
  const [graphId, setGraphId] = React.useState(DEFAULT_GRAPH_ID);
  const [graph, setGraph] = React.useState<GraphConfig | null>(null);
  const [nodes, setNodes] = React.useState<Node<GasNodeData>[]>([]);
  const [edges, setEdges] = React.useState<Edge[]>([]);
  const [selectedNodeId, setSelectedNodeId] = React.useState<string | null>(null);
  const [status, setStatus] = React.useState<string>('Idle');
  const [diagnosticsText, setDiagnosticsText] = React.useState<string>('');
  const [busy, setBusy] = React.useState(false);

  const selectedNode = React.useMemo(
    () => nodes.find((n) => n.id === selectedNodeId) ?? null,
    [nodes, selectedNodeId],
  );

  const selectedData = selectedNode?.data ?? null;

  const loadGraph = React.useCallback(async () => {
    setBusy(true);
    setStatus('Loading…');
    setDiagnosticsText('');
    try {
      const res = await fetch(`/api/mods/${encodeURIComponent(modId)}/gas/graphs/${encodeURIComponent(graphId)}`);
      const payload = await res.json();
      if (!res.ok || !payload.ok || !payload.graph) {
        throw new Error(payload.error ?? `Load failed (${res.status})`);
      }
      const loaded = payload.graph as GraphConfig;
      setGraph(loaded);
      const flow = graphToFlow(loaded);
      setNodes(flow.nodes);
      setEdges(flow.edges);
      setSelectedNodeId(null);
      setStatus(`Loaded ${loaded.id} (${loaded.kind})`);
    } catch (err) {
      setGraph(null);
      setNodes([]);
      setEdges([]);
      setStatus(err instanceof Error ? err.message : String(err));
    } finally {
      setBusy(false);
    }
  }, [modId, graphId]);

  React.useEffect(() => {
    void loadGraph();
  }, [loadGraph]);

  const currentGraph = React.useMemo(() => {
    if (!graph) return null;
    return flowNodesToGraph(graph, nodes);
  }, [graph, nodes]);

  const updateSelectedField = (field: 'teamId' | 'attribute' | 'next', value: string) => {
    if (!selectedNodeId || !graph) return;
    const nextNodes = nodes.map((n) => {
      if (n.id !== selectedNodeId) return n;
      const data: GasNodeData = { ...n.data };
      if (field === 'teamId') {
        const parsed = Number.parseInt(value, 10);
        data.teamId = Number.isFinite(parsed) ? parsed : 0;
      } else if (field === 'attribute') {
        data.attribute = value;
      } else {
        data.next = value.trim() === '' ? null : value.trim();
      }
      return { ...n, data };
    });
    setNodes(nextNodes);
    if (field === 'next') {
      setEdges(graphToFlow(flowNodesToGraph(graph, nextNodes)).edges);
    }
  };

  const onNodesChange = React.useCallback((changes: NodeChange<Node<GasNodeData>>[]) => {
    setNodes((prev) => applyNodeChanges(changes, prev));
  }, []);

  const runValidate = async (graphBody: GraphConfig): Promise<ValidateResponse> => {
    const res = await fetch(
      `/api/mods/${encodeURIComponent(modId)}/gas/graphs/${encodeURIComponent(graphId)}/validate`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(graphBody),
      },
    );
    const payload = (await res.json()) as ValidateResponse;
    if (!res.ok && payload.error) {
      return { ok: false, diagnostics: [], error: payload.error, instructionCount: 0 };
    }
    return payload;
  };

  const formatDiagnostics = (payload: ValidateResponse): string => {
    if (payload.error) return payload.error;
    const lines = (payload.diagnostics ?? []).map(
      (d) => `[${d.severity}] ${d.code}${d.nodeId ? ` @${d.nodeId}` : ''}: ${d.message}`,
    );
    if (lines.length === 0) {
      return payload.ok
        ? `OK — instructionCount=${payload.instructionCount ?? 0}`
        : 'Compile failed with no diagnostics.';
    }
    return [
      payload.ok ? `OK — instructionCount=${payload.instructionCount ?? 0}` : 'Compile FAILED',
      ...lines,
    ].join('\n');
  };

  const onValidate = async () => {
    if (!currentGraph) return;
    setBusy(true);
    setStatus('Validating via GraphCompiler…');
    try {
      const payload = await runValidate(currentGraph);
      setDiagnosticsText(formatDiagnostics(payload));
      setStatus(payload.ok ? 'Validate OK' : 'Validate failed');
    } catch (err) {
      setStatus(err instanceof Error ? err.message : String(err));
    } finally {
      setBusy(false);
    }
  };

  const onSave = async () => {
    if (!currentGraph) return;
    setBusy(true);
    setStatus('Validating before save…');
    try {
      const validation = await runValidate(currentGraph);
      setDiagnosticsText(formatDiagnostics(validation));
      if (!validation.ok) {
        setStatus('Save refused: GraphCompiler failed');
        return;
      }

      const res = await fetch(
        `/api/mods/${encodeURIComponent(modId)}/gas/graphs/${encodeURIComponent(graphId)}`,
        {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(currentGraph),
        },
      );
      const payload = await res.json();
      if (!res.ok || !payload.ok) {
        throw new Error(payload.error ?? `Save failed (${res.status})`);
      }
      setStatus(`Saved to ${payload.path ?? 'graphs.json'}`);
      await loadGraph();
    } catch (err) {
      setStatus(err instanceof Error ? err.message : String(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex h-screen w-screen flex-col bg-slate-950 text-slate-100">
      <header className="flex flex-wrap items-center gap-3 border-b border-slate-800 bg-slate-900 px-4 py-3">
        <div className="min-w-40">
          <div className="text-sm font-semibold text-white">GAS Query Graph Editor</div>
          <div className="text-[10px] text-slate-500">Bridge → graphs.json → GraphCompiler</div>
        </div>
        <Link to="/" className="rounded border border-slate-700 px-2 py-1 text-xs text-slate-300 hover:bg-slate-800">
          Map Editor
        </Link>
        <label className="flex items-center gap-2 text-xs text-slate-400">
          modId
          <input
            value={modId}
            onChange={(e) => setModId(e.target.value)}
            className="w-72 rounded border border-slate-700 bg-slate-950 px-2 py-1 text-slate-100"
          />
        </label>
        <label className="flex items-center gap-2 text-xs text-slate-400">
          graphId
          <input
            value={graphId}
            onChange={(e) => setGraphId(e.target.value)}
            className="w-80 rounded border border-slate-700 bg-slate-950 px-2 py-1 text-slate-100"
          />
        </label>
        <button
          type="button"
          disabled={busy}
          onClick={() => void loadGraph()}
          className="rounded bg-slate-700 px-3 py-1 text-xs font-semibold hover:bg-slate-600 disabled:opacity-50"
        >
          Load
        </button>
        <button
          type="button"
          disabled={busy || !currentGraph}
          onClick={() => void onValidate()}
          className="rounded bg-sky-700 px-3 py-1 text-xs font-semibold hover:bg-sky-600 disabled:opacity-50"
        >
          Validate
        </button>
        <button
          type="button"
          disabled={busy || !currentGraph}
          onClick={() => void onSave()}
          className="rounded bg-emerald-700 px-3 py-1 text-xs font-semibold hover:bg-emerald-600 disabled:opacity-50"
        >
          Save
        </button>
        <div className="text-xs text-slate-400">{status}</div>
      </header>

      <div className="grid min-h-0 flex-1 grid-cols-[1fr_320px]">
        <div className="min-h-0">
          {graph ? (
            <ReactFlow
              nodes={nodes}
              edges={edges}
              nodeTypes={nodeTypes}
              onNodesChange={onNodesChange}
              onSelectionChange={({ nodes: selected }) => {
                setSelectedNodeId(selected[0]?.id ?? null);
              }}
              fitView
              proOptions={{ hideAttribution: true }}
            >
              <Background gap={16} color="#334155" />
              <Controls />
              <MiniMap pannable zoomable />
            </ReactFlow>
          ) : (
            <div className="flex h-full items-center justify-center text-sm text-slate-500">
              No graph loaded. Check Bridge is running on :5299.
            </div>
          )}
        </div>

        <aside className="flex min-h-0 flex-col border-l border-slate-800 bg-slate-900/80">
          <div className="border-b border-slate-800 px-3 py-2 text-xs font-semibold uppercase tracking-wide text-slate-400">
            Inspector
          </div>
          <div className="space-y-3 overflow-auto p-3 text-xs">
            {selectedData ? (
              <>
                <div>
                  <div className="text-slate-500">Id</div>
                  <div className="font-mono text-slate-100">{selectedData.id}</div>
                </div>
                <div>
                  <div className="text-slate-500">Op</div>
                  <div className="font-mono text-sky-300">{selectedData.op}</div>
                </div>
                <label className="block">
                  <div className="mb-1 text-slate-500">Next</div>
                  <input
                    value={selectedData.next ?? ''}
                    onChange={(e) => updateSelectedField('next', e.target.value)}
                    className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
                  />
                </label>
                <label className="block">
                  <div className="mb-1 text-slate-500">TeamId</div>
                  <input
                    type="number"
                    value={selectedData.teamId ?? 0}
                    onChange={(e) => updateSelectedField('teamId', e.target.value)}
                    className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
                  />
                </label>
                <label className="block">
                  <div className="mb-1 text-slate-500">Attribute</div>
                  <input
                    value={selectedData.attribute ?? ''}
                    onChange={(e) => updateSelectedField('attribute', e.target.value)}
                    className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
                  />
                </label>
                {selectedData.tag ? (
                  <div>
                    <div className="text-slate-500">Tag</div>
                    <div className="font-mono">{String(selectedData.tag)}</div>
                  </div>
                ) : null}
                {selectedData.collectionKey ? (
                  <div>
                    <div className="text-slate-500">CollectionKey</div>
                    <div className="font-mono">{String(selectedData.collectionKey)}</div>
                  </div>
                ) : null}
              </>
            ) : (
              <div className="text-slate-500">Select a node to edit TeamId / Attribute / Next.</div>
            )}
          </div>

          <div className="border-t border-slate-800 px-3 py-2 text-xs font-semibold uppercase tracking-wide text-slate-400">
            Diagnostics
          </div>
          <pre className="min-h-0 flex-1 overflow-auto whitespace-pre-wrap p-3 font-mono text-[11px] text-amber-200">
            {diagnosticsText || 'Validate or Save to run GraphCompiler.CompileWithOutputs.'}
          </pre>
        </aside>
      </div>
    </div>
  );
};
