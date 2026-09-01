import React from 'react';
import { Link } from 'react-router-dom';
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  applyNodeChanges,
  applyEdgeChanges,
  addEdge,
  SelectionMode,
  type Node,
  type Edge,
  type Connection,
  type NodeChange,
  type EdgeChange,
  type ReactFlowInstance,
  MarkerType,
} from '@xyflow/react';
import { Search, Plus } from 'lucide-react';
import '@xyflow/react/dist/style.css';
import { GraphCatalogTree, type CatalogMod } from './gas-graph-editor/GraphCatalogTree';
import {
  GraphVariablePanel,
  decodeMapVarDrag,
  decodePlacedVarDrag,
  emptyVariableDraft,
  type GraphPlacedInstance,
  type GraphPlacedKind,
  type GraphVariableRow,
  type MapVariableDraft,
  type MapVariableScalarType,
} from './gas-graph-editor/GraphVariablePanel';
import { GasNode, isPureValueOp, type EventSchemaView } from './gas-graph-editor/GasNode';
import { authoredFieldsForOp, type AuthoredFieldKey } from './gas-graph-editor/authoredFields';
import { computeAutoLayout, eventEntryNodeId, isEventEntryNodeId } from './gas-graph-editor/autoLayout';
import { EventEntryInspector } from './gas-graph-editor/EventEntryInspector';
import { GraphCodegenPanel } from './gas-graph-editor/GraphCodegenPanel';
import {
  collectEventEntries,
  createEmptyEventEntry,
  entryLabelsFromNodes,
  eventThenEdgeId,
  uniqueEventLabel,
  type EventEntryConfig,
} from './gas-graph-editor/eventEntry';
import {
  applyLiveDebugToEdges,
  applyLiveDebugToNodes,
  applyWatchFocusToEdges,
  applyWatchFocusToNodes,
  computeLiveEdgeIds,
  computeLiveNodeHeat,
  computeLivePinValues,
  computeWatchedEntryFocus,
  type LiveDebugEvent,
} from './gas-graph-editor/liveVisualDebug';
import './gas-graph-editor/editor.css';

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
  payloadKey?: string | null;
  instanceId?: string | null;
  entryLabel?: string | null;
  event?: string | null;
  scope?: string | null;
  argKey?: string | null;
  enumType?: string | null;
  stateVar?: string | null;
  text?: string | null;
  textKey?: string | null;
  presentationSurface?: string | null;
  decoratorKind?: string | null;
  pinRegister?: number;
};

type GasNodeData = GraphNodeConfig & {
  label: string;
  role?: 'op' | 'event-entry';
  entry?: GraphEntryConfig;
  schema?: EventSchemaView | null;
  descriptor?: GraphDescriptor;
  sugar?: GraphSugarDescriptor;
  controlOutputPorts?: string[];
  liveDebug?: {
    intensity: number;
    current: boolean;
    pins: { pinIndex: number; value: string }[];
  };
};

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
  childArms?: boolean;
};

type EnumTypeView = {
  name: string;
  members: Array<{ name: string; value: number }>;
  source: string;
};

type TextKeyView = {
  id: string;
  argCount: number;
  source: string;
  preview?: string | null;
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
  executionBackend?: string;
  mode: string;
  latestSequence: number;
  cursor: { pc: number; steps: number; status: string; suspended: boolean };
};

type DebugEvent = LiveDebugEvent;

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
  synthetic?: boolean;
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
    payloadKey: n.payloadKey ?? undefined,
    instanceId: n.instanceId ?? undefined,
    entryLabel: n.entryLabel ?? undefined,
    event: n.event ?? undefined,
    scope: n.scope ?? undefined,
    argKey: n.argKey ?? undefined,
    enumType: n.enumType ?? undefined,
    stateVar: n.stateVar ?? undefined,
    text: n.text ?? undefined,
    textKey: n.textKey ?? undefined,
    presentationSurface: n.presentationSurface ?? undefined,
    decoratorKind: n.decoratorKind ?? undefined,
    pinRegister: n.pinRegister,
  });
}

function isControlFlowGraph(graph: GraphConfig): boolean {
  return Array.isArray(graph.controlEdges) || Array.isArray(graph.valueEdges);
}

// #1115: DispatchMapEvent payload ports are dynamic — one per non-String schema
// parameter, named after the parameter (mirrors the event-entry String filter).
function dispatchParamPorts(
  op: string,
  event: string | null | undefined,
  schemaFor: (event: string) => EventSchemaView | null,
): { op: string; controlOutputPorts: string[]; valueInputPorts: string[]; outputType: string; lowersTo: string } | null {
  if (op !== 'DispatchMapEvent' || !event) return null;
  const schema = schemaFor(event);
  const ports = schema
    ? schema.parameters.filter((param) => param.type !== 'String').map((param) => param.name)
    : [];
  return { op, controlOutputPorts: ['next'], valueInputPorts: ports, outputType: 'Void', lowersTo: 'DispatchMapEvent' };
}

/** FormatText brace auto-pins: `{0}` / `{name}` → arg:0 / arg:name Text inputs. */
function formatTextPorts(
  op: string,
  text: string | null | undefined,
): { op: string; controlOutputPorts: string[]; valueInputPorts: string[]; outputType: string; lowersTo: string } | null {
  if (op !== 'FormatText') return null;
  const ports: string[] = [];
  const seen = new Set<string>();
  const source = text ?? '';
  for (let i = 0; i < source.length; i++) {
    const ch = source[i];
    if (ch === '{' && source[i + 1] === '{') {
      i++;
      continue;
    }
    if (ch === '}' && source[i + 1] === '}') {
      i++;
      continue;
    }
    if (ch !== '{') continue;
    const close = source.indexOf('}', i + 1);
    if (close < 0) break;
    const raw = source.slice(i + 1, close);
    if (!raw || raw.includes(':')) {
      i = close;
      continue;
    }
    const port = `arg:${raw}`;
    if (!seen.has(port)) {
      seen.add(port);
      ports.push(port);
    }
    i = close;
  }
  return { op, controlOutputPorts: ['next'], valueInputPorts: ports, outputType: 'Text', lowersTo: 'ConcatText' };
}

function resolveControlOutputPorts(op: string, descriptor?: GraphDescriptor, sugar?: GraphSugarDescriptor): string[] {
  if (isPureValueOp(op)) return [];
  if (sugar) return sugar.controlOutputPorts;
  if (descriptor) return descriptor.controlOutputPorts;
  throw new Error(`Descriptor missing for graph op '${op}'.`);
}

function edgeLabel(edge: Edge<GasEdgeData>): string {
  if (edge.data?.kind === 'control') return String(edge.sourceHandle ?? '');
  return `${String(edge.sourceHandle ?? '')} -> ${String(edge.targetHandle ?? '')}`;
}

function opForEventPin(handle: string, schema?: EventSchemaView | null): { op: string; payloadKey?: string } | null {
  if (handle === 'owner') return { op: 'LoadExplicitTarget' };
  if (handle === 'caster') return { op: 'LoadCaster' };
  if (handle.startsWith('payload:')) {
    const key = handle.slice('payload:'.length);
    const param = schema?.parameters.find((candidate) => candidate.key === key);
    if (!param) return null;
    if (param.type === 'Entity') return { op: 'LoadEntryPayloadEntity', payloadKey: key };
    if (param.type === 'Int') return { op: 'LoadEntryPayloadInt', payloadKey: key };
    if (param.type === 'Float') return { op: 'LoadEntryPayloadFloat', payloadKey: key };
    return null;
  }
  return null;
}

function graphToFlow(
  graph: GraphConfig,
  descriptors: Record<string, GraphDescriptor> = {},
  sugars: Record<string, GraphSugarDescriptor> = {},
  layout: EditorLayout = {},
  schemaFor: (event: string) => EventSchemaView | null = () => null,
): { nodes: Node<GasNodeData>[]; edges: Edge<GasEdgeData>[] } {
  const dynamicControlPorts = new Map<string, string[]>();
  for (const edge of graph.controlEdges ?? []) {
    if (!edge.fromPort.startsWith('case:') && !edge.fromPort.startsWith('child:')) continue;
    const ports = dynamicControlPorts.get(edge.from) ?? [];
    if (!ports.includes(edge.fromPort)) ports.push(edge.fromPort);
    dynamicControlPorts.set(edge.from, ports);
  }
  const nodes: Node<GasNodeData>[] = graph.nodes.map((n, index) => {
    const dispatchSugar = dispatchParamPorts(n.op, n.event, schemaFor);
    const formatSugar = formatTextPorts(n.op, n.text);
    const dynamicSugar = dispatchSugar ?? formatSugar;
    return {
    id: n.id,
    type: 'gas',
    position: layout.nodes?.[n.id] ?? { x: 40 + index * 220, y: 80 + (index % 2) * 40 },
    data: {
      ...n,
      role: 'op',
      label: n.id,
      descriptor: descriptors[n.op],
      sugar: dynamicSugar ?? sugars[n.op],
      controlOutputPorts: dynamicSugar
        ? dynamicSugar.controlOutputPorts
        : [...resolveControlOutputPorts(n.op, descriptors[n.op], sugars[n.op]), ...(dynamicControlPorts.get(n.id) ?? [])],
    },
  };
  });

  const startIndex = new Map<string, number>();
  for (const entry of graph.entries ?? []) {
    const count = startIndex.get(entry.start) ?? 0;
    const id = eventEntryNodeId(entry.label);
    const startNode = nodes.find((node) => node.id === entry.start);
    const fallback = startNode?.position ?? { x: 40, y: 80 + count * 140 };
    nodes.push({
      id,
      type: 'gas',
      deletable: true,
      selectable: true,
      position: layout.nodes?.[id] ?? { x: fallback.x - 280, y: fallback.y + count * 28 },
      data: {
        id,
        op: 'Event',
        role: 'event-entry',
        entry,
        schema: schemaFor(entry.event),
        label: entry.event,
        controlOutputPorts: ['exec'],
      },
    });
    startIndex.set(entry.start, count + 1);
  }

  const edges: Edge<GasEdgeData>[] = [];
  for (const entry of graph.entries ?? []) {
    const source = eventEntryNodeId(entry.label);
    if (!nodes.some((node) => node.id === entry.start)) continue;
    edges.push({
      id: `c:${source}:exec:${entry.start}`,
      source,
      sourceHandle: 'exec',
      target: entry.start,
      targetHandle: 'control-in',
      markerEnd: { type: MarkerType.ArrowClosed },
      style: { stroke: '#fb7185', strokeWidth: 2 },
      data: { kind: 'control', synthetic: true },
    });
  }

  if (isControlFlowGraph(graph)) {
    for (const edge of graph.controlEdges ?? []) {
      edges.push({
        id: `c:${edge.from}:${edge.fromPort}:${edge.to}`,
        source: edge.from,
        sourceHandle: edge.fromPort,
        target: edge.to,
        targetHandle: 'control-in',
        markerEnd: { type: MarkerType.ArrowClosed },
        style: { stroke: '#38bdf8', strokeWidth: 2 },
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
        style: { stroke: edge.fromPort === 'list' ? '#34d399' : '#a78bfa', strokeWidth: 2 },
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

function toDisplayEdges(nodes: Node<GasNodeData>[], edges: Edge<GasEdgeData>[]): Edge<GasEdgeData>[] {
  const opById = new Map(nodes.map((node) => [node.id, node.data.op]));
  const isConst = (id: string) => isPureValueOp(opById.get(id) ?? '');
  const control = edges.filter((edge) => edge.data?.kind === 'control');
  const visible: Edge<GasEdgeData>[] = edges.filter((edge) => edge.data?.kind !== 'control');
  const outgoing = new Map<string, Edge<GasEdgeData>[]>();
  for (const edge of control) {
    const list = outgoing.get(edge.source) ?? [];
    list.push(edge);
    outgoing.set(edge.source, list);
  }

  const terminalsAfter = (id: string, visited: Set<string>): string[] => {
    if (!isConst(id)) return [id];
    if (visited.has(id)) return [];
    visited.add(id);
    const next = outgoing.get(id) ?? [];
    if (next.length === 0) return [];
    return next.flatMap((edge) => terminalsAfter(edge.target, visited));
  };

  const emitted = new Set<string>();
  const hasReal = (source: string, sourceHandle: string | null | undefined, target: string) =>
    control.some((edge) =>
      edge.source === source &&
      edge.sourceHandle === sourceHandle &&
      edge.target === target &&
      !isConst(edge.source) &&
      !isConst(edge.target));

  for (const edge of control) {
    if (!isConst(edge.source) && !isConst(edge.target)) {
      visible.push(edge);
      continue;
    }
    if (isConst(edge.source)) continue;
    for (const terminal of terminalsAfter(edge.target, new Set())) {
      if (terminal === edge.source || hasReal(edge.source, edge.sourceHandle, terminal)) continue;
      const id = `c:${edge.source}:${edge.sourceHandle ?? ''}:${terminal}:value-bypass`;
      if (emitted.has(id)) continue;
      emitted.add(id);
      visible.push({
        id,
        source: edge.source,
        sourceHandle: edge.sourceHandle,
        target: terminal,
        targetHandle: 'control-in',
        markerEnd: { type: MarkerType.ArrowClosed },
        style: { stroke: '#38bdf8', strokeWidth: 2 },
        deletable: false,
        selectable: false,
        data: { kind: 'control', synthetic: true },
      });
    }
  }

  return visible;
}

function flowToGraph(graph: GraphConfig, nodes: Node<GasNodeData>[], edges: Edge<GasEdgeData>[]): GraphConfig {
  const sourceById = new Map(graph.nodes.map((n) => [n.id, n]));
  const wireNodes = nodes
    .filter((flowNode) => flowNode.data.role !== 'event-entry' && !isEventEntryNodeId(flowNode.id))
    .map((flowNode) => {
    const n = sourceById.get(flowNode.id);
    const edited = flowNode.data;
    if (!n) {
      const created = { ...edited };
      delete created.label;
      delete created.descriptor;
      delete created.sugar;
      delete created.role;
      delete created.entry;
      delete created.schema;
      delete created.liveDebug;
      delete created.controlOutputPorts;
      return toWireNode({ ...created, id: flowNode.id, op: edited.op });
    }
    const rest = { ...edited };
    delete rest.label;
    delete rest.descriptor;
    delete rest.sugar;
    delete rest.controlOutputPorts;
    delete rest.role;
    delete rest.entry;
    delete rest.schema;
    delete rest.liveDebug;
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
      entries: collectEventEntries(nodes, edges),
      nodes: wireNodes.map((n) => {
        const rest = { ...n };
        delete rest.next;
        delete rest.inputs;
        return rest;
      }),
      controlEdges: edges
        .filter((edge) => edge.data?.kind === 'control' && !edge.data.synthetic && !isEventEntryNodeId(edge.source))
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
    entries: collectEventEntries(nodes, edges),
    nodes: wireNodes,
  };
}

function readEditorSelection(): { modId: string; graphId: string } {
  const params = new URLSearchParams(window.location.search);
  return {
    modId: params.get('mod')?.trim() || DEFAULT_MOD_ID,
    graphId: params.get('graph')?.trim() || DEFAULT_GRAPH_ID,
  };
}

export const GasGraphEditorPage: React.FC = () => {
  const initialSelection = React.useMemo(() => readEditorSelection(), []);
  const [modId, setModId] = React.useState(initialSelection.modId);
  const [graphId, setGraphId] = React.useState(initialSelection.graphId);
  const [graph, setGraph] = React.useState<GraphConfig | null>(null);
  const [descriptors, setDescriptors] = React.useState<Record<string, GraphDescriptor>>({});
  const [sugars, setSugars] = React.useState<Record<string, GraphSugarDescriptor>>({});
  const [panelAnchors, setPanelAnchors] = React.useState<string[]>([]);
  const [payloadKeys, setPayloadKeys] = React.useState<string[]>([]);
  const [eventSchemas, setEventSchemas] = React.useState<EventSchemaView[]>([]);
  const [enumCatalog, setEnumCatalog] = React.useState<EnumTypeView[]>([]);
  const [textKeyCatalog, setTextKeyCatalog] = React.useState<TextKeyView[]>([]);
  const [mapInstances, setMapInstances] = React.useState<GraphPlacedInstance[]>([]);
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
  const [btChildTarget, setBtChildTarget] = React.useState('');
  const [catalog, setCatalog] = React.useState<CatalogMod[]>([]);
  const [catalogStatus, setCatalogStatus] = React.useState('Loading catalog…');
  const [paletteMenu, setPaletteMenu] = React.useState<{ clientX: number; clientY: number; flowX: number; flowY: number } | null>(null);
  const [selectedVariable, setSelectedVariable] = React.useState<string | null>(null);
  const [declaredVariables, setDeclaredVariables] = React.useState<GraphVariableRow[]>([]);
  const [variableMapId, setVariableMapId] = React.useState<string | null>(null);
  const [variableStatus, setVariableStatus] = React.useState('Variables idle');
  const [variableDraft, setVariableDraft] = React.useState<MapVariableDraft>(emptyVariableDraft());
  const [variableBusy, setVariableBusy] = React.useState(false);
  const [varDropMenu, setVarDropMenu] = React.useState<{
    clientX: number;
    clientY: number;
    flowX: number;
    flowY: number;
    name: string;
    type: MapVariableScalarType;
    placed: boolean;
    placedKind?: GraphPlacedKind;
  } | null>(null);
  const debugPollInFlight = React.useRef(false);
  const reactFlowRef = React.useRef<ReactFlowInstance | null>(null);

  const selectedNode = React.useMemo(
    () => nodes.find((n) => n.id === selectedNodeId) ?? null,
    [nodes, selectedNodeId],
  );

  const selectedData = selectedNode?.data ?? null;
  const selectedEdge = React.useMemo(
    () => edges.find((e) => e.id === selectedEdgeId) ?? null,
    [edges, selectedEdgeId],
  );

  const loadMapVariables = React.useCallback(async (targetGraphId: string) => {
    const res = await fetch(`/api/mods/${encodeURIComponent(modId)}/gas/graphs/${encodeURIComponent(targetGraphId)}/map-variables`);
    const payload = await res.json();
    if (!res.ok || !payload.ok || !Array.isArray(payload.maps)) {
      throw new Error(payload.error ?? `Map variable load failed (${res.status})`);
    }
    const hosts = payload.maps as Array<{
      mapId: string;
      variables: Array<{ name: string; type: string; initial: number }>;
    }>;
    if (hosts.length === 0) {
      setDeclaredVariables([]);
      setVariableMapId(null);
      setVariableDraft(emptyVariableDraft());
      setVariableStatus('This graph is not mounted on a map, so it has no map variables.');
      return;
    }
    const signature = (host: (typeof hosts)[0]) => JSON.stringify(host.variables);
    if (hosts.length > 1 && hosts.some((host) => signature(host) !== signature(hosts[0]!))) {
      setDeclaredVariables([]);
      setVariableMapId(null);
      setVariableDraft(emptyVariableDraft());
      setVariableStatus(`This graph is mounted on ${hosts.map((host) => host.mapId).join(', ')} with different variable lists. Refuse to edit until those maps agree.`);
      return;
    }
    const host = hosts[0]!;
    const rows: GraphVariableRow[] = host.variables.map((variable) => {
      if (variable.type !== 'int' && variable.type !== 'float') {
        throw new Error(`Map '${host.mapId}' variable '${variable.name}' has unsupported type '${variable.type}'.`);
      }
      return {
        name: variable.name,
        type: variable.type,
        initial: variable.initial,
        declared: true,
        reads: 0,
        writes: 0,
      };
    });
    setDeclaredVariables(rows);
    setVariableMapId(host.mapId);
    setSelectedVariable(rows[0]?.name ?? null);
    setVariableDraft(rows[0]
      ? {
          name: rows[0].name,
          kind: rows[0].type,
          initial: String(rows[0].initial),
        }
      : emptyVariableDraft());
    setVariableStatus(hosts.length > 1
      ? `Shared variables from ${hosts.map((item) => item.mapId).join(', ')}.`
      : `Loaded ${rows.length} variables from ${host.mapId}.`);
  }, [modId]);

  const schemaFor = React.useCallback(
    (event: string): EventSchemaView | null => eventSchemas.find((schema) => schema.name === event) ?? null,
    [eventSchemas],
  );

  // Graph fetch must not re-run when schemas arrive after the first load; the
  // [schemaFor] effect below patches already-placed event nodes instead.
  const schemaForRef = React.useRef(schemaFor);
  React.useEffect(() => {
    schemaForRef.current = schemaFor;
  }, [schemaFor]);

  const loadEventSchemas = React.useCallback(async () => {
    try {
      const res = await fetch(`/api/graph/event-schemas/${encodeURIComponent(modId)}`);
      const payload = await res.json();
      if (!res.ok || !payload.ok || !Array.isArray(payload.schemas)) {
        throw new Error(payload.error ?? `Event schema load failed (${res.status})`);
      }
      setEventSchemas(payload.schemas as EventSchemaView[]);
    } catch (err) {
      setEventSchemas([]);
      setStatus(`Event schemas unavailable: ${err instanceof Error ? err.message : String(err)}`);
    }
  }, [modId]);

  React.useEffect(() => {
    void loadEventSchemas();
  }, [loadEventSchemas]);

  // #1125: launcher-wide enum vocabulary — feeds the SwitchInt enumType picker and the
  // case:{member} dropdown once a node is bound.
  const loadEnumCatalog = React.useCallback(async () => {
    try {
      const res = await fetch(`/api/graph/enums/${encodeURIComponent(modId)}`);
      const payload = await res.json();
      if (!res.ok || !payload.ok || !Array.isArray(payload.enums)) {
        throw new Error(payload.error ?? `Enum catalog load failed (${res.status})`);
      }
      setEnumCatalog(payload.enums as EnumTypeView[]);
    } catch (err) {
      setEnumCatalog([]);
      setStatus(`Enum catalog unavailable: ${err instanceof Error ? err.message : String(err)}`);
    }
  }, [modId]);

  React.useEffect(() => {
    void loadEnumCatalog();
  }, [loadEnumCatalog]);

  const loadTextKeyCatalog = React.useCallback(async () => {
    try {
      const res = await fetch(`/api/graph/text-keys/${encodeURIComponent(modId)}`);
      const payload = await res.json();
      if (!res.ok || !payload.ok || !Array.isArray(payload.textKeys)) {
        throw new Error(payload.error ?? `Text key catalog load failed (${res.status})`);
      }
      setTextKeyCatalog(payload.textKeys as TextKeyView[]);
    } catch (err) {
      setTextKeyCatalog([]);
      setStatus(`Text key catalog unavailable: ${err instanceof Error ? err.message : String(err)}`);
    }
  }, [modId]);

  React.useEffect(() => {
    void loadTextKeyCatalog();
  }, [loadTextKeyCatalog]);

  React.useEffect(() => {
    setNodes((previous) => previous.map((node) => {
      if (node.data.role === 'event-entry') {
        return { ...node, data: { ...node.data, schema: schemaFor(node.data.entry?.event ?? '') } };
      }

      if (node.data.op === 'DispatchMapEvent') {
        const dispatchSugar = dispatchParamPorts(node.data.op, node.data.event, schemaFor);
        if (!dispatchSugar) return node;
        return {
          ...node,
          data: { ...node.data, sugar: dispatchSugar, controlOutputPorts: dispatchSugar.controlOutputPorts },
        };
      }

      if (node.data.op === 'FormatText') {
        const formatSugar = formatTextPorts(node.data.op, node.data.text);
        if (!formatSugar) return node;
        return {
          ...node,
          data: { ...node.data, sugar: formatSugar, controlOutputPorts: formatSugar.controlOutputPorts },
        };
      }

      return node;
    }));
  }, [schemaFor]);

  React.useEffect(() => {
    if (!variableMapId) {
      setMapInstances([]);
      return;
    }
    let cancelled = false;
    void (async () => {
      try {
        const res = await fetch(`/api/mods/${encodeURIComponent(modId)}/maps/${encodeURIComponent(variableMapId)}/instances`);
        const payload = await res.json();
        if (cancelled) return;
        if (!res.ok || !payload.ok || !Array.isArray(payload.instances)) {
          throw new Error(payload.error ?? `Instance list load failed (${res.status})`);
        }
        // Keep the full row (instanceId + template); template feeds the panel tooltip and
        // the ordinal keeps the Placed section in the endpoint's authored order (#1108).
        setMapInstances((payload.instances as Array<{ instanceId: string; template?: string; kind?: string }>).map((instance, ordinal) => {
          const kindRaw = instance.kind;
          const kind: GraphPlacedKind =
            kindRaw === 'anchor' || kindRaw === 'region' || kindRaw === 'entity'
              ? kindRaw
              : (instance.instanceId.toLowerCase().includes('anchor') ? 'anchor' : 'entity');
          return {
            instanceId: instance.instanceId,
            template: instance.template ?? '',
            kind,
            ordinal,
          };
        }));
      } catch {
        if (!cancelled) setMapInstances([]);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [modId, variableMapId]);

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
      if (descriptorPayload.panelAnchors != null && !Array.isArray(descriptorPayload.panelAnchors)) {
        throw new Error('Descriptor response is missing panel anchors.');
      }
      const nextAnchors = (descriptorPayload.panelAnchors ?? []) as string[];
      setDescriptors(nextDescriptors);
      setSugars(nextSugars);
      setPanelAnchors(nextAnchors);
      setPayloadKeys(Array.isArray(descriptorPayload.payloadKeys) ? descriptorPayload.payloadKeys as string[] : []);
      setGraph(loaded);
      const flow = graphToFlow(loaded, nextDescriptors, nextSugars, nextLayout, schemaForRef.current);
      const hasSavedPositions = Object.keys(nextLayout.nodes ?? {}).length > 0;
      if (!hasSavedPositions) {
        const positions = computeAutoLayout(flow.nodes, flow.edges);
        flow.nodes = flow.nodes.map((node) => ({
          ...node,
          position: positions[node.id] ?? node.position,
        }));
        setLayout({ ...nextLayout, nodes: positions });
      } else {
        setLayout(nextLayout);
      }
      setNodes(flow.nodes);
      setEdges(flow.edges);
      setSelectedNodeId(null);
      setSelectedEdgeId(null);
      setSelectedVariable(null);
      setStatus(`Loaded ${loaded.id} (${loaded.kind})`);
      await loadMapVariables(loaded.id);
    } catch (err) {
      setGraph(null);
      setNodes([]);
      setEdges([]);
      setDeclaredVariables([]);
      setVariableMapId(null);
      setStatus(err instanceof Error ? err.message : String(err));
    } finally {
      setBusy(false);
    }
  }, [loadMapVariables, modId, graphId]);

  React.useEffect(() => {
    void loadGraph();
  }, [loadGraph]);

  React.useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    params.set('mod', modId);
    params.set('graph', graphId);
    const next = `${window.location.pathname}?${params.toString()}`;
    window.history.replaceState(null, '', next);
  }, [graphId, modId]);

  const loadCatalog = React.useCallback(async () => {
    try {
      const res = await fetch('/api/gas/graph-catalog');
      const payload = await res.json();
      if (!res.ok || !payload.ok || !Array.isArray(payload.mods)) {
        throw new Error(payload.error ?? `Catalog load failed (${res.status})`);
      }
      setCatalog(payload.mods as CatalogMod[]);
      setCatalogStatus(`Loaded ${payload.mods.length} mods with graphs`);
    } catch (err) {
      setCatalog([]);
      setCatalogStatus(err instanceof Error ? err.message : String(err));
    }
  }, []);

  React.useEffect(() => {
    void loadCatalog();
  }, [loadCatalog]);

  React.useEffect(() => {
    if (!paletteMenu && !varDropMenu) return undefined;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setPaletteMenu(null);
        setVarDropMenu(null);
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [paletteMenu, varDropMenu]);

  const openPaletteAt = React.useCallback((clientX: number, clientY: number) => {
    const flow = reactFlowRef.current?.screenToFlowPosition({ x: clientX, y: clientY }) ?? {
      x: 80 + (nodes.length % 3) * 240,
      y: 120 + Math.floor(nodes.length / 3) * 150,
    };
    setPaletteMenu({ clientX, clientY, flowX: flow.x, flowY: flow.y });
    setNodeSearch('');
  }, [nodes.length]);

  const currentGraph = React.useMemo(() => {
    if (!graph) return null;
    return flowToGraph(graph, nodes, edges);
  }, [graph, nodes, edges]);

  const updateSelectedField = (field: AuthoredFieldKey | 'next', value: string | boolean) => {
    if (!selectedNodeId || !graph) return;
    const nextNodes = nodes.map((n) => {
      if (n.id !== selectedNodeId) return n;
      const data: GasNodeData = { ...n.data };
      if (field === 'next') {
        data.next = String(value).trim() === '' ? null : String(value).trim();
      } else if (field === 'boolValue') {
        data.boolValue = Boolean(value);
      } else if (field === 'intValue' || field === 'teamId' || field === 'graphId') {
        const parsed = Number.parseInt(String(value), 10);
        data[field] = Number.isInteger(parsed) ? parsed : 0;
      } else if (field === 'floatValue' || field === 'panelZOrder') {
        const parsed = Number.parseFloat(String(value));
        data[field] = Number.isFinite(parsed) ? parsed : 0;
      } else {
        const text = String(value);
        data[field] = text.trim() === '' ? null : text;
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

  const onEdgesChange = React.useCallback((changes: EdgeChange<Edge<GasEdgeData>>[]) => {
    const removed = new Set(
      changes.filter((change) => change.type === 'remove').map((change) => change.id),
    );
    if (removed.size > 0) {
      const cleared = new Set(
        edges
          .filter((edge) => removed.has(edge.id) && isEventEntryNodeId(edge.source))
          .map((edge) => edge.source),
      );
      if (cleared.size > 0) {
        setNodes((previous) => previous.map((node) => (
          cleared.has(node.id) && node.data.entry
            ? { ...node, data: { ...node.data, entry: { ...node.data.entry, start: '' } } }
            : node
        )));
      }
    }
    setEdges((prev) => applyEdgeChanges(changes, prev));
  }, [edges]);

  const onConnect = React.useCallback((connection: Connection) => {
    if (!connection.source || !connection.target || !connection.sourceHandle || !connection.targetHandle) return;
    const kind: GasEdgeData['kind'] = connection.targetHandle === 'control-in' ? 'control' : 'value';
    const sourceNode = nodes.find((node) => node.id === connection.source);
    const sourceOp = sourceNode?.data.op ?? '';
    const targetOp = nodes.find((node) => node.id === connection.target)?.data.op ?? '';
    if (kind === 'control' && (isPureValueOp(sourceOp) || isPureValueOp(targetOp))) {
      setStatus('Const nodes only carry values. Wire Then around them, not through them.');
      return;
    }
    if (sourceNode?.data.role === 'event-entry') {
      if (connection.sourceHandle !== 'exec') {
        const pin = opForEventPin(connection.sourceHandle, sourceNode.data.schema);
        if (!pin) {
          setStatus('This pin has no runtime op yet (string payloads wait on the text value contract).');
          return;
        }
        const targetNode = nodes.find((node) => node.id === connection.target);
        const position = {
          x: (targetNode?.position.x ?? 300) - 230,
          y: (targetNode?.position.y ?? 200) + 48,
        };
        const base = pin.op.replace(/[^A-Za-z0-9_]/g, '_').toLocaleLowerCase();
        let nodeId = base;
        let suffix = 1;
        const used = new Set(nodes.map((node) => node.id));
        while (used.has(nodeId)) {
          suffix += 1;
          nodeId = `${base}_${suffix}`;
        }
        setNodes((previous) => [...previous, {
          id: nodeId,
          type: 'gas',
          position,
          data: {
            id: nodeId,
            op: pin.op,
            role: 'op',
            label: nodeId,
            payloadKey: pin.payloadKey ?? null,
            descriptor: descriptors[pin.op],
            sugar: sugars[pin.op],
            controlOutputPorts: resolveControlOutputPorts(pin.op, descriptors[pin.op], sugars[pin.op]),
          },
        }]);
        setEdges((prev) => addEdge({
          id: `value:${nodeId}:value:${connection.target}:${connection.targetHandle}`,
          source: nodeId,
          sourceHandle: 'value',
          target: connection.target,
          targetHandle: connection.targetHandle,
          markerEnd: { type: MarkerType.ArrowClosed },
          style: { stroke: '#a78bfa', strokeWidth: 2 },
          data: { kind: 'value' },
        }, prev));
        setSelectedNodeId(nodeId);
        setStatus(`Placed ${pin.op}${pin.payloadKey ? ` for ${pin.payloadKey}` : ''}.`);
        return;
      }
      if (kind !== 'control') return;
      setEdges((prev) => addEdge({
        id: eventThenEdgeId(connection.source!, connection.target!),
        source: connection.source,
        sourceHandle: 'exec',
        target: connection.target,
        targetHandle: 'control-in',
        markerEnd: { type: MarkerType.ArrowClosed },
        style: { stroke: '#fb7185', strokeWidth: 2 },
        data: { kind: 'control', synthetic: true },
      }, prev.filter((edge) => !(edge.source === connection.source && edge.sourceHandle === 'exec'))));
      setNodes((previous) => previous.map((node) => (
        node.id === connection.source && node.data.entry
          ? { ...node, data: { ...node.data, entry: { ...node.data.entry, start: connection.target! } } }
          : node
      )));
      return;
    }
    setEdges((prev) => addEdge({
      ...connection,
      id: `${kind}:${connection.source}:${connection.sourceHandle}:${connection.target}:${connection.targetHandle}`,
      markerEnd: { type: MarkerType.ArrowClosed },
      style: { stroke: kind === 'control' ? '#38bdf8' : '#a78bfa', strokeWidth: 2 },
      data: { kind },
    }, prev));
  }, [nodes, descriptors, sugars]);

  const applyAutoLayout = React.useCallback(() => {
    const positions = computeAutoLayout(nodes, edges);
    setNodes((previous) => previous.map((node) => ({
      ...node,
      position: positions[node.id] ?? node.position,
    })));
    setLayout((previous) => ({
      ...previous,
      nodes: {
        ...previous.nodes,
        ...positions,
      },
    }));
    window.requestAnimationFrame(() => {
      window.requestAnimationFrame(() => {
        reactFlowRef.current?.fitView({ padding: 0.16, duration: 240, minZoom: 0.12, maxZoom: 1.75 });
      });
    });
    setStatus('Auto-arranged. Save Layout to keep it.');
  }, [edges, nodes]);

  const addSwitchCase = React.useCallback(() => {
    if (!selectedNodeId || !graph || !isControlFlowGraph(graph)) return;
    const op = selectedData?.op;
    if (op !== 'SwitchInt' && op !== 'FsmState') return;
    const boundEnum = selectedData.enumType
      ? enumCatalog.find((candidate) => candidate.name === selectedData.enumType)
      : null;
    if (op === 'FsmState' && !boundEnum) {
      setStatus('FsmState case arms require a bound enumType.');
      return;
    }
    let sourceHandle: string;
    if (boundEnum) {
      // Enum-bound arms author member names; the compiler resolves them to values.
      if (!boundEnum.members.some((member) => member.name === switchCaseValue)) {
        setStatus(`'${switchCaseValue}' is not a member of enum '${boundEnum.name}'.`);
        return;
      }
      sourceHandle = `case:${switchCaseValue}`;
    } else {
      const caseValue = Number.parseInt(switchCaseValue, 10);
      if (!Number.isInteger(caseValue) || !switchCaseTarget || !nodes.some((node) => node.id === switchCaseTarget)) {
        setStatus('SwitchInt case requires an integer value and an existing target node.');
        return;
      }
      sourceHandle = `case:${caseValue}`;
    }
    if (!switchCaseTarget || !nodes.some((node) => node.id === switchCaseTarget)) {
      setStatus(`${op} case requires an existing target node.`);
      return;
    }
    if (edges.some((edge) => edge.source === selectedNodeId && edge.sourceHandle === sourceHandle)) {
      setStatus(`${op} case '${sourceHandle}' already exists.`);
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
    setStatus(`Added ${op} ${sourceHandle} -> ${switchCaseTarget}.`);
  }, [edges, enumCatalog, graph, nodes, selectedData?.enumType, selectedData?.op, selectedNodeId, switchCaseTarget, switchCaseValue]);

  const addBtChildArm = React.useCallback(() => {
    if (!selectedNodeId || !graph || !isControlFlowGraph(graph)) return;
    const op = selectedData?.op;
    if (!op || !sugars[op]?.childArms) return;
    if (!btChildTarget || !nodes.some((node) => node.id === btChildTarget)) {
      setStatus(`${op} child arm requires an existing target node.`);
      return;
    }
    let nextIndex = 0;
    for (const port of selectedData.controlOutputPorts ?? []) {
      if (!port.startsWith('child:')) continue;
      const parsed = Number.parseInt(port.slice('child:'.length), 10);
      if (Number.isInteger(parsed) && parsed >= nextIndex) nextIndex = parsed + 1;
    }
    const sourceHandle = `child:${nextIndex}`;
    if (edges.some((edge) => edge.source === selectedNodeId && edge.sourceHandle === sourceHandle)) {
      setStatus(`${op} ${sourceHandle} already exists.`);
      return;
    }
    setEdges((previous) => addEdge({
      id: `control:${selectedNodeId}:${sourceHandle}:${btChildTarget}:control-in`,
      source: selectedNodeId,
      sourceHandle,
      target: btChildTarget,
      targetHandle: 'control-in',
      markerEnd: { type: MarkerType.ArrowClosed },
      label: sourceHandle,
      data: { kind: 'control' },
    }, previous));
    setNodes((previous) => previous.map((node) => node.id !== selectedNodeId
      ? node
      : { ...node, data: { ...node.data, controlOutputPorts: [...new Set([...(node.data.controlOutputPorts ?? []), sourceHandle])] } }));
    setStatus(`Added ${op} ${sourceHandle} -> ${btChildTarget}.`);
  }, [btChildTarget, edges, graph, nodes, selectedData?.controlOutputPorts, selectedData?.op, selectedNodeId, sugars]);

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

  const addAuthoringNode = React.useCallback((op: string, position?: { x: number; y: number }, extras?: { var?: string; instanceId?: string }) => {
    if (!graph) return;
    if (!descriptors[op] && !sugars[op]) {
      setStatus(`Cannot add '${op}': this graph kind has no runtime descriptor for it.`);
      return;
    }
    const idBase = extras?.var ?? extras?.instanceId
      ? `${op.startsWith('Write') ? 'set' : 'get'}_${extras.var ?? extras.instanceId}`.replace(/[^A-Za-z0-9_]/g, '_').toLocaleLowerCase()
      : op.replace(/[^A-Za-z0-9_]/g, '_').toLocaleLowerCase();
    const used = new Set(nodes.map((node) => node.id));
    let suffix = 1;
    let id = idBase;
    while (used.has(id)) id = `${idBase}_${suffix++}`;
    const next: Node<GasNodeData> = {
      id,
      type: 'gas',
      position: position ?? { x: 80 + (nodes.length % 3) * 240, y: 120 + Math.floor(nodes.length / 3) * 150 },
      data: {
        id,
        op,
        role: 'op',
        label: id,
        var: extras?.var ?? null,
        instanceId: extras?.instanceId ?? null,
        descriptor: descriptors[op],
        sugar: sugars[op],
        controlOutputPorts: resolveControlOutputPorts(op, descriptors[op], sugars[op]),
      },
    };
    setNodes((previous) => [...previous, next]);
    setSelectedNodeId(id);
    setSelectedEdgeId(null);
    setPaletteMenu(null);
    setVarDropMenu(null);
    setStatus(extras?.var ?? extras?.instanceId ? `Added ${op} for ${extras.var ?? extras.instanceId}.` : `Added ${op}; wire its pins before validation.`);
  }, [descriptors, graph, nodes, sugars]);

  const addEventEntry = React.useCallback((position?: { x: number; y: number }) => {
    if (!graph || graph.kind !== 'TriggerGraph') {
      setStatus('Event cards only exist on TriggerGraph.');
      return;
    }
    const label = uniqueEventLabel(entryLabelsFromNodes(nodes));
    const id = eventEntryNodeId(label);
    const next: Node<GasNodeData> = {
      id,
      type: 'gas',
      deletable: true,
      selectable: true,
      position: position ?? { x: 40, y: 80 + nodes.filter((node) => node.data.role === 'event-entry').length * 140 },
      data: {
        id,
        op: 'Event',
        role: 'event-entry',
        entry: createEmptyEventEntry(label),
        label,
        controlOutputPorts: ['exec'],
      },
    };
    setNodes((previous) => [...previous, next]);
    setSelectedNodeId(id);
    setSelectedEdgeId(null);
    setPaletteMenu(null);
    setStatus('Added Event. Fill Event name, then wire Then to the first node.');
  }, [graph, nodes]);

  const updateSelectedEntry = (nextEntry: EventEntryConfig) => {
    if (!selectedNodeId || !graph) return;
    const selected = nodes.find((node) => node.id === selectedNodeId);
    if (!selected?.data.entry) return;
    const nextLabel = nextEntry.label.trim();
    if (nextLabel.length === 0) {
      setStatus('Event label cannot be empty.');
      return;
    }
    const nextId = eventEntryNodeId(nextLabel);
    if (nextId !== selectedNodeId && nodes.some((node) => node.id === nextId)) {
      setStatus(`Event label '${nextLabel}' is already used.`);
      return;
    }

    const previousStart = selected.data.entry.start;
    const nextStart = nextEntry.start.trim();
    setNodes((previous) => previous.map((node) => {
      if (node.id !== selectedNodeId) return node;
      return {
        ...node,
        id: nextId,
        data: {
          ...node.data,
          id: nextId,
          label: nextLabel,
          entry: { ...nextEntry, label: nextLabel, start: nextStart },
          schema: schemaFor(nextEntry.event),
        },
      };
    }));
    if (nextId !== selectedNodeId) {
      setEdges((previous) => previous.map((edge) => {
        const source = edge.source === selectedNodeId ? nextId : edge.source;
        const target = edge.target === selectedNodeId ? nextId : edge.target;
        if (source === edge.source && target === edge.target) return edge;
        return {
          ...edge,
          id: edge.data?.kind === 'control' && isEventEntryNodeId(source)
            ? eventThenEdgeId(source, target)
            : edge.id.replace(selectedNodeId, nextId),
          source,
          target,
        };
      }));
      setLayout((previous) => {
        const current = previous.nodes?.[selectedNodeId];
        if (!current) return previous;
        const nodesLayout = { ...previous.nodes };
        delete nodesLayout[selectedNodeId];
        nodesLayout[nextId] = current;
        return { ...previous, nodes: nodesLayout };
      });
      setSelectedNodeId(nextId);
    }
    if (nextStart !== previousStart) {
      setEdges((previous) => {
        const without = previous.filter((edge) => !(edge.source === nextId && edge.sourceHandle === 'exec'));
        if (!nextStart || !nodes.some((node) => node.id === nextStart)) return without;
        return addEdge({
          id: eventThenEdgeId(nextId, nextStart),
          source: nextId,
          sourceHandle: 'exec',
          target: nextStart,
          targetHandle: 'control-in',
          markerEnd: { type: MarkerType.ArrowClosed },
          style: { stroke: '#fb7185', strokeWidth: 2 },
          data: { kind: 'control', synthetic: true },
        }, without);
      });
    }
  };

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

  React.useEffect(() => {
    if (!graph) return;
    void refreshDebugMounts();
  }, [graph, refreshDebugMounts]);

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
    if (!debugEnabled || debugEvents.length === 0) {
      return {
        heat: new Map(),
        pins: new Map(),
        hotEdges: new Set<string>(),
      };
    }
    const heat = computeLiveNodeHeat(debugEvents);
    const pins = computeLivePinValues(debugEvents);
    const hotEdges = computeLiveEdgeIds(debugEvents, edges);
    return { heat, pins, hotEdges };
  }, [debugEnabled, debugEvents, edges]);

  const watchFocus = React.useMemo(() => {
    if (!debugEnabled || !debugEntryLabel) {
      return { nodeIds: new Set<string>(), edgeIds: new Set<string>() };
    }
    return computeWatchedEntryFocus(nodes, edges, debugEntryLabel, eventEntryNodeId);
  }, [debugEnabled, debugEntryLabel, edges, nodes]);

  React.useEffect(() => {
    if (!debugEnabled || watchFocus.nodeIds.size === 0) return;
    const focusIds = [...watchFocus.nodeIds];
    const timer = window.setTimeout(() => {
      reactFlowRef.current?.fitView({
        nodes: focusIds.map((id) => ({ id })),
        padding: 0.28,
        duration: 280,
        minZoom: 0.35,
        maxZoom: 1.35,
      });
    }, 80);
    return () => window.clearTimeout(timer);
  }, [debugEnabled, debugEntryLabel, watchFocus.nodeIds.size]);

  const displayNodes = React.useMemo(() => {
    let next = nodes as Node<GasNodeData & Record<string, unknown>>[];
    if (debugEnabled) {
      next = applyLiveDebugToNodes(next, activeDebugNodes.heat, activeDebugNodes.pins);
      next = applyWatchFocusToNodes(next, watchFocus.nodeIds);
    }
    if (selectedVariable == null) return next as Node<GasNodeData>[];
    return next.map((node) => {
      const usesVar = node.data.var === selectedVariable;
      if (!usesVar) return node as Node<GasNodeData>;
      return {
        ...node,
        style: {
          ...node.style,
          outline: '2px solid #fbbf24',
          outlineOffset: '2px',
        },
      } as Node<GasNodeData>;
    });
  }, [activeDebugNodes, debugEnabled, nodes, selectedVariable, watchFocus.nodeIds]);

  const displayEdges = React.useMemo(() => {
    let base = toDisplayEdges(nodes, edges);
    if (!debugEnabled) return base;
    base = applyLiveDebugToEdges(base, activeDebugNodes.hotEdges) as Edge<GasEdgeData>[];
    return applyWatchFocusToEdges(base, watchFocus.edgeIds, activeDebugNodes.hotEdges) as Edge<GasEdgeData>[];
  }, [activeDebugNodes.hotEdges, debugEnabled, edges, nodes, watchFocus.edgeIds]);

  const mapVariables = React.useMemo<GraphVariableRow[]>(() => {
    const rows = new Map<string, GraphVariableRow>();
    for (const declared of declaredVariables) {
      rows.set(declared.name, { ...declared, reads: 0, writes: 0 });
    }
    for (const node of nodes) {
      const name = node.data.var;
      if (!name) continue;
      const current = rows.get(name) ?? {
        name,
        type: node.data.op.includes('Float') ? 'float' : 'int',
        initial: 0,
        declared: false,
        reads: 0,
        writes: 0,
      };
      if (node.data.op.startsWith('Read')) current.reads += 1;
      if (node.data.op.startsWith('Write')) current.writes += 1;
      rows.set(name, current);
    }
    return [...rows.values()].sort((a, b) => a.name.localeCompare(b.name));
  }, [declaredVariables, nodes]);

  const focusVariable = React.useCallback((name: string) => {
    setSelectedVariable(name);
    const declared = declaredVariables.find((variable) => variable.name === name);
    if (declared) {
      setVariableDraft({
        name: declared.name,
        kind: declared.type,
        initial: String(declared.initial),
      });
    }
    const target = nodes.find((node) => node.data.var === name);
    if (!target) return;
    setSelectedNodeId(target.id);
    setSelectedEdgeId(null);
    reactFlowRef.current?.setCenter(target.position.x + 110, target.position.y + 40, { zoom: 0.9, duration: 220 });
  }, [declaredVariables, nodes]);

  const parseDraftInitial = (draft: MapVariableDraft): number => {
    if (draft.kind === 'int') {
      const parsed = Number.parseInt(draft.initial, 10);
      if (!Number.isInteger(parsed)) throw new Error('Integer variables need a whole-number default.');
      return parsed;
    }
    const parsed = Number.parseFloat(draft.initial);
    if (!Number.isFinite(parsed)) throw new Error('Float variables need a numeric default.');
    return parsed;
  };

  const saveMapVariables = async (nextRows: GraphVariableRow[]) => {
    if (!variableMapId) throw new Error('This graph is not mounted on a map.');
    const res = await fetch(`/api/mods/${encodeURIComponent(modId)}/maps/${encodeURIComponent(variableMapId)}/variables`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        variables: nextRows.map((variable) => ({
          name: variable.name,
          type: variable.type,
          initial: variable.initial,
        })),
      }),
    });
    const payload = await res.json();
    if (!res.ok || !payload.ok) throw new Error(payload.error ?? `Variable save failed (${res.status})`);
    await loadMapVariables(graphId);
  };

  const createMapVariable = async () => {
    try {
      setVariableBusy(true);
      const name = variableDraft.name.trim();
      if (!name) throw new Error('Variable name is required.');
      if (declaredVariables.some((variable) => variable.name === name)) {
        throw new Error(`Variable '${name}' already exists.`);
      }
      const next: GraphVariableRow = {
        name,
        type: variableDraft.kind === 'float' ? 'float' : 'int',
        initial: parseDraftInitial(variableDraft),
        declared: true,
        reads: 0,
        writes: 0,
      };
      await saveMapVariables([...declaredVariables, next]);
      setSelectedVariable(name);
      setVariableStatus(`Added ${name}. Drag it onto the canvas to place Get or Set.`);
    } catch (err) {
      setVariableStatus(err instanceof Error ? err.message : String(err));
    } finally {
      setVariableBusy(false);
    }
  };

  const updateMapVariable = async () => {
    if (!selectedVariable) {
      setVariableStatus('Select a variable to update.');
      return;
    }
    try {
      setVariableBusy(true);
      const name = variableDraft.name.trim();
      if (!name) throw new Error('Variable name is required.');
      if (name !== selectedVariable && declaredVariables.some((variable) => variable.name === name)) {
        throw new Error(`Variable '${name}' already exists.`);
      }
      if (name !== selectedVariable && nodes.some((node) => node.data.var === selectedVariable)) {
        throw new Error(`Rename '${selectedVariable}' after removing or retargeting its Get/Set nodes on this graph.`);
      }
      const nextRows = declaredVariables.map((variable) => (
        variable.name === selectedVariable
          ? {
              ...variable,
              name,
              type: (variableDraft.kind === 'float' ? 'float' : 'int') as MapVariableScalarType,
              initial: parseDraftInitial(variableDraft),
            }
          : variable
      ));
      await saveMapVariables(nextRows);
      setSelectedVariable(name);
      setVariableStatus(`Updated ${name}.`);
    } catch (err) {
      setVariableStatus(err instanceof Error ? err.message : String(err));
    } finally {
      setVariableBusy(false);
    }
  };

  const deleteMapVariable = async () => {
    if (!selectedVariable) {
      setVariableStatus('Select a variable to delete.');
      return;
    }
    if (nodes.some((node) => node.data.var === selectedVariable)) {
      setVariableStatus(`Cannot delete '${selectedVariable}' while this graph still has Get/Set nodes for it.`);
      return;
    }
    try {
      setVariableBusy(true);
      await saveMapVariables(declaredVariables.filter((variable) => variable.name !== selectedVariable));
      setSelectedVariable(null);
      setVariableDraft(emptyVariableDraft());
      setVariableStatus(`Deleted ${selectedVariable}.`);
    } catch (err) {
      setVariableStatus(err instanceof Error ? err.message : String(err));
    } finally {
      setVariableBusy(false);
    }
  };

  const placeVariableAccess = (access: 'get' | 'set', position: { x: number; y: number }, name: string, type: MapVariableScalarType) => {
    const op = access === 'get'
      ? (type === 'float' ? 'ReadMapVarFloat' : 'ReadMapVarInt')
      : (type === 'float' ? 'WriteMapVarFloat' : 'WriteMapVarInt');
    addAuthoringNode(op, position, { var: name });
  };

  const onVariableDragOver = (event: React.DragEvent) => {
    const raw = event.dataTransfer.getData('text/plain');
    const payload = decodeMapVarDrag(raw) ?? decodePlacedVarDrag(raw);
    if (!payload && !event.dataTransfer.types.includes('text/plain')) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = 'copy';
  };

  const onVariableDrop = (event: React.DragEvent) => {
    const raw = event.dataTransfer.getData('text/plain');
    const placed = decodePlacedVarDrag(raw);
    const mapVar = decodeMapVarDrag(raw);
    if (!placed && !mapVar) return;
    event.preventDefault();
    const flow = reactFlowRef.current?.screenToFlowPosition({ x: event.clientX, y: event.clientY }) ?? {
      x: 80,
      y: 120,
    };
    setPaletteMenu(null);
    setVarDropMenu({
      clientX: event.clientX,
      clientY: event.clientY,
      flowX: flow.x,
      flowY: flow.y,
      name: placed ? placed.instanceId : mapVar!.name,
      type: placed ? 'int' : mapVar!.type,
      placed: placed != null,
      placedKind: placed?.kind,
    });
  };

  const placePlacedAccess = (position: { x: number; y: number }, instanceId: string, kind: GraphPlacedKind) => {
    const op =
      kind === 'region' ? 'LoadPlacedRegion' :
      kind === 'anchor' ? 'LoadPlacedAnchor' :
      'LoadPlacedEntity';
    addAuthoringNode(op, position, { instanceId });
  };

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
          disabled={busy || nodes.length === 0}
          onClick={applyAutoLayout}
          className="rounded bg-indigo-700 px-3 py-1 text-xs font-semibold hover:bg-indigo-600 disabled:opacity-50"
        >
          Auto Layout
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
          onClick={() => void loadCatalog()}
          className="rounded border border-slate-600 px-3 py-1 text-xs font-semibold text-slate-200 hover:bg-slate-800"
        >
          Refresh Tree
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

      <div className="grid min-h-0 flex-1 grid-cols-[260px_1fr_320px]">
        <div className="flex min-h-0 flex-col border-r border-slate-800">
          <div className="min-h-0 flex-[3] overflow-hidden [&_aside]:h-full [&_aside]:border-r-0">
            <GraphCatalogTree
              mods={catalog}
              selectedModId={modId}
              selectedGraphId={graphId}
              status={catalogStatus}
              onSelect={(nextModId, nextGraphId) => {
                setModId(nextModId);
                setGraphId(nextGraphId);
                setSelectedVariable(null);
              }}
            />
          </div>
          <GraphVariablePanel
            variables={mapVariables}
            placedInstances={mapInstances}
            selectedName={selectedVariable}
            mapId={variableMapId}
            status={variableStatus}
            draft={variableDraft}
            busy={variableBusy || busy}
            onSelect={focusVariable}
            onDraftChange={setVariableDraft}
            onCreate={() => void createMapVariable()}
            onUpdate={() => void updateMapVariable()}
            onDelete={() => void deleteMapVariable()}
          />
        </div>
        <div className="min-h-0">
          {graph ? (
            <div className="relative h-full" onDragOver={onVariableDragOver} onDrop={onVariableDrop}>
              <ReactFlow
                className="gas-graph-flow"
                nodes={displayNodes}
                edges={displayEdges}
                nodeTypes={nodeTypes}
                onInit={(instance) => { reactFlowRef.current = instance; }}
                onPaneClick={() => {
                  setPaletteMenu(null);
                  setVarDropMenu(null);
                }}
                onPaneContextMenu={(event) => {
                  event.preventDefault();
                  openPaletteAt(event.clientX, event.clientY);
                }}
                onNodeContextMenu={(event) => {
                  event.preventDefault();
                  openPaletteAt(event.clientX, event.clientY);
                }}
                onNodesChange={onNodesChange}
                onEdgesChange={onEdgesChange}
                onConnect={onConnect}
                onSelectionChange={({ nodes: selected, edges: selectedEdges }) => {
                  setSelectedNodeId(selected[0]?.id ?? null);
                  setSelectedEdgeId(selectedEdges[0]?.id ?? null);
                }}
                panOnDrag={[1]}
                selectionOnDrag
                selectionMode={SelectionMode.Partial}
                selectNodesOnDrag
                minZoom={0.12}
                maxZoom={1.75}
                fitView
                proOptions={{ hideAttribution: true }}
              >
                <Background gap={16} color="#334155" />
                <Controls />
                <MiniMap
                  pannable
                  zoomable
                  bgColor="#020617"
                  maskColor="rgba(2, 6, 23, 0.35)"
                  nodeStrokeColor="#94a3b8"
                  nodeColor={(node) => {
                    if (node.data.role === 'event-entry') return '#fb7185';
                    if (node.data.op === 'SwitchInt' || node.data.op === 'FsmState') return '#f59e0b';
                    if (sugars[node.data.op as string]?.childArms || node.data.op === 'BtDecorator') return '#a78bfa';
                    return '#38bdf8';
                  }}
                />
              </ReactFlow>
              <div className="pointer-events-none absolute left-3 top-3 z-10 rounded border border-slate-800 bg-slate-950/80 px-2 py-1 text-[10px] text-slate-400">
                Middle-drag to pan · Left-drag to box-select · Right-click to add a node
              </div>
              {paletteMenu ? (
                <div
                  className="fixed z-50 w-72 rounded border border-slate-700 bg-slate-950/95 p-2 shadow-xl"
                  style={{
                    left: Math.min(paletteMenu.clientX, window.innerWidth - 300),
                    top: Math.min(paletteMenu.clientY, window.innerHeight - 360),
                  }}
                  onMouseDown={(event) => event.stopPropagation()}
                >
                  <div className="flex items-center gap-2 border-b border-slate-800 pb-2">
                    <Search size={14} className="text-slate-500" aria-hidden="true" />
                    <input
                      autoFocus
                      value={nodeSearch}
                      onChange={(event) => setNodeSearch(event.target.value)}
                      onKeyDown={(event) => {
                        if (event.key === 'Escape') setPaletteMenu(null);
                        if (event.key === 'Enter' && availableNodes[0]) {
                          addAuthoringNode(availableNodes[0].op, { x: paletteMenu.flowX, y: paletteMenu.flowY });
                        }
                      }}
                      placeholder="Find node"
                      aria-label="Find graph node"
                      className="min-w-0 flex-1 bg-transparent text-xs text-slate-100 outline-none placeholder:text-slate-600"
                    />
                    <span className="text-[10px] text-slate-600">Enter</span>
                  </div>
                  <div className="mt-2 max-h-64 overflow-auto">
                    {graph.kind === 'TriggerGraph' && (!nodeSearch.trim() || 'event'.includes(nodeSearch.trim().toLocaleLowerCase())) ? (
                      <button
                        type="button"
                        onClick={() => addEventEntry({ x: paletteMenu.flowX, y: paletteMenu.flowY })}
                        className="flex w-full items-center gap-2 px-2 py-1.5 text-left text-xs text-rose-100 hover:bg-rose-950"
                      >
                        <Plus size={12} className="text-rose-400" aria-hidden="true" />
                        <span className="font-mono">Event</span>
                        <span className="ml-auto text-[10px] text-rose-300">entry</span>
                      </button>
                    ) : null}
                    {availableNodes.slice(0, 24).map((entry) => (
                      <button
                        key={entry.op}
                        type="button"
                        onClick={() => addAuthoringNode(entry.op, { x: paletteMenu.flowX, y: paletteMenu.flowY })}
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
              ) : null}
              {varDropMenu ? (
                <div
                  className="fixed z-50 w-56 rounded border border-amber-800 bg-slate-950/95 p-2 shadow-xl"
                  style={{
                    left: Math.min(varDropMenu.clientX, window.innerWidth - 240),
                    top: Math.min(varDropMenu.clientY, window.innerHeight - 160),
                  }}
                  onMouseDown={(event) => event.stopPropagation()}
                >
                  <div className="mb-2 px-1 text-[11px] text-amber-100">
                    Place <span className="font-mono">{varDropMenu.name}</span>
                  </div>
                  {varDropMenu.placed ? (
                    <button
                      type="button"
                      onClick={() => placePlacedAccess(
                        { x: varDropMenu.flowX, y: varDropMenu.flowY },
                        varDropMenu.name,
                        varDropMenu.placedKind ?? 'entity')}
                      className="flex w-full items-center justify-between rounded px-2 py-1.5 text-left text-xs text-slate-200 hover:bg-slate-800"
                    >
                      <span>Get</span>
                      <span className="font-mono text-[10px] text-slate-500">
                        {(varDropMenu.placedKind ?? 'entity') === 'region'
                          ? 'LoadPlacedRegion'
                          : (varDropMenu.placedKind ?? 'entity') === 'anchor'
                            ? 'LoadPlacedAnchor'
                            : 'LoadPlacedEntity'}
                      </span>
                    </button>
                  ) : (
                    <>
                      <button
                        type="button"
                        onClick={() => placeVariableAccess('get', { x: varDropMenu.flowX, y: varDropMenu.flowY }, varDropMenu.name, varDropMenu.type)}
                        className="mb-1 flex w-full items-center justify-between rounded px-2 py-1.5 text-left text-xs text-slate-200 hover:bg-slate-800"
                      >
                        <span>Get</span>
                        <span className="font-mono text-[10px] text-slate-500">
                          {varDropMenu.type === 'float' ? 'ReadMapVarFloat' : 'ReadMapVarInt'}
                        </span>
                      </button>
                      <button
                        type="button"
                        onClick={() => placeVariableAccess('set', { x: varDropMenu.flowX, y: varDropMenu.flowY }, varDropMenu.name, varDropMenu.type)}
                        className="flex w-full items-center justify-between rounded px-2 py-1.5 text-left text-xs text-slate-200 hover:bg-slate-800"
                      >
                        <span>Set</span>
                        <span className="font-mono text-[10px] text-slate-500">
                          {varDropMenu.type === 'float' ? 'WriteMapVarFloat' : 'WriteMapVarInt'}
                        </span>
                      </button>
                    </>
                  )}
                </div>
              ) : null}
            </div>
          ) : (
            <div className="flex h-full items-center justify-center text-sm text-slate-500">
              Select a graph in the left tree. Bridge must be running on :5299.
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
                {selectedData.role === 'event-entry' && selectedData.entry ? (
                  <EventEntryInspector
                    entry={selectedData.entry}
                    eventSchemas={eventSchemas}
                    startOptions={nodes.filter((node) => node.data.role !== 'event-entry').map((node) => node.id)}
                    instanceOptions={mapInstances.map((instance) => instance.instanceId)}
                    variableOptions={declaredVariables.map((variable) => variable.name)}
                    onChange={updateSelectedEntry}
                    onAdd={() => addEventEntry()}
                  />
                ) : (
                  <>
                <div>
                  <div className="text-slate-500">Id</div>
                  <div className="font-mono text-slate-100">{selectedData.id}</div>
                </div>
                <div>
                  <div className="text-slate-500">Op</div>
                  <div className="font-mono text-sky-300">{selectedData.op}</div>
                </div>
                {authoredFieldsForOp(selectedData.op).map((field) => {
                  const raw = selectedData[field.key];
                  if (field.kind === 'bool') {
                    return (
                      <label key={field.key} className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={Boolean(raw)}
                          onChange={(event) => updateSelectedField(field.key, event.target.checked)}
                        />
                        <span className="text-slate-400">{field.label}</span>
                      </label>
                    );
                  }
                      if (field.kind === 'anchor' && panelAnchors.length > 0) {
                        const current = raw == null ? '' : String(raw);
                        const options = current && !panelAnchors.includes(current)
                          ? [...panelAnchors, current]
                          : panelAnchors;
                        return (
                          <label key={field.key} className="block">
                            <div className="mb-1 text-slate-500">{field.label}</div>
                            <select
                              value={current}
                              onChange={(event) => updateSelectedField(field.key, event.target.value)}
                              className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
                            >
                              <option value="">Select anchor</option>
                              {options.map((anchor) => (
                                <option key={anchor} value={anchor}>{anchor}</option>
                              ))}
                            </select>
                          </label>
                        );
                      }
                      if (field.kind === 'payloadKey' && payloadKeys.length > 0) {
                        const current = raw == null ? '' : String(raw);
                        const options = current && !payloadKeys.includes(current)
                          ? [...payloadKeys, current]
                          : payloadKeys;
                        return (
                          <label key={field.key} className="block">
                            <div className="mb-1 text-slate-500">{field.label}</div>
                            <select
                              value={current}
                              onChange={(event) => updateSelectedField(field.key, event.target.value)}
                              className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
                            >
                              <option value="">Select payload key</option>
                              {options.map((key) => (
                                <option key={key} value={key}>{key}</option>
                              ))}
                            </select>
                          </label>
                        );
                      }
                      if (field.kind === 'enumType' && enumCatalog.length > 0) {
                        const current = raw == null ? '' : String(raw);
                        const options = current && !enumCatalog.some((candidate) => candidate.name === current)
                          ? [current, ...enumCatalog.map((candidate) => candidate.name)]
                          : enumCatalog.map((candidate) => candidate.name);
                        return (
                          <label key={field.key} className="block">
                            <div className="mb-1 text-slate-500">{field.label}</div>
                            <select
                              value={current}
                              onChange={(event) => updateSelectedField(field.key, event.target.value)}
                              className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
                            >
                              <option value="">Unbound (raw case ints)</option>
                              {options.map((name) => (
                                <option key={name} value={name}>{name}</option>
                              ))}
                            </select>
                          </label>
                        );
                      }
                      if (field.kind === 'textKey' && textKeyCatalog.length > 0) {
                        const current = raw == null ? '' : String(raw);
                        const options = current && !textKeyCatalog.some((candidate) => candidate.id === current)
                          ? [current, ...textKeyCatalog.map((candidate) => candidate.id)]
                          : textKeyCatalog.map((candidate) => candidate.id);
                        return (
                          <label key={field.key} className="block">
                            <div className="mb-1 text-slate-500">{field.label}</div>
                            <select
                              value={current}
                              onChange={(event) => updateSelectedField(field.key, event.target.value)}
                              className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
                            >
                              <option value="">Select text key</option>
                              {options.map((id) => {
                                const meta = textKeyCatalog.find((candidate) => candidate.id === id);
                                const suffix = meta?.preview
                                  ? ` — ${meta.preview}`
                                  : meta
                                    ? ` (args=${meta.argCount})`
                                    : '';
                                return (
                                  <option key={id} value={id}>{id}{suffix}</option>
                                );
                              })}
                            </select>
                          </label>
                        );
                      }
                      if (field.kind === 'instanceId' && mapInstances.length > 0) {
                        const current = raw == null ? '' : String(raw);
                        const selectedOp = selectedData.op ?? '';
                        const filtered = mapInstances.filter((instance) => {
                          if (selectedOp === 'LoadPlacedRegion') return instance.kind === 'region';
                          if (selectedOp === 'LoadPlacedAnchor') return instance.kind === 'anchor';
                          if (selectedOp === 'LoadPlacedEntity') return instance.kind === 'entity' || instance.kind === 'anchor';
                          return true;
                        });
                        const instanceIds = filtered.map((instance) => instance.instanceId);
                        const options = current && !instanceIds.includes(current)
                          ? [...instanceIds, current]
                          : instanceIds;
                        return (
                          <label key={field.key} className="block">
                            <div className="mb-1 text-slate-500">{field.label}</div>
                            <select
                              value={current}
                              onChange={(event) => updateSelectedField(field.key, event.target.value)}
                              className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
                            >
                              <option value="">Select placed instance</option>
                              {options.map((id) => (
                                <option key={id} value={id}>{id}</option>
                              ))}
                            </select>
                          </label>
                        );
                      }
                      if (field.key === 'decoratorKind') {
                        const current = raw == null ? '' : String(raw);
                        const known = ['inverter', 'forceSuccess', 'forceFailure'];
                        const options = current && !known.includes(current) ? [...known, current] : known;
                        return (
                          <label key={field.key} className="block">
                            <div className="mb-1 text-slate-500">{field.label}</div>
                            <select
                              value={current}
                              onChange={(event) => updateSelectedField(field.key, event.target.value)}
                              className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
                            >
                              <option value="">Select kind</option>
                              {options.map((kind) => (
                                <option key={kind} value={kind}>{kind}</option>
                              ))}
                            </select>
                          </label>
                        );
                      }
                  return (
                    <label key={field.key} className="block">
                      <div className="mb-1 text-slate-500">{field.label}</div>
                      <input
                        type={field.kind === 'string' ? 'text' : 'number'}
                        step={field.kind === 'int' ? '1' : undefined}
                        value={raw == null ? '' : String(raw)}
                        onChange={(event) => updateSelectedField(field.key, event.target.value)}
                        className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
                      />
                    </label>
                  );
                })}
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
                {(selectedData.op === 'SwitchInt' || selectedData.op === 'FsmState') && graph && isControlFlowGraph(graph) ? (
                  <div className="space-y-2 rounded border border-sky-900 bg-sky-950/30 p-2">
                    <div className="text-sky-300">{selectedData.op === 'FsmState' ? 'FsmState case arms' : 'Switch cases'}</div>
                    {(() => {
                      const boundEnum = selectedData.enumType
                        ? enumCatalog.find((candidate) => candidate.name === selectedData.enumType)
                        : null;
                      if (selectedData.op === 'FsmState' && !boundEnum) {
                        return (
                          <div className="text-[11px] text-amber-300">
                            Bind enumType before adding case arms (FsmState fails closed without it).
                          </div>
                        );
                      }
                      if (boundEnum) {
                        return (
                          <label className="block">
                            <div className="mb-1 text-slate-500">Case member ({boundEnum.name})</div>
                            <select
                              value={boundEnum.members.some((member) => member.name === switchCaseValue) ? switchCaseValue : ''}
                              onChange={(event) => setSwitchCaseValue(event.target.value)}
                              className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
                            >
                              <option value="">Select member</option>
                              {boundEnum.members.map((member) => (
                                <option key={member.name} value={member.name}>{member.name} ({member.value})</option>
                              ))}
                            </select>
                          </label>
                        );
                      }
                      return (
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
                      );
                    })()}
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
                {selectedData.op && sugars[selectedData.op]?.childArms && graph && isControlFlowGraph(graph) ? (
                  <div className="space-y-2 rounded border border-violet-900 bg-violet-950/30 p-2">
                    <div className="text-violet-300">{selectedData.op} child arms</div>
                    <div className="font-mono text-[10px] text-violet-200/80">
                      {(selectedData.controlOutputPorts ?? []).filter((port) => port.startsWith('child:')).join(', ') || 'none yet'}
                    </div>
                    <label className="block">
                      <div className="mb-1 text-slate-500">Target node</div>
                      <select
                        value={btChildTarget}
                        onChange={(event) => setBtChildTarget(event.target.value)}
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
                      onClick={addBtChildArm}
                      className="w-full rounded bg-violet-700 px-2 py-1 font-semibold text-violet-50 hover:bg-violet-600"
                    >
                      Add child edge
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
                  </>
                )}
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
              <div className="space-y-2">
                <div className="text-slate-500">Select a node or an Event card.</div>
                {graph?.kind === 'TriggerGraph' ? (
                  <button
                    type="button"
                    onClick={() => addEventEntry()}
                    className="w-full rounded bg-rose-800 px-2 py-1 font-semibold text-rose-50 hover:bg-rose-700"
                  >
                    Add Event
                  </button>
                ) : null}
              </div>
            )}
          </div>

          <div className="border-t border-slate-800 px-3 py-2 text-xs font-semibold uppercase tracking-wide text-slate-400">
            Diagnostics
          </div>
          <pre className="min-h-0 flex-1 overflow-auto whitespace-pre-wrap p-3 font-mono text-[11px] text-amber-200">
            {diagnosticsText || 'Validate or Save to run the Bridge compiler.'}
          </pre>

          {graph ? (
            <GraphCodegenPanel
              modId={modId}
              graphId={graphId}
              graphBody={flowToGraph(graph, nodes, edges)}
              executionBackendLabel={
                debugMounts.find((m) => m.entryLabel === debugEntryLabel)?.executionBackend
                ?? debugMounts[0]?.executionBackend
                ?? 'Interpret'
              }
            />
          ) : null}

          <div className="border-t border-slate-800 px-3 py-2 text-xs font-semibold uppercase tracking-wide text-amber-300">
            Live Debug · {debugMounts.find((m) => m.entryLabel === debugEntryLabel)?.executionBackend
              ?? debugMounts[0]?.executionBackend
              ?? 'Interpret'}
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
            {debugEnabled ? (
              <div className="rounded border border-cyan-900/60 bg-cyan-950/40 px-2 py-1.5 text-[10px] leading-4 text-cyan-100/90">
                Framing entry <span className="font-mono text-cyan-50">{debugEntryLabel || '—'}</span>
                {' '}({watchFocus.nodeIds.size} nodes). Other chains are dimmed.
                Play the game action that fires this entry — the framed path lights up.
              </div>
            ) : null}
            <div className="max-h-28 overflow-auto rounded border border-slate-800 bg-slate-950 p-2 font-mono text-[10px]">
              {debugEvents.length === 0 ? 'No trace changes yet.' : debugEvents.slice(-24).map((event) => (
                <div key={event.sequence} className={event.nodeId ? 'text-cyan-200' : 'text-slate-400'}>
                  #{event.sequence} {event.event} {event.nodeId ?? `pc:${event.steps}`}
                  {event.controlPort ? ` →${event.controlPort}` : ''}
                  {event.pinIndex !== undefined ? ` pin[${event.pinIndex}]=${String(event.value)}` : ''}
                </div>
              ))}
            </div>
          </div>
        </aside>
      </div>
    </div>
  );
};
