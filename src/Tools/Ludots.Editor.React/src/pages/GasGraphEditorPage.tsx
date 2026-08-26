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
  addEdge,
  type Node,
  type Edge,
  type Connection,
  type NodeProps,
  type NodeChange,
  MarkerType,
} from '@xyflow/react';
import { Search, Plus } from 'lucide-react';
import '@xyflow/react/dist/style.css';

type GraphNodeConfig = {
  id: string;
  op: string;
  next?: string | null;
  inputs?: string[];
  graphId?: number;
  functionName?: string | null;
  teamId?: number;
  attribute?: string | null;
  tag?: string | null;
  lookupTable?: string | null;
  lookupField?: string | null;
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
  panelType?: string | null;
  panelAnchor?: string | null;
  panelSkin?: string | null;
  panelZOrder?: number | null;
  var?: string | null;
  pinRegister?: number;
};

type GasNodeData = GraphNodeConfig & { label: string; descriptor?: GraphDescriptor; sugar?: GraphSugarDescriptor; controlOutputPorts?: string[] };

type GraphDescriptor = {
  op: string;
  code: number;
  linearOutputType: string;
  queryOutputType: string;
  linearInputPorts: string[];
  queryInputPorts: string[];
  scriptInputPorts: string[];
  controlOutputPorts: string[];
  dstRole: string;
  flagsRole: string;
  immRole: string;
  scriptSliceOnly: boolean;
};

type GraphSugarDescriptor = {
  op: string;
  controlOutputPorts: string[];
  valueInputPorts: string[];
  outputType: string;
  lowersTo: string;
};

type EditorLayout = {
  nodes?: Record<string, { x: number; y: number; collapsed?: boolean }>;
  viewport?: { x: number; y: number; zoom: number };
};

type DebugMount = {
  graphId: number;
  graphName: string;
  entryLabel: string;
  event: string;
  mode: string;
  latestSequence: number;
  cursor: { pc: number; steps: number; status: string; suspended: boolean };
};

type DebugEvent = {
  sequence: number;
  event: string;
  nodeId?: string | null;
  op?: string | null;
  pinIndex?: number;
  value?: number | boolean;
  steps: number;
};

type GraphControlEdgeConfig = {
  from: string;
  fromPort: string;
  to: string;
};

type GraphValueEdgeConfig = {
  from: string;
  fromPort: string;
  to: string;
  toPort: string;
};

type GasEdgeData = {
  kind: 'control' | 'value';
};

type GraphOutputConfig = {
  id: string;
  destination?: string;
  type?: string;
  source?: string;
  key?: string;
};

type GraphEntryConfig = {
  label: string;
  event: string;
  start: string;
  once?: boolean;
  refire?: string | null;
  filters?: {
    region?: string | null;
    tag?: string | null;
    team?: number | null;
    threshold?: number | null;
    direction?: string | null;
    action?: string | null;
  } | null;
};

type GraphConfig = {
  id: string;
  kind: string;
  entry: string;
  entries?: GraphEntryConfig[];
  nodes: GraphNodeConfig[];
  controlEdges?: GraphControlEdgeConfig[];
  valueEdges?: GraphValueEdgeConfig[];
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
  const descriptor = data.descriptor;
  const sugar = data.sugar;
  const inputPorts = Array.from(new Set([
    ...(descriptor?.linearInputPorts ?? []),
    ...(descriptor?.queryInputPorts ?? []),
    ...(descriptor?.scriptInputPorts ?? []),
    ...(sugar?.valueInputPorts ?? []),
  ]));
  const outputType = descriptor?.queryOutputType !== 'Void'
    ? descriptor?.queryOutputType
    : descriptor?.linearOutputType;
  const valueOutput = outputType && outputType !== 'Void' && outputType !== 'TargetList' ? 'value' : null;
  const listOutput = outputType === 'TargetList' ? 'list' : null;

  return (
    <div
      className={`relative min-w-[180px] rounded border px-3 py-2 text-xs shadow ${
        selected ? 'border-sky-400 bg-slate-800' : 'border-slate-600 bg-slate-900'
      }`}
    >
      <Handle id="control-in" type="target" position={Position.Left} className="!top-4 !bg-sky-400" />
      {inputPorts.map((port, index) => (
        <Handle
          key={port}
          id={port}
          type="target"
          position={Position.Left}
          style={{ top: 42 + index * 18 }}
          className="!bg-emerald-400"
        />
      ))}
      <div className="font-semibold text-slate-100">{data.label}</div>
      <div className="mt-1 text-[10px] text-sky-300">{data.op}</div>
      <div className="mt-2 flex flex-wrap gap-1 text-[9px] uppercase tracking-wide text-slate-500">
        <span className="rounded bg-sky-950 px-1 text-sky-300">next</span>
        {valueOutput ? <span className="rounded bg-violet-950 px-1 text-violet-300">{valueOutput}</span> : null}
        {inputPorts.map((port) => <span key={port} className="rounded bg-emerald-950 px-1 text-emerald-300">{port}</span>)}
      </div>
      {(data.controlOutputPorts ?? []).map((port, index) => (
        <Handle
          key={port}
          id={port}
          type="source"
          position={Position.Right}
          style={{ top: 16 + index * 18 }}
          className="!bg-sky-400"
        />
      ))}
      {valueOutput ? (
        <Handle id={valueOutput} type="source" position={Position.Right} className="!top-12 !bg-violet-400" />
      ) : null}
      {listOutput ? (
        <Handle id={listOutput} type="source" position={Position.Right} className="!top-12 !bg-emerald-400" />
      ) : null}
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
    graphId: n.graphId,
    functionName: n.functionName ?? undefined,
    next: n.next ?? undefined,
    inputs: n.inputs && n.inputs.length > 0 ? n.inputs : undefined,
    teamId: n.teamId,
    attribute: n.attribute ?? undefined,
    tag: n.tag ?? undefined,
    lookupTable: n.lookupTable ?? undefined,
    lookupField: n.lookupField ?? undefined,
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
    panelType: n.panelType ?? undefined,
    panelAnchor: n.panelAnchor ?? undefined,
    panelSkin: n.panelSkin ?? undefined,
    panelZOrder: n.panelZOrder,
    var: n.var ?? undefined,
    pinRegister: n.pinRegister,
  });
}

function isControlFlowGraph(graph: GraphConfig): boolean {
  return Array.isArray(graph.controlEdges) || Array.isArray(graph.valueEdges);
}

function resolveControlOutputPorts(op: string, descriptor?: GraphDescriptor, sugar?: GraphSugarDescriptor): string[] {
  if (sugar) return sugar.controlOutputPorts;
  if (descriptor) return descriptor.controlOutputPorts;
  throw new Error(`Descriptor missing for graph op '${op}'.`);
}

function edgeLabel(edge: Edge<GasEdgeData>): string {
  if (edge.data?.kind === 'control') return String(edge.sourceHandle ?? '');
  return `${String(edge.sourceHandle ?? '')} -> ${String(edge.targetHandle ?? '')}`;
}

function graphToFlow(
  graph: GraphConfig,
  descriptors: Record<string, GraphDescriptor> = {},
  sugars: Record<string, GraphSugarDescriptor> = {},
  layout: EditorLayout = {},
): { nodes: Node<GasNodeData>[]; edges: Edge<GasEdgeData>[] } {
  const switchPorts = new Map<string, string[]>();
  for (const edge of graph.controlEdges ?? []) {
    if (!edge.fromPort.startsWith('case:')) continue;
    const ports = switchPorts.get(edge.from) ?? [];
    if (!ports.includes(edge.fromPort)) ports.push(edge.fromPort);
    switchPorts.set(edge.from, ports);
  }
  const nodes: Node<GasNodeData>[] = graph.nodes.map((n, index) => ({
    id: n.id,
    type: 'gas',
    position: layout.nodes?.[n.id] ?? { x: 40 + index * 220, y: 80 + (index % 2) * 40 },
    data: {
      ...n,
      label: n.id,
      descriptor: descriptors[n.op],
      sugar: sugars[n.op],
      controlOutputPorts: [...resolveControlOutputPorts(n.op, descriptors[n.op], sugars[n.op]), ...(switchPorts.get(n.id) ?? [])],
    },
  }));

  const edges: Edge<GasEdgeData>[] = [];
  if (isControlFlowGraph(graph)) {
    for (const edge of graph.controlEdges ?? []) {
      edges.push({
        id: `c:${edge.from}:${edge.fromPort}:${edge.to}`,
        source: edge.from,
        sourceHandle: edge.fromPort,
        target: edge.to,
        targetHandle: 'control-in',
        markerEnd: { type: MarkerType.ArrowClosed },
        label: edge.fromPort,
        style: { stroke: '#38bdf8', strokeWidth: 2 },
        labelStyle: { fill: '#bae6fd', fontSize: 11 },
        data: { kind: 'control' },
      });
    }

    for (const edge of graph.valueEdges ?? []) {
      edges.push({
        id: `v:${edge.from}:${edge.fromPort}:${edge.to}:${edge.toPort}`,
        source: edge.from,
        sourceHandle: edge.fromPort,
        target: edge.to,
        targetHandle: edge.toPort,
        markerEnd: { type: MarkerType.ArrowClosed },
        label: `${edge.fromPort} -> ${edge.toPort}`,
        style: { stroke: edge.fromPort === 'list' ? '#34d399' : '#a78bfa', strokeWidth: 2 },
        labelStyle: { fill: edge.fromPort === 'list' ? '#bbf7d0' : '#ddd6fe', fontSize: 11 },
        data: { kind: 'value' },
      });
    }

    return { nodes, edges };
  }

  for (const n of graph.nodes) {
    if (!n.next) continue;
    edges.push({
      id: `${n.id}->${n.next}`,
      source: n.id,
      target: n.next,
      markerEnd: { type: MarkerType.ArrowClosed },
      label: 'next',
      style: { stroke: '#64748b' },
      data: { kind: 'control' },
    });
  }

  return { nodes, edges };
}

function flowToGraph(graph: GraphConfig, nodes: Node<GasNodeData>[], edges: Edge<GasEdgeData>[]): GraphConfig {
  const sourceById = new Map(graph.nodes.map((n) => [n.id, n]));
  const wireNodes = nodes.map((flowNode) => {
    const n = sourceById.get(flowNode.id);
    const edited = flowNode.data;
    if (!n) {
      return toWireNode({ ...edited, id: flowNode.id, op: edited.op });
    }
    const rest = { ...edited };
    delete rest.label;
    delete rest.descriptor;
    delete rest.sugar;
    delete rest.controlOutputPorts;
    return toWireNode({
      ...n,
      ...rest,
      id: n.id,
      op: rest.op || n.op,
      next: isControlFlowGraph(graph) ? undefined : rest.next ?? null,
    });
  });

  if (isControlFlowGraph(graph)) {
    return {
      id: graph.id,
      kind: graph.kind,
      entry: graph.entry,
      entries: graph.entries,
      nodes: wireNodes.map((n) => {
        const rest = { ...n };
        delete rest.next;
        delete rest.inputs;
        return rest;
      }),
      controlEdges: edges
        .filter((edge) => edge.data?.kind === 'control')
        .map((edge) => {
          if (!edge.sourceHandle) throw new Error(`Control edge '${edge.id}' is missing a source port.`);
          return { from: edge.source, fromPort: edge.sourceHandle, to: edge.target };
        }),
      valueEdges: edges
        .filter((edge) => edge.data?.kind === 'value')
        .map((edge) => ({
          from: edge.source,
          fromPort: String(edge.sourceHandle ?? ''),
          to: edge.target,
          toPort: String(edge.targetHandle ?? ''),
        })),
      outputs: graph.outputs,
    };
  }

  return {
    id: graph.id,
    kind: graph.kind,
    entry: graph.entry,
    outputs: graph.outputs,
    entries: graph.entries,
    nodes: wireNodes,
  };
}

export const GasGraphEditorPage: React.FC = () => {
  const [modId, setModId] = React.useState(DEFAULT_MOD_ID);
  const [graphId, setGraphId] = React.useState(DEFAULT_GRAPH_ID);
  const [graph, setGraph] = React.useState<GraphConfig | null>(null);
  const [descriptors, setDescriptors] = React.useState<Record<string, GraphDescriptor>>({});
  const [sugars, setSugars] = React.useState<Record<string, GraphSugarDescriptor>>({});
  const [nodeSearch, setNodeSearch] = React.useState('');
  const [layout, setLayout] = React.useState<EditorLayout>({});
  const [nodes, setNodes] = React.useState<Node<GasNodeData>[]>([]);
  const [edges, setEdges] = React.useState<Edge<GasEdgeData>[]>([]);
  const [selectedNodeId, setSelectedNodeId] = React.useState<string | null>(null);
  const [selectedEdgeId, setSelectedEdgeId] = React.useState<string | null>(null);
  const [status, setStatus] = React.useState<string>('Idle');
  const [diagnosticsText, setDiagnosticsText] = React.useState<string>('');
  const [busy, setBusy] = React.useState(false);
  const [debugMounts, setDebugMounts] = React.useState<DebugMount[]>([]);
  const [debugEntryLabel, setDebugEntryLabel] = React.useState('');
  const [debugEnabled, setDebugEnabled] = React.useState(false);
  const [debugEvents, setDebugEvents] = React.useState<DebugEvent[]>([]);
  const [debugSince, setDebugSince] = React.useState(0);
  const [debugStatus, setDebugStatus] = React.useState('Bridge idle');
  const [switchCaseValue, setSwitchCaseValue] = React.useState('0');
  const [switchCaseTarget, setSwitchCaseTarget] = React.useState('');
  const debugPollInFlight = React.useRef(false);

  const selectedNode = React.useMemo(
    () => nodes.find((n) => n.id === selectedNodeId) ?? null,
    [nodes, selectedNodeId],
  );

  const selectedData = selectedNode?.data ?? null;
  const selectedEdge = React.useMemo(
    () => edges.find((e) => e.id === selectedEdgeId) ?? null,
    [edges, selectedEdgeId],
  );

  const loadGraph = React.useCallback(async () => {
    setBusy(true);
    setStatus('Loading…');
    setDiagnosticsText('');
    try {
      const graphRes = await fetch(`/api/mods/${encodeURIComponent(modId)}/gas/graphs/${encodeURIComponent(graphId)}`);
      const payload = await graphRes.json();
      if (!graphRes.ok || !payload.ok || !payload.graph) {
        throw new Error(payload.error ?? `Load failed (${graphRes.status})`);
      }
      const loaded = payload.graph as GraphConfig;
      const [descriptorRes, layoutRes] = await Promise.all([
        fetch(`/api/graph/descriptors/${encodeURIComponent(loaded.kind)}`),
        fetch(`/api/mods/${encodeURIComponent(modId)}/gas/graph-editor/${encodeURIComponent(graphId)}`),
      ]);
      const descriptorPayload = await descriptorRes.json();
      const layoutPayload = await layoutRes.json();
      if (!descriptorRes.ok || !descriptorPayload.ok) {
        throw new Error(descriptorPayload.error ?? `Descriptor load failed (${descriptorRes.status})`);
      }
      if (!layoutRes.ok || !layoutPayload.ok || !layoutPayload.layout || typeof layoutPayload.layout !== 'object' || Array.isArray(layoutPayload.layout)) {
        throw new Error(layoutPayload.error ?? `Layout load failed (${layoutRes.status})`);
      }
      const nextDescriptors: Record<string, GraphDescriptor> = {};
      for (const descriptor of (descriptorPayload.descriptors ?? []) as GraphDescriptor[]) {
        if (!descriptor.op || !Array.isArray(descriptor.controlOutputPorts)) {
          throw new Error('Descriptor response is missing control output ports.');
        }
        nextDescriptors[descriptor.op] = descriptor;
      }
      const nextSugars: Record<string, GraphSugarDescriptor> = {};
      for (const sugar of (descriptorPayload.authoringSugars ?? []) as GraphSugarDescriptor[]) {
        nextSugars[sugar.op] = sugar;
      }
      const missingDescriptor = loaded.nodes.find((node) => !nextDescriptors[node.op] && !nextSugars[node.op]);
      if (missingDescriptor) throw new Error(`Descriptor missing for graph op '${missingDescriptor.op}'.`);
      const nextLayout = (layoutPayload.layout ?? {}) as EditorLayout;
      setDescriptors(nextDescriptors);
      setSugars(nextSugars);
      setLayout(nextLayout);
      setGraph(loaded);
      const flow = graphToFlow(loaded, nextDescriptors, nextSugars, nextLayout);
      setNodes(flow.nodes);
      setEdges(flow.edges);
      setSelectedNodeId(null);
      setSelectedEdgeId(null);
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
    return flowToGraph(graph, nodes, edges);
  }, [graph, nodes, edges]);

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
      setEdges(graphToFlow(flowToGraph(graph, nextNodes, edges), descriptors, sugars, layout).edges);
    }
  };

  const updateSelectedEdgeField = (field: 'source' | 'sourceHandle' | 'target' | 'targetHandle', value: string) => {
    if (!selectedEdgeId) return;
    setEdges((prev) =>
      prev.map((edge) => {
        if (edge.id !== selectedEdgeId) return edge;
        const nextEdge: Edge<GasEdgeData> = {
          ...edge,
          source: field === 'source' ? value.trim() : edge.source,
          sourceHandle: field === 'sourceHandle' ? value.trim() : edge.sourceHandle,
          target: field === 'target' ? value.trim() : edge.target,
          targetHandle: field === 'targetHandle' ? value.trim() : edge.targetHandle,
        };
        return {
          ...nextEdge,
          id: `${nextEdge.data?.kind ?? 'edge'}:${nextEdge.source}:${nextEdge.sourceHandle ?? ''}:${nextEdge.target}:${nextEdge.targetHandle ?? ''}`,
          label: edgeLabel(nextEdge),
        };
      }),
    );
  };

  const onNodesChange = React.useCallback((changes: NodeChange<Node<GasNodeData>>[]) => {
    setNodes((prev) => applyNodeChanges(changes, prev));
    const removed = new Set(changes.filter((change) => change.type === 'remove').map((change) => change.id));
    if (removed.size > 0) {
      setEdges((prev) => prev.filter((edge) => !removed.has(edge.source) && !removed.has(edge.target)));
    }
  }, []);

  const onConnect = React.useCallback((connection: Connection) => {
    if (!connection.source || !connection.target || !connection.sourceHandle || !connection.targetHandle) return;
    const kind: GasEdgeData['kind'] = connection.targetHandle === 'control-in' ? 'control' : 'value';
    setEdges((prev) => addEdge({
      ...connection,
      id: `${kind}:${connection.source}:${connection.sourceHandle}:${connection.target}:${connection.targetHandle}`,
      markerEnd: { type: MarkerType.ArrowClosed },
      label: kind === 'control'
        ? connection.sourceHandle
        : `${connection.sourceHandle} -> ${connection.targetHandle}`,
      data: { kind },
    }, prev));
  }, []);

  const addSwitchCase = React.useCallback(() => {
    if (!selectedNodeId || !graph || !isControlFlowGraph(graph) || selectedData?.op !== 'SwitchInt') return;
    const caseValue = Number.parseInt(switchCaseValue, 10);
    if (!Number.isInteger(caseValue) || !switchCaseTarget || !nodes.some((node) => node.id === switchCaseTarget)) {
      setStatus('SwitchInt case requires an integer value and an existing target node.');
      return;
    }
    const sourceHandle = `case:${caseValue}`;
    if (edges.some((edge) => edge.source === selectedNodeId && edge.sourceHandle === sourceHandle)) {
      setStatus(`SwitchInt case '${sourceHandle}' already exists.`);
      return;
    }
    setEdges((previous) => addEdge({
      id: `control:${selectedNodeId}:${sourceHandle}:${switchCaseTarget}:control-in`,
      source: selectedNodeId,
      sourceHandle,
      target: switchCaseTarget,
      targetHandle: 'control-in',
      markerEnd: { type: MarkerType.ArrowClosed },
      label: sourceHandle,
      data: { kind: 'control' },
    }, previous));
    setNodes((previous) => previous.map((node) => node.id !== selectedNodeId
      ? node
      : { ...node, data: { ...node.data, controlOutputPorts: [...new Set([...(node.data.controlOutputPorts ?? []), sourceHandle])] } }));
    setStatus(`Added SwitchInt ${sourceHandle} -> ${switchCaseTarget}.`);
  }, [edges, graph, nodes, selectedData?.op, selectedNodeId, switchCaseTarget, switchCaseValue]);

  const availableNodes = React.useMemo(() => {
    const entries = [
      ...Object.values(descriptors).map((descriptor) => ({ op: descriptor.op, descriptor, sugar: undefined })),
      ...Object.values(sugars).map((sugar) => ({ op: sugar.op, descriptor: undefined, sugar })),
    ];
    const query = nodeSearch.trim().toLocaleLowerCase();
    return entries
      .filter((entry) => !query || entry.op.toLocaleLowerCase().includes(query))
      .sort((a, b) => a.op.localeCompare(b.op));
  }, [descriptors, nodeSearch, sugars]);

  const addAuthoringNode = React.useCallback((op: string) => {
    if (!graph) return;
    const idBase = op.replace(/[^A-Za-z0-9_]/g, '_').toLocaleLowerCase();
    const used = new Set(nodes.map((node) => node.id));
    let suffix = 1;
    let id = idBase;
    while (used.has(id)) id = `${idBase}_${suffix++}`;
    const next: Node<GasNodeData> = {
      id,
      type: 'gas',
      position: { x: 80 + (nodes.length % 3) * 240, y: 120 + Math.floor(nodes.length / 3) * 150 },
      data: {
        id,
        op,
        label: id,
        descriptor: descriptors[op],
        sugar: sugars[op],
        controlOutputPorts: resolveControlOutputPorts(op, descriptors[op], sugars[op]),
      },
    };
    setNodes((previous) => [...previous, next]);
    setSelectedNodeId(id);
    setSelectedEdgeId(null);
    setStatus(`Added ${op}; wire its pins before validation.`);
  }, [descriptors, graph, nodes, sugars]);

  const saveLayout = React.useCallback(async () => {
    const nextLayout: EditorLayout = {
      ...layout,
      nodes: Object.fromEntries(nodes.map((node) => [node.id, { x: node.position.x, y: node.position.y }])),
    };
    const res = await fetch(
      `/api/mods/${encodeURIComponent(modId)}/gas/graph-editor/${encodeURIComponent(graphId)}`,
      { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(nextLayout) },
    );
    const payload = await res.json();
    if (!res.ok || !payload.ok) throw new Error(payload.error ?? `Layout save failed (${res.status})`);
    setLayout(nextLayout);
  }, [graphId, layout, modId, nodes]);

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
    setStatus('Validating via Bridge compiler…');
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
        setStatus('Save refused: compiler failed');
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
      await saveLayout();
      setStatus(`Saved to ${payload.path ?? 'graphs.json'}`);
      await loadGraph();
    } catch (err) {
      setStatus(err instanceof Error ? err.message : String(err));
    } finally {
      setBusy(false);
    }
  };

  const bridgeRpc = React.useCallback(async (method: string, params: Record<string, unknown>) => {
    const res = await fetch('/agent-bridge/rpc', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ jsonrpc: '2.0', id: Date.now(), method, params }),
    });
    const payload = await res.json();
    if (!res.ok || payload.error) throw new Error(payload.error?.message ?? `Bridge request failed (${res.status})`);
    return payload.result as Record<string, unknown>;
  }, []);

  const refreshDebugMounts = React.useCallback(async () => {
    try {
      const result = await bridgeRpc('ludots.graph.debug', { action: 'list' });
      const mounts = (result.mounts ?? []) as DebugMount[];
      const matching = mounts.filter((mount) => mount.graphName === graphId || String(mount.graphId) === graphId);
      setDebugMounts(matching);
      if (!debugEntryLabel && matching[0]) setDebugEntryLabel(matching[0].entryLabel);
      setDebugStatus(`Bridge: ${matching.length} mounted entry${matching.length === 1 ? '' : 'ies'}`);
    } catch (err) {
      setDebugStatus(err instanceof Error ? err.message : String(err));
    }
  }, [bridgeRpc, debugEntryLabel, graphId]);

  const pollDebug = React.useCallback(async () => {
    if (!debugEnabled || !debugEntryLabel) return;
    if (debugPollInFlight.current) return;
    debugPollInFlight.current = true;
    try {
      const result = await bridgeRpc('ludots.graph.debug', {
        action: 'drain', graphId, entryLabel: debugEntryLabel, since: debugSince, max: 128,
      });
      const incoming = (result.events ?? []) as DebugEvent[];
      if (result.gap) setDebugEvents([]);
      const latestSequence = Number(result.latestSequence ?? debugSince);
      if (incoming.length > 0) {
        setDebugEvents((previous) => [...previous, ...incoming].slice(-300));
      }
      if (latestSequence < debugSince) return;
      if (result.gap || incoming.length > 0) setDebugSince(latestSequence);
      setDebugStatus(`Live: ${String(result.mount ? (result.mount as DebugMount).cursor.status : 'unknown')} · ${incoming.length} changes`);
    } catch (err) {
      setDebugStatus(err instanceof Error ? err.message : String(err));
    } finally {
      debugPollInFlight.current = false;
    }
  }, [bridgeRpc, debugEnabled, debugEntryLabel, debugSince, graphId]);

  React.useEffect(() => {
    if (!debugEnabled) return undefined;
    void refreshDebugMounts();
    const timer = window.setInterval(() => { void pollDebug(); }, 250);
    return () => window.clearInterval(timer);
  }, [debugEnabled, pollDebug, refreshDebugMounts]);

  const toggleDebug = async () => {
    try {
      const entry = debugEntryLabel || debugMounts[0]?.entryLabel;
      if (!entry) throw new Error('No mounted entry selected. Refresh the bridge mount list first.');
      await bridgeRpc('ludots.graph.debug', { action: 'configure', graphId, entryLabel: entry, mode: debugEnabled ? 'off' : 'nodeAndPins' });
      setDebugEntryLabel(entry);
      setDebugSince(0);
      setDebugEvents([]);
      setDebugEnabled(!debugEnabled);
      setDebugStatus(debugEnabled ? 'Live debug off' : 'Live debug armed');
    } catch (err) {
      setDebugStatus(err instanceof Error ? err.message : String(err));
    }
  };

  const activeDebugNodes = React.useMemo(() => {
    const ids = new Set<string>();
    const latestEvent = debugEvents[debugEvents.length - 1];
    if (latestEvent?.nodeId) ids.add(latestEvent.nodeId);
    return ids;
  }, [debugEvents]);

  const displayNodes = React.useMemo(() => nodes.map((node) => ({
    ...node,
    style: activeDebugNodes.has(node.id)
      ? { ...node.style, border: '2px solid #facc15', boxShadow: '0 0 18px rgba(250,204,21,.45)' }
      : node.style,
  })), [activeDebugNodes, nodes]);

  return (
    <div className="flex h-screen w-screen flex-col bg-slate-950 text-slate-100">
      <header className="flex flex-wrap items-center gap-3 border-b border-slate-800 bg-slate-900 px-4 py-3">
        <div className="min-w-40">
          <div className="text-sm font-semibold text-white">Ludots Graph Editor</div>
          <div className="text-[10px] text-slate-500">Author contract · compiler diagnostics · live execution</div>
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
        <button
          type="button"
          disabled={busy || !currentGraph}
          onClick={() => void saveLayout()}
          className="rounded border border-slate-600 px-3 py-1 text-xs font-semibold text-slate-200 hover:bg-slate-800 disabled:opacity-50"
        >
          Save Layout
        </button>
        <button
          type="button"
          onClick={() => void refreshDebugMounts()}
          className="rounded border border-amber-700 px-3 py-1 text-xs font-semibold text-amber-200 hover:bg-amber-950"
        >
          Refresh Live
        </button>
        <div className="text-xs text-slate-400">{status}</div>
      </header>

      <div className="grid min-h-0 flex-1 grid-cols-[1fr_320px]">
        <div className="min-h-0">
          {graph ? (
            <div className="relative h-full">
              <ReactFlow
                nodes={displayNodes}
                edges={edges}
                nodeTypes={nodeTypes}
                onNodesChange={onNodesChange}
                onConnect={onConnect}
                onSelectionChange={({ nodes: selected, edges: selectedEdges }) => {
                  setSelectedNodeId(selected[0]?.id ?? null);
                  setSelectedEdgeId(selectedEdges[0]?.id ?? null);
                }}
                fitView
                proOptions={{ hideAttribution: true }}
              >
                <Background gap={16} color="#334155" />
                <Controls />
                <MiniMap pannable zoomable />
              </ReactFlow>
              <div className="absolute left-3 top-3 z-10 w-72 rounded border border-slate-700 bg-slate-950/95 p-2 shadow-xl">
                <div className="flex items-center gap-2 border-b border-slate-800 pb-2">
                  <Search size={14} className="text-slate-500" aria-hidden="true" />
                  <input
                    value={nodeSearch}
                    onChange={(event) => setNodeSearch(event.target.value)}
                    onKeyDown={(event) => {
                      if (event.key === 'Enter' && availableNodes[0]) addAuthoringNode(availableNodes[0].op);
                    }}
                    placeholder="Find node"
                    aria-label="Find graph node"
                    className="min-w-0 flex-1 bg-transparent text-xs text-slate-100 outline-none placeholder:text-slate-600"
                  />
                  <span className="text-[10px] text-slate-600">Enter</span>
                </div>
                <div className="mt-2 max-h-64 overflow-auto">
                  {availableNodes.slice(0, 24).map((entry) => (
                    <button
                      key={entry.op}
                      type="button"
                      onClick={() => addAuthoringNode(entry.op)}
                      className="flex w-full items-center gap-2 px-2 py-1.5 text-left text-xs text-slate-300 hover:bg-slate-800"
                    >
                      <Plus size={12} className="text-emerald-400" aria-hidden="true" />
                      <span className="font-mono">{entry.op}</span>
                      {entry.sugar ? <span className="ml-auto text-[10px] text-amber-300">sugar</span> : null}
                    </button>
                  ))}
                  {availableNodes.length === 0 ? <div className="px-2 py-2 text-xs text-slate-600">No runtime node matches.</div> : null}
                </div>
              </div>
            </div>
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
                {selectedData.descriptor ? (
                  <div className="rounded border border-slate-800 bg-slate-950/70 p-2">
                    <div className="mb-1 text-slate-500">Descriptor ports</div>
                    <div className="font-mono text-[10px] text-emerald-300">
                      in: {[...new Set([
                        ...selectedData.descriptor.linearInputPorts,
                        ...selectedData.descriptor.queryInputPorts,
                        ...selectedData.descriptor.scriptInputPorts,
                      ])].join(', ') || 'none'}
                    </div>
                    <div className="font-mono text-[10px] text-violet-300">
                      out: {selectedData.descriptor.queryOutputType !== 'Void'
                        ? selectedData.descriptor.queryOutputType
                        : selectedData.descriptor.linearOutputType}
                    </div>
                  </div>
                ) : null}
                {selectedData.op === 'SwitchInt' && graph && isControlFlowGraph(graph) ? (
                  <div className="space-y-2 rounded border border-sky-900 bg-sky-950/30 p-2">
                    <div className="text-sky-300">Switch cases</div>
                    <label className="block">
                      <div className="mb-1 text-slate-500">Case value</div>
                      <input
                        type="number"
                        step="1"
                        value={switchCaseValue}
                        onChange={(event) => setSwitchCaseValue(event.target.value)}
                        className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
                      />
                    </label>
                    <label className="block">
                      <div className="mb-1 text-slate-500">Target node</div>
                      <select
                        value={switchCaseTarget}
                        onChange={(event) => setSwitchCaseTarget(event.target.value)}
                        className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
                      >
                        <option value="">Select target</option>
                        {nodes.filter((node) => node.id !== selectedNodeId).map((node) => (
                          <option key={node.id} value={node.id}>{node.id}</option>
                        ))}
                      </select>
                    </label>
                    <button
                      type="button"
                      onClick={addSwitchCase}
                      className="w-full rounded bg-sky-700 px-2 py-1 font-semibold text-sky-50 hover:bg-sky-600"
                    >
                      Add case edge
                    </button>
                  </div>
                ) : null}
                {!graph || !isControlFlowGraph(graph) ? (
                  <label className="block">
                    <div className="mb-1 text-slate-500">Next</div>
                    <input
                      value={selectedData.next ?? ''}
                      onChange={(e) => updateSelectedField('next', e.target.value)}
                      className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
                    />
                  </label>
                ) : null}
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
            ) : selectedEdge ? (
              <>
                <div>
                  <div className="text-slate-500">Edge</div>
                  <div className="font-mono text-slate-100">{selectedEdge.data?.kind ?? 'edge'}</div>
                </div>
                <label className="block">
                  <div className="mb-1 text-slate-500">From node</div>
                  <input
                    value={selectedEdge.source}
                    onChange={(e) => updateSelectedEdgeField('source', e.target.value)}
                    className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
                  />
                </label>
                <label className="block">
                  <div className="mb-1 text-slate-500">From port</div>
                  <input
                    value={selectedEdge.sourceHandle ?? ''}
                    onChange={(e) => updateSelectedEdgeField('sourceHandle', e.target.value)}
                    className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
                  />
                </label>
                <label className="block">
                  <div className="mb-1 text-slate-500">To node</div>
                  <input
                    value={selectedEdge.target}
                    onChange={(e) => updateSelectedEdgeField('target', e.target.value)}
                    className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
                  />
                </label>
                {selectedEdge.data?.kind === 'value' ? (
                  <label className="block">
                    <div className="mb-1 text-slate-500">To port</div>
                    <input
                      value={selectedEdge.targetHandle ?? ''}
                      onChange={(e) => updateSelectedEdgeField('targetHandle', e.target.value)}
                      className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
                    />
                  </label>
                ) : null}
              </>
            ) : (
              <div className="text-slate-500">Select a node to edit TeamId / Attribute, or select an edge to edit pin endpoints.</div>
            )}
          </div>

          <div className="border-t border-slate-800 px-3 py-2 text-xs font-semibold uppercase tracking-wide text-slate-400">
            Diagnostics
          </div>
          <pre className="min-h-0 flex-1 overflow-auto whitespace-pre-wrap p-3 font-mono text-[11px] text-amber-200">
            {diagnosticsText || 'Validate or Save to run the Bridge compiler.'}
          </pre>

          <div className="border-t border-slate-800 px-3 py-2 text-xs font-semibold uppercase tracking-wide text-amber-300">
            Live Debug
          </div>
          <div className="space-y-2 border-t border-slate-800 p-3 text-xs">
            <div className="flex items-center gap-2">
              <select
                value={debugEntryLabel}
                onChange={(event) => { setDebugEntryLabel(event.target.value); setDebugSince(0); setDebugEvents([]); }}
                className="min-w-0 flex-1 rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono text-[11px]"
              >
                <option value="">Select mounted entry</option>
                {debugMounts.map((mount) => <option key={`${mount.graphName}:${mount.entryLabel}`} value={mount.entryLabel}>{mount.entryLabel} · {mount.event}</option>)}
              </select>
              <button type="button" onClick={() => void toggleDebug()} className="rounded bg-amber-700 px-2 py-1 font-semibold text-amber-50 hover:bg-amber-600">
                {debugEnabled ? 'Stop' : 'Watch'}
              </button>
            </div>
            <div className="text-[10px] text-slate-400">{debugStatus}</div>
            <div className="max-h-40 overflow-auto rounded border border-slate-800 bg-slate-950 p-2 font-mono text-[10px]">
              {debugEvents.length === 0 ? 'No trace changes yet.' : debugEvents.slice(-40).map((event) => (
                <div key={event.sequence} className={event.nodeId ? 'text-amber-200' : 'text-slate-400'}>
                  #{event.sequence} {event.event} {event.nodeId ?? `pc:${event.steps}`}{event.pinIndex !== undefined ? ` pin[${event.pinIndex}]=${String(event.value)}` : ''}
                </div>
              ))}
            </div>
          </div>
        </aside>
      </div>
    </div>
  );
};
