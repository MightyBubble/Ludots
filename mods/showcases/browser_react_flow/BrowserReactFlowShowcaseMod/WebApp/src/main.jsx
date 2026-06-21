import React, { useCallback, useMemo, useRef, useState } from 'react';
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
  ViewportPortal,
  addEdge,
  useEdgesState,
  useNodesState
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import './styles.css';
import {
  DATA_PLANE_DEFAULT_TOPIC,
  createLudotsDataPlaneClient,
  ensureLudotsDataPlaneTransport
} from './dataplane/client.js';

const lanes = [
  { id: 'runtime', label: 'Runtime', color: '#64d2ff' },
  { id: 'ui', label: 'Skia UI', color: '#8ee6a8' },
  { id: 'browser', label: 'CEF Surface', color: '#f8d66d' },
  { id: 'mods', label: 'Mods', color: '#ff9fb7' }
];

const laneWidth = 320;
const laneHeight = 720;
const laneGap = 360;

const initialInteractionState = {
  dragEvents: 0,
  dragStops: 0,
  moveEvents: 0,
  wheelEvents: 0,
  paneClicks: 0,
  lastEvent: 'ready',
  lastNode: 'none',
  lastPosition: 'n/a',
  viewport: { x: 0, y: 0, zoom: 1 }
};

const initialRawInputState = {
  down: 0,
  move: 0,
  up: 0,
  wheel: 0,
  pointerDown: 0,
  pointerMove: 0,
  pointerUp: 0,
  last: 'none',
  pointerLast: 'none',
  button: 0,
  buttons: 0,
  pointerButtons: 0,
  target: 'none',
  x: 0,
  y: 0
};

const initialDataPlaneState = {
  phase: 'boot',
  transport: 'none',
  sessionId: 'pending',
  topic: DATA_PLANE_DEFAULT_TOPIC,
  tick: 0,
  entityCount: 0,
  selectedEntityId: 'none',
  lastPacket: 'none',
  lastCommand: 'none',
  commandAcks: 0,
  coalescedPackets: 0,
  droppedPackets: 0,
  binaryBytes: 0,
  error: '',
  rows: []
};

function resolveShowcaseMode() {
  const params = new URLSearchParams(window.location.search);
  return params.get('perf') === 'baseline' || params.get('mode') === 'baseline'
    ? 'baseline'
    : 'react-flow';
}

function buildInitialGraph() {
  const nodes = [];
  const edges = [];

  lanes.forEach((lane, laneIndex) => {
    const laneX = laneIndex * laneGap;

    for (let step = 0; step < 7; step += 1) {
      const nodeId = `${lane.id}-${step}`;
      nodes.push({
        id: nodeId,
        type: 'stageNode',
        position: {
          x: laneX + 30 + (step % 2) * 38,
          y: 56 + step * 88
        },
        sourcePosition: Position.Right,
        targetPosition: Position.Left,
        data: {
          index: `${laneIndex + 1}.${step + 1}`,
          title: `${lane.label} ${step + 1}`,
          detail: resolveDetail(lane.id, step),
          color: lane.color
        }
      });

      if (step > 0) {
        edges.push({
          id: `${lane.id}-${step - 1}-${step}`,
          source: `${lane.id}-${step - 1}`,
          target: nodeId,
          type: 'smoothstep',
          animated: step % 2 === 0,
          className: 'lane-edge',
          style: { stroke: lane.color, strokeWidth: 3.2 },
          markerEnd: { type: MarkerType.ArrowClosed, color: lane.color }
        });
      }
    }
  });

  for (let laneIndex = 0; laneIndex < lanes.length - 1; laneIndex += 1) {
    for (let step = 0; step < 7; step += 2) {
      const sourceLane = lanes[laneIndex];
      const targetLane = lanes[laneIndex + 1];
      edges.push({
        id: `cross-${sourceLane.id}-${targetLane.id}-${step}`,
        source: `${sourceLane.id}-${step}`,
        target: `${targetLane.id}-${Math.min(6, step + 1)}`,
        type: 'bezier',
        animated: true,
        className: 'cross-edge',
        style: { stroke: '#f7fbff', strokeWidth: 2.4, strokeDasharray: '7 5' },
        markerEnd: { type: MarkerType.ArrowClosed, color: '#f7fbff' }
      });
    }
  }

  return { nodes, edges };
}

function resolveDetail(laneId, step) {
  const details = {
    runtime: ['launch preset', 'load mods', 'register services', 'start game', 'tick world', 'present frame', 'shutdown cleanly'],
    ui: ['compose scene', 'measure text', 'layout tree', 'draw canvas', 'copy browser frame', 'alpha blend', 'mark dirty'],
    browser: ['init CEF', 'create OSR view', 'serve resources', 'execute React', 'paint BGRA', 'post message', 'release surface'],
    mods: ['discover bundle', 'resolve assets', 'mount webview', 'send host ack', 'observe status', 'package app', 'reuse runtime']
  };

  return details[laneId][step];
}

function StageNode({ data }) {
  return (
    <div className="stage-node" style={{ '--node-accent': data.color }}>
      <Handle type="target" position={Position.Left} />
      <div className="stage-index">{data.index}</div>
      <div className="stage-copy">
        <strong>{data.title}</strong>
        <span>{data.detail}</span>
      </div>
      <Handle type="source" position={Position.Right} />
    </div>
  );
}

function LaneBackgrounds() {
  return (
    <ViewportPortal>
      {lanes.map((lane, laneIndex) => (
        <div
          key={lane.id}
          className="lane-bg"
          style={{
            '--lane-accent': lane.color,
            left: laneIndex * laneGap,
            top: 0,
            width: laneWidth,
            height: laneHeight
          }}
        >
          <span>{lane.label}</span>
        </div>
      ))}
    </ViewportPortal>
  );
}

function useLudotsDataPlane() {
  const clientRef = useRef(null);
  const [state, setState] = useState(initialDataPlaneState);

  React.useEffect(() => {
    let active = true;
    const { transport, installedFake } = ensureLudotsDataPlaneTransport();
    const client = createLudotsDataPlaneClient({
      transport,
      installedFake,
      diagnostics: (diagnostic) => {
        if (!active || diagnostic.level !== 'error') {
          return;
        }

        React.startTransition(() => {
          setState((current) => ({
            ...current,
            error: diagnostic.message,
            lastPacket: diagnostic.type
          }));
        });
      }
    });
    clientRef.current = client;

    React.startTransition(() => {
      setState((current) => ({
        ...current,
        phase: 'connecting',
        transport: transport?.name ?? 'unknown',
        error: ''
      }));
    });

    client
      .handshake({ app: 'browser-react-flow-showcase' })
      .then((handshake) => {
        if (!active) {
          return null;
        }

        React.startTransition(() => {
          setState((current) => ({
            ...current,
            phase: 'connected',
            sessionId: handshake.sessionId ?? handshake.payload?.sessionId ?? current.sessionId,
            transport: handshake.payload?.transportName ?? transport?.name ?? current.transport
          }));
        });
        return client.subscribe(DATA_PLANE_DEFAULT_TOPIC, (event) => {
          if (!active) {
            return;
          }

          React.startTransition(() => {
            setState((current) => reduceDataPlaneEvent(current, event));
          });
        });
      })
      .catch((error) => {
        if (!active) {
          return;
        }

        React.startTransition(() => {
          setState((current) => ({
            ...current,
            phase: 'error',
            error: error instanceof Error ? error.message : String(error)
          }));
        });
      });

    return () => {
      active = false;
      client.close();
      if (transport?.dispose) {
        transport.dispose();
      }
    };
  }, []);

  const command = useCallback(async (name, payload) => {
    const client = clientRef.current;
    if (!client) {
      return;
    }

    React.startTransition(() => {
      setState((current) => ({
        ...current,
        lastCommand: `${name}:pending`,
        error: ''
      }));
    });

    try {
      const response = await client.command(name, payload);
      React.startTransition(() => {
        setState((current) => ({
          ...current,
          lastCommand: `${name}:ack`,
          commandAcks: current.commandAcks + 1,
          selectedEntityId: payload.nodeId ?? current.selectedEntityId,
          error: response.payload?.message ?? current.error
        }));
      });
    } catch (error) {
      React.startTransition(() => {
        setState((current) => ({
          ...current,
          lastCommand: `${name}:error`,
          error: error instanceof Error ? error.message : String(error)
        }));
      });
    }
  }, []);

  return { state, command };
}

function reduceDataPlaneEvent(current, event) {
  if (event.kind === 'binaryChunk') {
    return {
      ...current,
      lastPacket: `${event.packetKind}:binary`,
      binaryBytes: current.binaryBytes + (event.binaryBytes ?? 0)
    };
  }

  const payload = event.payload ?? {};
  const rows = Array.isArray(payload.entities)
    ? payload.entities
    : Array.isArray(payload.entityPatches)
      ? payload.entityPatches
      : current.rows;
  const diagnostics = payload.diagnostics ?? payload.metrics ?? {};

  return {
    ...current,
    phase: 'streaming',
    topic: event.topic ?? current.topic,
    sessionId: event.sessionId ?? current.sessionId,
    tick: payload.tick ?? current.tick,
    entityCount: payload.entityCount ?? rows.length ?? current.entityCount,
    selectedEntityId: payload.selectedEntityId ?? current.selectedEntityId,
    lastPacket: event.kind,
    coalescedPackets: diagnostics.coalescedPackets ?? current.coalescedPackets,
    droppedPackets: diagnostics.droppedPackets ?? current.droppedPackets,
    rows
  };
}

function FlowShowcase() {
  const initialGraph = useMemo(buildInitialGraph, []);
  const [nodes, setNodes, onNodesChange] = useNodesState(initialGraph.nodes);
  const [edges, setEdges, onEdgesChange] = useEdgesState(initialGraph.edges);
  const interactionRef = useRef(initialInteractionState);
  const rawInputRef = useRef(initialRawInputState);
  const interactionFrameRef = useRef(0);
  const rawInputFrameRef = useRef(0);
  const [interaction, setInteraction] = useState(initialInteractionState);
  const [rawInput, setRawInput] = useState(initialRawInputState);
  const [keyboardProbe, setKeyboardProbe] = useState('');
  const [keyboardStatus, setKeyboardStatus] = useState('waiting for text input');
  const nodeTypes = useMemo(() => ({ stageNode: StageNode }), []);
  const dataPlane = useLudotsDataPlane();

  const publishInteraction = useCallback((next, important = false) => {
    window.__LUDOTS_REACT_FLOW_INTERACTION__ = next;
    if (important && window.CefSharp?.PostMessage) {
      window.CefSharp.PostMessage({
        source: 'browser-react-flow-showcase',
        type: 'interaction',
        dragEvents: next.dragEvents,
        dragStops: next.dragStops,
        moveEvents: next.moveEvents,
        wheelEvents: next.wheelEvents,
        paneClicks: next.paneClicks,
        lastEvent: next.lastEvent,
        lastNode: next.lastNode,
        lastPosition: next.lastPosition,
        viewport: next.viewport
      });
    }
  }, []);

  const flushInteractionPanel = useCallback(() => {
    interactionFrameRef.current = 0;
    React.startTransition(() => setInteraction(interactionRef.current));
  }, []);

  const scheduleInteractionPanel = useCallback(() => {
    if (interactionFrameRef.current !== 0) {
      return;
    }

    interactionFrameRef.current = window.requestAnimationFrame(flushInteractionPanel);
  }, [flushInteractionPanel]);

  const commitInteractionPanel = useCallback((next, important) => {
    if (important) {
      if (interactionFrameRef.current !== 0) {
        window.cancelAnimationFrame(interactionFrameRef.current);
        interactionFrameRef.current = 0;
      }

      React.startTransition(() => setInteraction(next));
      return;
    }

    scheduleInteractionPanel();
  }, [scheduleInteractionPanel]);

  const updateInteraction = useCallback(
    (updater, important = false) => {
      const current = interactionRef.current;
      const next = typeof updater === 'function' ? updater(current) : { ...current, ...updater };
      interactionRef.current = next;
      publishInteraction(next, important);
      commitInteractionPanel(next, important);
    },
    [commitInteractionPanel, publishInteraction]
  );

  const flushRawInputPanel = useCallback(() => {
    rawInputFrameRef.current = 0;
    React.startTransition(() => setRawInput(rawInputRef.current));
  }, []);

  const scheduleRawInputPanel = useCallback(() => {
    if (rawInputFrameRef.current !== 0) {
      return;
    }

    rawInputFrameRef.current = window.requestAnimationFrame(flushRawInputPanel);
  }, [flushRawInputPanel]);

  React.useEffect(() => () => {
    if (interactionFrameRef.current !== 0) {
      window.cancelAnimationFrame(interactionFrameRef.current);
      interactionFrameRef.current = 0;
    }

    if (rawInputFrameRef.current !== 0) {
      window.cancelAnimationFrame(rawInputFrameRef.current);
      rawInputFrameRef.current = 0;
    }
  }, []);

  const onConnect = useCallback(
    (connection) =>
      setEdges((currentEdges) =>
        addEdge(
          {
            ...connection,
            animated: true,
            type: 'smoothstep',
            style: { stroke: '#ffffff', strokeWidth: 2 }
          },
          currentEdges
        )
      ),
    [setEdges]
  );

  const formatNodePosition = useCallback((node) => {
    const x = Math.round(node.position.x);
    const y = Math.round(node.position.y);
    return `${x}, ${y}`;
  }, []);

  const onNodeDragStart = useCallback(
    (_event, node) => {
      updateInteraction(
        (current) => ({
          ...current,
          dragEvents: current.dragEvents + 1,
          lastEvent: 'drag-start',
          lastNode: node.data?.title ?? node.id,
          lastPosition: formatNodePosition(node)
        }),
        true
      );
    },
    [formatNodePosition, updateInteraction]
  );

  const onNodeDrag = useCallback(
    (_event, node) => {
      updateInteraction((current) => ({
        ...current,
        dragEvents: current.dragEvents + 1,
        lastEvent: 'dragging',
        lastNode: node.data?.title ?? node.id,
        lastPosition: formatNodePosition(node)
      }));
    },
    [formatNodePosition, updateInteraction]
  );

  const onNodeDragStop = useCallback(
    (_event, node) => {
      updateInteraction(
        (current) => ({
          ...current,
          dragEvents: current.dragEvents + 1,
          dragStops: current.dragStops + 1,
          lastEvent: 'drag-stop',
          lastNode: node.data?.title ?? node.id,
          lastPosition: formatNodePosition(node)
        }),
        true
      );
      dataPlane.command('issueMoveOrder', {
        nodeId: node.id,
        target: {
          x: Math.round(node.position.x),
          y: Math.round(node.position.y)
        }
      });
    },
    [dataPlane, formatNodePosition, updateInteraction]
  );

  const onNodeClick = useCallback(
    (_event, node) => {
      updateInteraction(
        (current) => ({
          ...current,
          lastEvent: 'node-click',
          lastNode: node.data?.title ?? node.id,
          lastPosition: formatNodePosition(node)
        }),
        true
      );
      dataPlane.command('inspectEntity', {
        nodeId: node.id,
        title: node.data?.title ?? node.id
      });
    },
    [dataPlane, formatNodePosition, updateInteraction]
  );

  const onMove = useCallback(
    (_event, viewport) => {
      updateInteraction((current) => ({
        ...current,
        moveEvents: current.moveEvents + 1,
        lastEvent: current.lastEvent === 'ready' ? 'viewport' : current.lastEvent,
        viewport
      }));
    },
    [updateInteraction]
  );

  const onPaneClick = useCallback(
    () => {
      updateInteraction(
        (current) => ({
          ...current,
          paneClicks: current.paneClicks + 1,
          lastEvent: 'pane-click'
        }),
        true
      );
    },
    [updateInteraction]
  );

  const onPaneScroll = useCallback(
    () => {
      updateInteraction((current) => ({
        ...current,
        wheelEvents: current.wheelEvents + 1,
        lastEvent: 'wheel'
      }));
    },
    [updateInteraction]
  );

  React.useEffect(() => {
    const formatTarget = (event) => {
      const target = event.target;
      if (!(target instanceof Element)) {
        return 'none';
      }

      if (target.classList.contains('react-flow__node') || target.closest('.react-flow__node')) {
        return 'node';
      }

      if (target.classList.contains('react-flow__pane') || target.closest('.react-flow__pane')) {
        return 'pane';
      }

      if (target.classList.contains('react-flow__handle') || target.closest('.react-flow__handle')) {
        return 'handle';
      }

      return target.className?.toString().split(' ')[0] || target.tagName.toLowerCase();
    };

    const updateRawInput = (eventName, kind = 'mouse') => (event) => {
      const current = rawInputRef.current;
      const countKey = kind === 'pointer'
        ? eventName === 'down'
          ? 'pointerDown'
          : eventName === 'move'
            ? 'pointerMove'
            : 'pointerUp'
        : eventName;
      const next = {
        ...current,
        [countKey]: current[countKey] + 1,
        last: kind === 'mouse' ? eventName : current.last,
        pointerLast: kind === 'pointer' ? eventName : current.pointerLast,
        button: typeof event.button === 'number' ? event.button : current.button,
        buttons: typeof event.buttons === 'number' ? event.buttons : current.buttons,
        pointerButtons: kind === 'pointer' && typeof event.buttons === 'number' ? event.buttons : current.pointerButtons,
        target: formatTarget(event),
        x: Math.round(event.clientX ?? 0),
        y: Math.round(event.clientY ?? 0)
      };

      rawInputRef.current = next;
      window.__LUDOTS_REACT_FLOW_RAW_INPUT__ = next;
      if ((eventName === 'down' || eventName === 'up' || eventName === 'wheel') && window.CefSharp?.PostMessage) {
        window.CefSharp.PostMessage({
          source: 'browser-react-flow-showcase',
          type: 'raw-input',
          event: eventName,
          x: next.x,
          y: next.y,
          down: next.down,
          move: next.move,
          up: next.up,
          wheel: next.wheel
        });
      }

      scheduleRawInputPanel();
    };

    const onMouseDown = updateRawInput('down');
    const onMouseMove = updateRawInput('move');
    const onMouseUp = updateRawInput('up');
    const onWheel = updateRawInput('wheel');
    const onPointerDown = updateRawInput('down', 'pointer');
    const onPointerMove = updateRawInput('move', 'pointer');
    const onPointerUp = updateRawInput('up', 'pointer');

    const eventOptions = { capture: true, passive: true };
    window.addEventListener('mousedown', onMouseDown, eventOptions);
    window.addEventListener('mousemove', onMouseMove, eventOptions);
    window.addEventListener('mouseup', onMouseUp, eventOptions);
    window.addEventListener('wheel', onWheel, eventOptions);
    window.addEventListener('pointerdown', onPointerDown, eventOptions);
    window.addEventListener('pointermove', onPointerMove, eventOptions);
    window.addEventListener('pointerup', onPointerUp, eventOptions);

    return () => {
      window.removeEventListener('mousedown', onMouseDown, eventOptions);
      window.removeEventListener('mousemove', onMouseMove, eventOptions);
      window.removeEventListener('mouseup', onMouseUp, eventOptions);
      window.removeEventListener('wheel', onWheel, eventOptions);
      window.removeEventListener('pointerdown', onPointerDown, eventOptions);
      window.removeEventListener('pointermove', onPointerMove, eventOptions);
      window.removeEventListener('pointerup', onPointerUp, eventOptions);
    };
  }, [scheduleRawInputPanel]);

  React.useEffect(() => {
    const message = {
      source: 'browser-react-flow-showcase',
      package: '@xyflow/react',
      nodes: initialGraph.nodes.length,
      edges: initialGraph.edges.length,
      alpha: 'transparent-body'
    };

    if (window.CefSharp?.PostMessage) {
      window.CefSharp.PostMessage(message);
    }

    window.__LUDOTS_REACT_FLOW_READY__ = message;
  }, [initialGraph]);

  React.useEffect(() => {
    const publishResize = () => {
      const payload = {
        source: 'browser-react-flow-showcase',
        type: 'viewport-resize',
        width: window.innerWidth,
        height: window.innerHeight,
        devicePixelRatio: window.devicePixelRatio || 1
      };
      window.__LUDOTS_REACT_FLOW_VIEWPORT__ = payload;
      if (window.CefSharp?.PostMessage) {
        window.CefSharp.PostMessage(payload);
      }
    };

    publishResize();
    window.addEventListener('resize', publishResize);
    return () => window.removeEventListener('resize', publishResize);
  }, []);

  return (
    <div className="showcase-shell">
      <ReactFlow
        className="react-flow-alpha-cutout"
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onConnect={onConnect}
        onMove={onMove}
        onNodeDragStart={onNodeDragStart}
        onNodeDrag={onNodeDrag}
        onNodeDragStop={onNodeDragStop}
        onNodeClick={onNodeClick}
        onPaneClick={onPaneClick}
        onPaneScroll={onPaneScroll}
        fitView
        minZoom={0.35}
        maxZoom={1.8}
        nodesDraggable
        nodeDragThreshold={1}
        defaultEdgeOptions={{
          type: 'smoothstep',
          markerEnd: { type: MarkerType.ArrowClosed }
        }}
      >
        <LaneBackgrounds />
        <Background variant={BackgroundVariant.Dots} gap={24} size={1.5} color="rgba(255,255,255,0.28)" />
        <MiniMap pannable zoomable nodeStrokeWidth={3} className="mini-map" />
        <Controls className="flow-controls" />
        <Panel position="top-left" className="top-panel">
          <div>
            <span className="eyebrow">CEF offscreen proof</span>
            <h1>React Flow in Ludots</h1>
          </div>
          <p>
            Vite + React + @xyflow/react rendered as a packaged browser app, composited over Raylib through Skia with alpha.
          </p>
        </Panel>
        <Panel position="bottom-right" className="metrics-panel">
          <div><strong>{nodes.length}</strong><span>nodes</span></div>
          <div><strong>{edges.length}</strong><span>edges</span></div>
          <div><strong>alpha</strong><span>transparent</span></div>
          <div><strong>{dataPlane.state.entityCount}</strong><span>entities</span></div>
          <div><strong>{dataPlane.state.tick}</strong><span>tick</span></div>
        </Panel>
        <Panel position="top-right" className="dataplane-panel">
          <div className="keyboard-probe">
            <label htmlFor="keyboard-probe-input">Keyboard probe</label>
            <input
              id="keyboard-probe-input"
              value={keyboardProbe}
              onChange={(event) => {
                const value = event.target.value;
                setKeyboardProbe(value);
                setKeyboardStatus(`input: ${value || 'empty'}`);
                window.__LUDOTS_REACT_FLOW_KEYBOARD__ = {
                  ...(window.__LUDOTS_REACT_FLOW_KEYBOARD__ ?? {}),
                  value,
                  event: 'input'
                };
              }}
              onKeyDown={(event) => {
                window.__LUDOTS_REACT_FLOW_KEYBOARD__ = {
                  value: event.currentTarget.value,
                  key: event.key,
                  code: event.code,
                  event: 'keydown'
                };
                setKeyboardStatus(`key: ${event.key}`);
              }}
              placeholder="click and type"
            />
            <span>{keyboardStatus}</span>
          </div>
          <div className="dataplane-head">
            <strong>{dataPlane.state.phase}</strong>
            <span>{dataPlane.state.transport}</span>
          </div>
          <div className="dataplane-grid">
            <div><strong>{dataPlane.state.lastPacket}</strong><span>packet</span></div>
            <div><strong>{dataPlane.state.commandAcks}</strong><span>acks</span></div>
            <div><strong>{dataPlane.state.coalescedPackets}</strong><span>coalesced</span></div>
            <div><strong>{dataPlane.state.binaryBytes}</strong><span>binary bytes</span></div>
          </div>
          <div className="dataplane-list">
            {dataPlane.state.rows.slice(0, 3).map((row) => (
              <button
                key={row.id ?? row.stableId ?? row.label}
                type="button"
                onClick={() => dataPlane.command('inspectEntity', { nodeId: row.id ?? row.label })}
              >
                <span>{row.label ?? row.id ?? row.stableId}</span>
                <strong>{row.hp ?? row.changes?.hp ?? 'ok'}</strong>
              </button>
            ))}
          </div>
          <p>{dataPlane.state.error || dataPlane.state.topic}</p>
        </Panel>
        <Panel position="bottom-center" className="interaction-panel">
          <div><strong>{interaction.lastEvent}</strong><span>event</span></div>
          <div><strong>{interaction.dragEvents}</strong><span>drag events</span></div>
          <div><strong>{interaction.dragStops}</strong><span>drag stops</span></div>
          <div><strong>{interaction.lastNode}</strong><span>{interaction.lastPosition}</span></div>
          <div><strong>{interaction.moveEvents}</strong><span>{interaction.viewport.zoom.toFixed(2)}x zoom</span></div>
          <div><strong>{rawInput.down}/{rawInput.move}/{rawInput.up}</strong><span>{rawInput.last} b{rawInput.button}/{rawInput.buttons}</span></div>
          <div><strong>{rawInput.pointerDown}/{rawInput.pointerMove}/{rawInput.pointerUp}</strong><span>ptr {rawInput.pointerLast} {rawInput.target}</span></div>
        </Panel>
        <Panel position="bottom-left" className="alpha-panel">
          <button
            type="button"
            onClick={() => {
              window.__LUDOTS_REACT_FLOW_ALPHA_HUD__ = (window.__LUDOTS_REACT_FLOW_ALPHA_HUD__ ?? 0) + 1;
            }}
          >
            Web HUD click target
          </button>
          <span>opaque browser pixels</span>
        </Panel>
      </ReactFlow>
    </div>
  );
}

function PerfBaselineApp() {
  const canvasRef = useRef(null);
  const [stats, setStats] = useState({
    frames: 0,
    fps: 0,
    lastInput: 'none',
    keyEcho: ''
  });

  React.useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) {
      return undefined;
    }

    const context = canvas.getContext('2d', { alpha: true });
    let frame = 0;
    let raf = 0;
    let lastSample = performance.now();
    let sampleFrames = 0;

    const resize = () => {
      const dpr = window.devicePixelRatio || 1;
      canvas.width = Math.max(1, Math.floor(window.innerWidth * dpr));
      canvas.height = Math.max(1, Math.floor(window.innerHeight * dpr));
      canvas.style.width = `${window.innerWidth}px`;
      canvas.style.height = `${window.innerHeight}px`;
      context.setTransform(dpr, 0, 0, dpr, 0, 0);
    };

    const draw = (time) => {
      frame += 1;
      sampleFrames += 1;
      context.clearRect(0, 0, window.innerWidth, window.innerHeight);
      context.fillStyle = 'rgba(4, 10, 18, 0.68)';
      context.fillRect(24, 24, 360, 126);
      context.strokeStyle = 'rgba(126, 231, 178, 0.92)';
      context.lineWidth = 2;
      context.strokeRect(24, 24, 360, 126);
      context.fillStyle = '#f7fbff';
      context.font = '600 18px system-ui, sans-serif';
      context.fillText('Browser perf baseline', 44, 64);
      context.font = '13px system-ui, sans-serif';
      context.fillStyle = 'rgba(232, 244, 255, 0.78)';
      context.fillText('No React Flow, no DataPlane publisher, one deterministic canvas.', 44, 94);
      context.fillText(`frame ${frame}  pulse ${Math.round((Math.sin(time * 0.004) + 1) * 50)}`, 44, 122);

      const elapsed = time - lastSample;
      if (elapsed >= 500) {
        const fps = Math.round((sampleFrames * 1000) / elapsed);
        setStats((current) => ({
          ...current,
          frames: frame,
          fps
        }));
        lastSample = time;
        sampleFrames = 0;
      }

      raf = window.requestAnimationFrame(draw);
    };

    resize();
    window.addEventListener('resize', resize);
    raf = window.requestAnimationFrame(draw);
    window.__LUDOTS_BROWSER_PERF_BASELINE_READY__ = true;

    return () => {
      window.cancelAnimationFrame(raf);
      window.removeEventListener('resize', resize);
    };
  }, []);

  return (
    <div
      className="perf-shell"
      onPointerDown={(event) => setStats((current) => ({ ...current, lastInput: `pointer ${Math.round(event.clientX)},${Math.round(event.clientY)}` }))}
      onWheel={(event) => setStats((current) => ({ ...current, lastInput: `wheel ${Math.round(event.deltaY)}` }))}
    >
      <canvas ref={canvasRef} className="perf-canvas" />
      <div className="perf-panel">
        <strong>{stats.fps} fps</strong>
        <span>{stats.frames} frames</span>
        <span>{stats.lastInput}</span>
        <input
          value={stats.keyEcho}
          onChange={(event) => setStats((current) => ({ ...current, keyEcho: event.target.value }))}
          placeholder="keyboard probe"
        />
      </div>
    </div>
  );
}

function App() {
  if (resolveShowcaseMode() === 'baseline') {
    return <PerfBaselineApp />;
  }

  return (
    <ReactFlowProvider>
      <FlowShowcase />
    </ReactFlowProvider>
  );
}

createRoot(document.getElementById('root')).render(<App />);
