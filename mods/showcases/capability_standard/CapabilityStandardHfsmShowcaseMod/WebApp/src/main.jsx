import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import {
  Background,
  BackgroundVariant,
  Controls,
  Handle,
  MarkerType,
  MiniMap,
  Panel,
  Position,
  ReactFlow,
  ReactFlowProvider,
  useEdgesState,
  useNodesState
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import './styles.css';
import {
  HFSM_GRAPH_DEBUG_TOPIC,
  createLudotsDataPlaneClient,
  waitForLudotsDataPlaneTransport
} from './dataplane/client.js';
import showcaseConfig from '../../assets/HfsmShowcase/showcase.json';

const ROOT_GRAPH_ID = 'hfsm.root';
const ACTIVE_COLOR = '#ffd166';
const SELECTED_COLOR = '#5fb3ff';
const DEAD_COLOR = '#ff5a64';
const EDGE_COLORS = {
  hierarchy: '#7ec8a8',
  transition: '#5fb3ff',
  interrupt: '#ff5a64',
  flow: '#ffd166',
  data: '#91a0b5'
};

const emptyPacket = {
  revision: 0,
  rootGraph: null,
  implementations: [],
  activeGraphId: ROOT_GRAPH_ID,
  selectedNodeId: '',
  activeStateId: '',
  activeStatePathIds: [],
  activeImplementationGraphId: '',
  activeOpNodeIds: [],
  runtime: {
    isActive: false,
    stateId: '',
    stateLabel: '',
    statePath: '',
    playerStory: '',
    lastEvent: '',
    health: 0,
    water: 0,
    lapCount: 0,
    transitionCount: 0,
    heroXCm: 0,
    heroYCm: 0,
    dead: false
  },
  selectedEntity: {
    instanceId: 'hfsm-hero',
    name: 'HFSM Runner'
  },
  command: {
    lastCommand: 'none',
    lastStatus: 'idle'
  }
};

function resolvePreviewMode() {
  const params = new URLSearchParams(window.location.search);
  return params.get('dataplane') === 'mock' || params.get('mode') === 'mock';
}

function useHfsmGraphDebugDataPlane() {
  const clientRef = useRef(null);
  const [packet, setPacket] = useState(emptyPacket);
  const [status, setStatus] = useState({
    phase: 'boot',
    transport: 'none',
    error: ''
  });

  useEffect(() => {
    let active = true;
    let resolvedTransport = null;
    let client = null;
    const abortController = new AbortController();
    const previewMode = resolvePreviewMode();

    setStatus({
      phase: previewMode ? 'connecting' : 'waiting-for-host',
      transport: 'none',
      error: ''
    });

    waitForLudotsDataPlaneTransport({
      allowMock: previewMode,
      previewGraphDebug: showcaseConfig.graphDebug,
      signal: abortController.signal
    })
      .then((resolved) => {
        if (!active) {
          return null;
        }

        resolvedTransport = resolved;
        const { transport, hostBacked } = resolved;
        client = createLudotsDataPlaneClient({
          transport,
          hostBacked,
          diagnostics: (diagnostic) => {
            if (active && diagnostic.level === 'error') {
              setStatus((current) => ({ ...current, phase: 'error', error: diagnostic.message }));
            }
          }
        });
        clientRef.current = client;

        setStatus({
          phase: 'connecting',
          transport: transport?.name ?? 'unknown',
          error: ''
        });

        return client.handshake({ app: 'capability-standard-hfsm-graph-debug' });
      })
      .then((handshake) => {
        if (!active || !handshake || !client) {
          return null;
        }

        const transport = resolvedTransport?.transport;
        setStatus({
          phase: 'connected',
          transport: handshake.payload?.transportMode ?? handshake.payload?.transportName ?? transport?.name ?? 'unknown',
          error: ''
        });
        return client.subscribe(HFSM_GRAPH_DEBUG_TOPIC, (event) => {
          if (!active) {
            return;
          }

          const payload = event.payload ?? {};
          React.startTransition(() => {
            setPacket((current) => ({
              ...current,
              ...payload,
              rootGraph: payload.rootGraph ?? current.rootGraph,
              implementations: payload.implementations ?? current.implementations ?? []
            }));
            setStatus((current) => ({ ...current, phase: 'streaming', error: '' }));
          });
        });
      })
      .catch((error) => {
        if (active) {
          setStatus({
            phase: 'error',
            transport: resolvedTransport?.transport?.name ?? 'none',
            error: error instanceof Error ? error.message : String(error)
          });
        }
      });

    return () => {
      active = false;
      abortController.abort();
      client?.close();
      resolvedTransport?.transport?.dispose?.();
    };
  }, []);

  const command = useCallback(async (name, payload = {}) => {
    const client = clientRef.current;
    if (!client) {
      return;
    }

    setStatus((current) => ({ ...current, error: '' }));
    try {
      await client.command(name, payload);
    } catch (error) {
      setStatus((current) => ({
        ...current,
        phase: 'error',
        error: error instanceof Error ? error.message : String(error)
      }));
    }
  }, []);

  return { packet, status, command };
}

function GraphDebugApp() {
  const { packet, status, command } = useHfsmGraphDebugDataPlane();
  const [viewGraphId, setViewGraphId] = useState(ROOT_GRAPH_ID);
  const [localSelectedNodeId, setLocalSelectedNodeId] = useState('');

  useEffect(() => {
    if (packet.activeGraphId && viewGraphId === '') {
      setViewGraphId(packet.activeGraphId);
    }
  }, [packet.activeGraphId, viewGraphId]);

  useEffect(() => {
    if (packet.selectedNodeId) {
      setLocalSelectedNodeId(packet.selectedNodeId);
    }
  }, [packet.selectedNodeId]);

  const currentGraph = useMemo(
    () => resolveCurrentGraph(packet, viewGraphId),
    [packet, viewGraphId]
  );
  const selectedNode = useMemo(
    () => currentGraph?.nodes?.find((node) => node.id === localSelectedNodeId) ?? null,
    [currentGraph, localSelectedNodeId]
  );

  const openGraph = useCallback((graphId) => {
    setViewGraphId(graphId);
    command('openGraph', { graphId });
  }, [command]);

  const selectNode = useCallback((nodeId) => {
    setLocalSelectedNodeId(nodeId);
    command('selectNode', { nodeId });
  }, [command]);

  const runRootRuntimeCommand = useCallback((name) => {
    setViewGraphId(packet.rootGraph?.id ?? ROOT_GRAPH_ID);
    command(name);
  }, [command, packet.rootGraph?.id]);

  if (!currentGraph || currentGraph.nodes.length === 0) {
    return <BootPanel status={status} />;
  }

  return (
    <div className="app-shell">
      <HeaderBar
        status={status}
        runtime={packet.runtime}
        command={packet.command}
        onKill={() => runRootRuntimeCommand('killHero')}
        onThirst={() => runRootRuntimeCommand('makeThirsty')}
        onReset={() => runRootRuntimeCommand('resetStory')}
      />
      <Breadcrumbs
        graph={currentGraph}
        rootTitle={packet.rootGraph?.title ?? 'HFSM'}
        isRoot={currentGraph.id === packet.rootGraph?.id}
        onOpenRoot={() => openGraph(packet.rootGraph?.id ?? ROOT_GRAPH_ID)}
      />
      <GraphCanvas
        key={currentGraph.id}
        graph={currentGraph}
        packet={packet}
        selectedNodeId={localSelectedNodeId}
        onSelectNode={selectNode}
        onOpenGraph={openGraph}
      />
      <InspectorPanel graph={currentGraph} runtime={packet.runtime} selectedNode={selectedNode} />
    </div>
  );
}

function BootPanel({ status }) {
  return (
    <div className="boot-panel">
      <strong>HFSM graph editor/debug</strong>
      <span>{status.phase}</span>
      <p>{status.error || 'Waiting for Ludots runtime data.'}</p>
    </div>
  );
}

function HeaderBar({ status, runtime, command, onKill, onThirst, onReset }) {
  return (
    <header className="header-bar">
      <div className="title-block">
        <span>HFSM editor/debug</span>
        <strong>{runtime.statePath || 'Waiting for map'}</strong>
      </div>
      <div className="runtime-strip">
        <Metric label="water" value={runtime.water} tone="water" />
        <Metric label="health" value={runtime.health} tone={runtime.dead ? 'dead' : 'health'} />
        <Metric label="laps" value={runtime.lapCount} />
        <Metric label="transitions" value={runtime.transitionCount} />
      </div>
      <div className="command-bar">
        <button type="button" onClick={onThirst}>Thirst</button>
        <button type="button" onClick={onKill}>Kill</button>
        <button type="button" onClick={onReset}>Reset</button>
      </div>
      <div className="connection-chip" title={status.error || command.lastStatus}>
        {status.phase}
      </div>
    </header>
  );
}

function Metric({ label, value, tone = 'neutral' }) {
  return (
    <div className={`metric metric-${tone}`}>
      <strong>{value}</strong>
      <span>{label}</span>
    </div>
  );
}

function Breadcrumbs({ graph, rootTitle, isRoot, onOpenRoot }) {
  return (
    <nav className="breadcrumbs">
      <button type="button" disabled={isRoot} onClick={onOpenRoot}>{rootTitle}</button>
      {!isRoot && (
        <>
          <span>/</span>
          <strong>{graph.title}</strong>
        </>
      )}
    </nav>
  );
}

function GraphCanvas({ graph, packet, selectedNodeId, onSelectNode, onOpenGraph }) {
  const draggingRef = useRef(false);
  const pendingFlowRef = useRef(null);
  const nodeTypes = useMemo(() => ({ graphNode: GraphNode }), []);
  const graphDefinitionKey = useMemo(() => graphDefinitionSignature(graph), [graph]);
  const activeStatePathKey = (packet.activeStatePathIds ?? []).join(',');
  const activeOpNodeKey = (packet.activeOpNodeIds ?? []).join(',');
  const runtimeDead = packet.runtime?.dead === true;

  const openImplementationGraph = useCallback((nodeId, graphId) => {
    onSelectNode(nodeId);
    onOpenGraph(graphId);
  }, [onOpenGraph, onSelectNode]);

  const flow = useMemo(
    () => buildFlowGraph(graph, packet, selectedNodeId, openImplementationGraph),
    [graphDefinitionKey, packet.activeStateId, activeStatePathKey, activeOpNodeKey, runtimeDead, selectedNodeId, openImplementationGraph]
  );
  const [nodes, setNodes, onNodesChange] = useNodesState(flow.nodes);
  const [edges, setEdges, onEdgesChange] = useEdgesState(flow.edges);

  const applyFlow = useCallback((nextFlow) => {
    setNodes((current) => mergeFlowNodes(current, nextFlow.nodes));
    setEdges(nextFlow.edges);
  }, [setEdges, setNodes]);

  useEffect(() => {
    if (draggingRef.current) {
      pendingFlowRef.current = flow;
      setEdges(flow.edges);
      return;
    }

    applyFlow(flow);
  }, [applyFlow, flow, setEdges]);

  const handleNodeClick = useCallback((_event, node) => {
    onSelectNode(node.id);
  }, [onSelectNode]);

  const handleNodeDoubleClick = useCallback((event, node) => {
    event.preventDefault();
    const graphId = node.data?.implementationGraphId;
    if (graphId) {
      node.data?.onOpenGraph?.(node.id, graphId);
    }
  }, []);

  const handleNodeDragStart = useCallback(() => {
    draggingRef.current = true;
  }, []);

  const handleNodeDragStop = useCallback(() => {
    draggingRef.current = false;
    const pendingFlow = pendingFlowRef.current;
    pendingFlowRef.current = null;
    if (pendingFlow) {
      applyFlow(pendingFlow);
    }
  }, [applyFlow]);

  return (
    <main className="graph-stage">
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onNodeClick={handleNodeClick}
        onNodeDoubleClick={handleNodeDoubleClick}
        onNodeDragStart={handleNodeDragStart}
        onNodeDragStop={handleNodeDragStop}
        fitView
        minZoom={0.35}
        maxZoom={1.6}
        nodesDraggable
        nodeDragThreshold={2}
        panOnDrag
        zoomOnScroll
        proOptions={{ hideAttribution: true }}
      >
        <Background variant={BackgroundVariant.Dots} gap={22} size={1.2} color="rgba(160, 174, 192, 0.25)" />
        <MiniMap pannable zoomable className="mini-map" nodeStrokeWidth={3} />
        <Controls className="flow-controls" />
        <Panel position="top-left" className="graph-label">
          <strong>{graph.title}</strong>
          <span>{graph.summary || graph.kind}</span>
        </Panel>
      </ReactFlow>
    </main>
  );
}

function GraphNode({ data }) {
  const inputPins = data.inputPins ?? [];
  const outputPins = data.outputPins ?? [];
  const className = [
    'graph-node',
    `graph-node-${data.kind || 'state'}`,
    data.active ? 'is-active' : '',
    data.pathActive ? 'is-path-active' : '',
    data.selected ? 'is-selected' : '',
    data.dead ? 'is-dead' : '',
    data.implementationGraphId ? 'can-open' : ''
  ].filter(Boolean).join(' ');

  return (
    <div className={className}>
      {inputPins.map((pin, index) => (
        <Handle
          key={`in-${pin.id}`}
          id={pin.id}
          type="target"
          position={Position.Left}
          className="pin-handle pin-input"
          style={{ top: pinTop(index, inputPins.length) }}
        />
      ))}
      {outputPins.map((pin, index) => (
        <Handle
          key={`out-${pin.id}`}
          id={pin.id}
          type="source"
          position={Position.Right}
          className="pin-handle pin-output"
          style={{ top: pinTop(index, outputPins.length) }}
        />
      ))}
      <div className="node-head">
        <span>{data.kind}</span>
        {data.opCode && <code>{data.opCode}</code>}
      </div>
      {data.implementationGraphId && (
        <button
          type="button"
          className="node-open-button nodrag nopan"
          aria-label={`Open ${data.label} implementation graph`}
          onClick={(event) => {
            event.stopPropagation();
            data.onOpenGraph?.(data.id, data.implementationGraphId);
          }}
        >
          Open
        </button>
      )}
      <strong>{data.label}</strong>
      <p>{data.description}</p>
      {(inputPins.length > 0 || outputPins.length > 0) && (
        <div className="pin-grid">
          <PinList title="in" pins={inputPins} />
          <PinList title="out" pins={outputPins} />
        </div>
      )}
    </div>
  );
}

function PinList({ title, pins }) {
  return (
    <div className="pin-list">
      <span>{title}</span>
      {pins.length === 0 ? <em>none</em> : pins.map((pin) => (
        <small key={pin.id}>{pin.label}<b>{pin.type}</b></small>
      ))}
    </div>
  );
}

function InspectorPanel({ graph, runtime, selectedNode }) {
  const activeText = graph.kind === 'implementation'
    ? runtime.stateLabel || runtime.stateId || 'inactive'
    : runtime.statePath || 'inactive';

  return (
    <aside className="inspector">
      <section>
        <span>selected entity</span>
        <strong>hfsm-hero</strong>
        <p>{runtime.playerStory || 'Load the showcase map to attach live runtime data.'}</p>
      </section>
      <section>
        <span>current node</span>
        <strong>{activeText}</strong>
        <p>{runtime.lastEvent || 'No runtime event yet.'}</p>
      </section>
      <section>
        <span>selected graph node</span>
        <strong>{selectedNode?.label ?? 'none'}</strong>
        <p>{selectedNode?.description ?? 'Click a node to inspect its pins and implementation link.'}</p>
      </section>
    </aside>
  );
}

function resolveCurrentGraph(packet, viewGraphId) {
  const root = packet.rootGraph;
  if (!root) {
    return null;
  }

  if (!viewGraphId || viewGraphId === root.id) {
    return root;
  }

  return packet.implementations?.find((graph) => graph.id === viewGraphId) ?? root;
}

function buildFlowGraph(graph, packet, selectedNodeId, onOpenGraph = null) {
  const activeStatePath = new Set(packet.activeStatePathIds ?? []);
  const activeOps = new Set(packet.activeOpNodeIds ?? []);
  const nodes = (graph.nodes ?? []).map((node) => {
    const active = graph.kind === 'implementation'
      ? activeOps.has(node.id)
      : node.id === packet.activeStateId;
    const pathActive = graph.kind !== 'implementation' && activeStatePath.has(node.id);
    return {
      id: node.id,
      type: 'graphNode',
      position: { x: node.x ?? 0, y: node.y ?? 0 },
      data: {
        ...node,
        active,
        pathActive,
        selected: node.id === selectedNodeId,
        dead: packet.runtime?.dead === true && node.id === 'Dead',
        onOpenGraph
      }
    };
  });

  const edges = (graph.edges ?? []).map((edge) => {
    const activeEdge = graph.kind === 'implementation'
      ? activeOps.has(edge.from) || activeOps.has(edge.to)
      : activeStatePath.has(edge.from) && activeStatePath.has(edge.to);
    const stroke = activeEdge
      ? (packet.runtime?.dead ? DEAD_COLOR : ACTIVE_COLOR)
      : EDGE_COLORS[edge.kind] ?? '#91a0b5';
    return {
      id: edge.id,
      source: edge.from,
      target: edge.to,
      sourceHandle: edge.fromPin || undefined,
      targetHandle: edge.toPin || undefined,
      type: 'smoothstep',
      animated: activeEdge || edge.kind === 'interrupt',
      label: edge.label,
      className: activeEdge ? 'edge-active' : `edge-${edge.kind}`,
      style: {
        stroke,
        strokeWidth: activeEdge ? 3.5 : 2.2
      },
      markerEnd: {
        type: MarkerType.ArrowClosed,
        color: stroke
      }
    };
  });

  return { nodes, edges };
}

function mergeFlowNodes(current, nextNodes) {
  const currentById = new Map(current.map((node) => [node.id, node]));
  return nextNodes.map((node) => {
    const currentNode = currentById.get(node.id);
    if (!currentNode) {
      return node;
    }

    return {
      ...node,
      position: currentNode.position,
      measured: currentNode.measured,
      selected: currentNode.selected,
      dragging: currentNode.dragging
    };
  });
}

function graphDefinitionSignature(graph) {
  const nodes = (graph.nodes ?? []).map((node) => {
    const inputs = (node.inputPins ?? []).map(pinSignature).join(',');
    const outputs = (node.outputPins ?? []).map(pinSignature).join(',');
    return `${node.id}:${node.kind}:${node.label}:${node.opCode ?? ''}:${node.implementationGraphId ?? ''}:${node.x ?? 0}:${node.y ?? 0}:${inputs}:${outputs}`;
  }).join('|');
  const edges = (graph.edges ?? []).map((edge) =>
    `${edge.id}:${edge.from}:${edge.fromPin ?? ''}:${edge.to}:${edge.toPin ?? ''}:${edge.kind}:${edge.label ?? ''}`
  ).join('|');
  return `${graph.id}:${graph.kind}:${nodes}:${edges}`;
}

function pinSignature(pin) {
  return `${pin.id}:${pin.label}:${pin.type}`;
}

function pinTop(index, count) {
  if (count <= 1) {
    return '50%';
  }

  const min = 34;
  const max = 78;
  return `${min + ((max - min) * index) / (count - 1)}%`;
}

const rootElement = document.getElementById('root');
const root = globalThis.__ludotsHfsmGraphDebugRoot ?? createRoot(rootElement);
globalThis.__ludotsHfsmGraphDebugRoot = root;
root.render(
  <React.StrictMode>
    <ReactFlowProvider>
      <GraphDebugApp />
    </ReactFlowProvider>
  </React.StrictMode>
);
