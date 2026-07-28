import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import {
  Background,
  BackgroundVariant,
  Controls,
  Handle,
  MarkerType,
  MiniMap,
  Position,
  ReactFlow,
  ReactFlowProvider,
  addEdge,
  applyNodeChanges,
  useEdgesState,
  useNodesState
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import {
  GRAPH_WORKBENCH_TOPIC,
  createLudotsDataPlaneClient,
  ensureLudotsDataPlaneTransport
} from './dataplane/client.js';
import './styles.css';

const MODE_LABELS = {
  graph: 'Graph',
  fsm: 'FSM',
  bt: 'BT'
};

const EMPTY_CONNECTION = {
  phase: 'boot',
  transport: 'none',
  sessionId: 'pending',
  topic: GRAPH_WORKBENCH_TOPIC,
  lastPacket: 'none',
  lastCommand: 'none',
  commandAcks: 0,
  error: ''
};

function App() {
  const workbench = useGraphWorkbenchDataPlane();

  if (!workbench.snapshot && workbench.connection.phase !== 'streaming') {
    return <MissingHost connection={workbench.connection} />;
  }

  return (
    <ReactFlowProvider>
      <GraphWorkbench workbench={workbench} />
    </ReactFlowProvider>
  );
}

function MissingHost({ connection }) {
  return (
    <main className="missing-host">
      <section>
        <h1>Graph Workbench</h1>
        <p>Ludots DataPlane required.</p>
        <strong>{connection.phase}</strong>
        <span>{connection.error || 'Waiting for CEF host.'}</span>
      </section>
    </main>
  );
}

function GraphWorkbench({ workbench }) {
  const {
    snapshot,
    draft,
    setDraftDocument,
    command,
    connection,
    dirty
  } = workbench;
  const document = draft ?? snapshot.document;
  const runtime = snapshot.runtime;
  const compile = snapshot.compile;
  const [view, setView] = useState(() => ({
    mode: 'graph',
    id: document.activeGraphId || document.graphs[0]?.id || '',
    title: findGraph(document, document.activeGraphId)?.title || 'Graph'
  }));
  const [breadcrumbs, setBreadcrumbs] = useState([]);
  const [selectedNodeId, setSelectedNodeId] = useState('');
  const [notice, setNotice] = useState('');

  useEffect(() => {
    if (!view.id) {
      setView({
        mode: 'graph',
        id: document.activeGraphId || document.graphs[0]?.id || '',
        title: findGraph(document, document.activeGraphId)?.title || 'Graph'
      });
    }
  }, [document, view.id]);

  const activeDocument = useMemo(() => resolveActiveDocument(document, view), [document, view]);
  const selectedNode = useMemo(
    () => activeDocument?.nodes?.find((node) => node.id === selectedNodeId) ?? null,
    [activeDocument, selectedNodeId]
  );
  const activeEntity = useMemo(
    () => runtime.entities.find((entity) => entity.id === runtime.selectedEntityId) ?? runtime.entities[0],
    [runtime]
  );
  const flow = useMemo(
    () => buildFlow(activeDocument, view, runtime, selectedNodeId),
    [activeDocument, runtime, selectedNodeId, view]
  );
  const [nodes, setNodes] = useNodesState(flow.nodes);
  const [edges, setEdges, onEdgesChange] = useEdgesState(flow.edges);

  useEffect(() => {
    setNodes(flow.nodes);
    setEdges(flow.edges);
  }, [flow, setEdges, setNodes]);

  const updateDocument = useCallback(
    (updater) => {
      setDraftDocument((current) => {
        const next = cloneDocument(current);
        updater(next);
        next.revision = (next.revision ?? 0) + 1;
        return next;
      });
    },
    [setDraftDocument]
  );

  const setMode = useCallback(
    (mode) => {
      const next = resolveModeDefaultView(document, mode);
      setBreadcrumbs([]);
      setSelectedNodeId('');
      setNotice('');
      setView(next);
      void command('setActiveDocument', modeToActivePayload(mode, next.id)).catch((error) => {
        setNotice(formatError(error));
      });
    },
    [command, document]
  );

  const onNodesChange = useCallback(
    (changes) => {
      setNodes((current) => applyNodeChanges(changes, current));
    },
    [setNodes]
  );

  const onNodeClick = useCallback((_event, node) => {
    setSelectedNodeId(node.id);
    setNotice('');
  }, []);

  const onNodeDoubleClick = useCallback(
    (_event, node) => {
      const implementationGraphId = node.data?.implementationGraphId ?? '';
      if (!implementationGraphId) {
        setSelectedNodeId(node.id);
        setNotice('该节点没有实现图绑定。');
        return;
      }

      const graph = findGraph(document, implementationGraphId);
      if (!graph) {
        setSelectedNodeId(node.id);
        setNotice(`找不到实现图 ${implementationGraphId}。`);
        return;
      }

      setBreadcrumbs((current) => [
        ...current,
        { mode: view.mode, id: view.id, title: activeDocument?.title ?? MODE_LABELS[view.mode], selectedNodeId: node.id }
      ]);
      setView({ mode: 'graph', id: graph.id, title: graph.title });
      setSelectedNodeId('');
      setNotice('');
      void command('setActiveDocument', { graphId: graph.id }).catch((error) => {
        setNotice(formatError(error));
      });
    },
    [activeDocument, command, document, view]
  );

  const onNodeDragStop = useCallback(
    (_event, node) => {
      updateDocument((next) => {
        const target = resolveActiveDocument(next, view);
        const item = target?.nodes?.find((candidate) => candidate.id === node.id);
        if (item) {
          item.x = Math.round(node.position.x);
          item.y = Math.round(node.position.y);
        }
      });
    },
    [updateDocument, view]
  );

  const onConnect = useCallback(
    (connection) => {
      const edgeId = `${connection.source}-${connection.target}-${Date.now().toString(16)}`;
      setEdges((current) =>
        addEdge({
          ...connection,
          id: edgeId,
          type: 'smoothstep',
          animated: true,
          markerEnd: { type: MarkerType.ArrowClosed }
        }, current)
      );
      updateDocument((next) => {
        const target = resolveActiveDocument(next, view);
        target?.edges?.push({
          id: edgeId,
          source: connection.source,
          target: connection.target,
          label: view.mode === 'graph' ? 'next' : 'link',
          role: 'next'
        });
      });
    },
    [setEdges, updateDocument, view]
  );

  const addNodeFromPalette = useCallback(
    (kind) => {
      updateDocument((next) => {
        const target = resolveActiveDocument(next, view);
        if (!target) {
          return;
        }

        const suffix = (target.nodes.length + 1).toString().padStart(2, '0');
        const id = `${target.id}.node_${suffix}`;
        target.nodes.push({
          id,
          label: kind,
          kind: view.mode === 'graph' ? 'GraphOp' : kind,
          op: view.mode === 'graph' ? kind : '',
          implementationGraphId: '',
          x: 120 + target.nodes.length * 42,
          y: 160 + target.nodes.length * 28,
          intValue: 0,
          floatValue: 0,
          boolValue: false,
          tag: '',
          attribute: '',
          effectTemplate: '',
          inputs: []
        });
        setSelectedNodeId(id);
      });
    },
    [updateDocument, view]
  );

  const compileNow = useCallback(async () => {
    setNotice('');
    try {
      await command('compileDocument', { document });
      setNotice('编译成功，运行中程序已更新。');
    } catch (error) {
      setNotice(error instanceof Error ? error.message : String(error));
    }
  }, [command, document]);

  const selectEntity = useCallback(
    async (entityId) => {
      await command('selectEntity', { entityId });
    },
    [command]
  );

  const goBreadcrumb = useCallback(
    (index) => {
      const target = breadcrumbs[index];
      if (!target) {
        return;
      }

      setBreadcrumbs((current) => current.slice(0, index));
      setView({ mode: target.mode, id: target.id, title: target.title });
      setSelectedNodeId(target.selectedNodeId);
      setNotice('');
    },
    [breadcrumbs]
  );

  return (
    <main className="workbench-shell">
      <header className="topbar">
        <div className="brand-block">
          <h1>Graph Workbench</h1>
          <span>{connection.phase} / rev {runtime.appliedRevision}</span>
        </div>
        <div className="mode-tabs" role="tablist">
          {Object.entries(MODE_LABELS).map(([mode, label]) => (
            <button
              key={mode}
              type="button"
              className={view.mode === mode ? 'active' : ''}
              onClick={() => setMode(mode)}
            >
              {label}
            </button>
          ))}
        </div>
        <div className="compile-strip">
          <span className={compile.success ? 'compile-ok' : 'compile-bad'}>
            {compile.success ? 'Compiled' : 'Draft'}
          </span>
          <span>{dirty ? 'editing' : connection.lastPacket}</span>
          <button type="button" onClick={compileNow}>Compile</button>
        </div>
      </header>

      <section className="content-grid">
        <aside className="left-rail">
          <DocumentList document={document} view={view} setView={setView} setBreadcrumbs={setBreadcrumbs} />
          <PalettePanel mode={view.mode} palette={snapshot.palette} onAdd={addNodeFromPalette} />
          <EntityList entities={runtime.entities} selectedId={runtime.selectedEntityId} onSelect={selectEntity} />
        </aside>

        <section className="canvas-column">
          <Breadcrumbs
            root={view}
            trail={breadcrumbs}
            activeTitle={activeDocument?.title ?? view.title}
            onClick={goBreadcrumb}
          />
          <div className="flow-surface">
            <ReactFlow
              nodes={nodes}
              edges={edges}
              nodeTypes={nodeTypes}
              onNodesChange={onNodesChange}
              onEdgesChange={onEdgesChange}
              onConnect={onConnect}
              onNodeClick={onNodeClick}
              onNodeDoubleClick={onNodeDoubleClick}
              onNodeDragStop={onNodeDragStop}
              fitView
              minZoom={0.35}
              maxZoom={1.8}
              nodesDraggable
              defaultEdgeOptions={{
                type: 'smoothstep',
                markerEnd: { type: MarkerType.ArrowClosed }
              }}
            >
              <Background variant={BackgroundVariant.Dots} gap={24} size={1.2} color="rgba(210, 224, 230, 0.28)" />
              <MiniMap pannable zoomable nodeStrokeWidth={3} className="mini-map" />
              <Controls className="flow-controls" />
            </ReactFlow>
          </div>
          <RuntimeStrip runtime={runtime} entity={activeEntity} notice={notice} />
        </section>

        <aside className="right-rail">
          <NodeInspector
            document={document}
            node={selectedNode}
            view={view}
            runtime={runtime}
            updateDocument={updateDocument}
          />
          <CompilePanel compile={compile} />
        </aside>
      </section>
    </main>
  );
}

function DocumentList({ document, view, setView, setBreadcrumbs }) {
  const rows = [
    ...document.graphs.map((item) => ({ mode: 'graph', id: item.id, title: item.title, domain: item.domain })),
    ...document.stateMachines.map((item) => ({ mode: 'fsm', id: item.id, title: item.title, domain: 'FSM' })),
    ...document.behaviorTrees.map((item) => ({ mode: 'bt', id: item.id, title: item.title, domain: 'BT' }))
  ];

  return (
    <section className="rail-section">
      <h2>文档</h2>
      <div className="doc-list">
        {rows.map((row) => (
          <button
            key={`${row.mode}:${row.id}`}
            type="button"
            className={view.mode === row.mode && view.id === row.id ? 'selected' : ''}
            onClick={() => {
              setBreadcrumbs([]);
              setView(row);
            }}
          >
            <strong>{row.title}</strong>
            <span>{row.domain}</span>
          </button>
        ))}
      </div>
    </section>
  );
}

function PalettePanel({ mode, palette, onAdd }) {
  const items = mode === 'graph'
    ? palette.graphOps
    : mode === 'fsm'
      ? palette.fsmNodeKinds
      : palette.behaviorNodeKinds;

  return (
    <section className="rail-section">
      <h2>节点</h2>
      <div className="palette-grid">
        {items.map((item) => (
          <button key={item} type="button" onClick={() => onAdd(item)}>
            {item}
          </button>
        ))}
      </div>
    </section>
  );
}

function EntityList({ entities, selectedId, onSelect }) {
  return (
    <section className="rail-section grow">
      <h2>实体</h2>
      <div className="entity-list">
        {entities.map((entity) => (
          <button
            key={entity.id}
            type="button"
            className={entity.id === selectedId ? 'selected' : ''}
            onClick={() => onSelect(entity.id)}
          >
            <strong>{entity.label}</strong>
            <span>{entity.domain}</span>
          </button>
        ))}
      </div>
    </section>
  );
}

function Breadcrumbs({ trail, activeTitle, onClick }) {
  return (
    <nav className="breadcrumbs">
      {trail.map((item, index) => (
        <button key={`${item.mode}:${item.id}:${index}`} type="button" onClick={() => onClick(index)}>
          {MODE_LABELS[item.mode]} / {item.title}
        </button>
      ))}
      <strong>{activeTitle}</strong>
    </nav>
  );
}

function RuntimeStrip({ runtime, entity, notice }) {
  return (
    <footer className="runtime-strip">
      <div>
        <span>选中实体</span>
        <strong>{entity?.label ?? runtime.selectedEntityId}</strong>
      </div>
      <div>
        <span>Graph</span>
        <strong>{runtime.currentGraphNodeId || '-'}</strong>
      </div>
      <div>
        <span>FSM</span>
        <strong>{runtime.currentStateNodeId || '-'}</strong>
      </div>
      <div>
        <span>BT</span>
        <strong>{runtime.currentBehaviorNodeId || '-'}</strong>
      </div>
      <p>{notice || `${runtime.source} / ${runtime.entities.length} entities / ${runtime.aggregates.map((row) => `${row.domain}:${row.count}`).join(' ')}`}</p>
    </footer>
  );
}

function NodeInspector({ document, node, view, runtime, updateDocument }) {
  const isRuntimeNode = node && isNodeActiveForRuntime(node.id, view, runtime);

  if (!node) {
    return (
      <section className="inspector">
        <h2>检查器</h2>
        <p>未选择节点</p>
      </section>
    );
  }

  const updateNode = (patch) => {
    updateDocument((next) => {
      const target = resolveActiveDocument(next, view);
      const item = target?.nodes?.find((candidate) => candidate.id === node.id);
      if (item) {
        Object.assign(item, patch);
      }
    });
  };

  return (
    <section className="inspector">
      <header>
        <h2>检查器</h2>
        <span className={isRuntimeNode ? 'live' : ''}>{isRuntimeNode ? 'Live' : 'Draft'}</span>
      </header>
      <label>
        名称
        <input value={node.label} onChange={(event) => updateNode({ label: event.target.value })} />
      </label>
      <label>
        类型
        <input value={node.kind} onChange={(event) => updateNode({ kind: event.target.value })} />
      </label>
      {view.mode === 'graph' ? (
        <>
          <label>
            Op
            <select value={node.op || 'ConstInt'} onChange={(event) => updateNode({ op: event.target.value })}>
              {(document?.palette?.graphOps ?? ['ConstInt', 'AddInt', 'CompareLtInt', 'CompareEqInt']).map((op) => (
                <option key={op} value={op}>{op}</option>
              ))}
            </select>
          </label>
          <label>
            Int
            <input
              type="number"
              value={node.intValue ?? 0}
              onChange={(event) => updateNode({ intValue: Number(event.target.value) })}
            />
          </label>
        </>
      ) : (
        <label>
          实现图
          <select
            value={node.implementationGraphId ?? ''}
            onChange={(event) => updateNode({ implementationGraphId: event.target.value })}
          >
            <option value="">无绑定</option>
            {document.graphs.map((graph) => (
              <option key={graph.id} value={graph.id}>{graph.title}</option>
            ))}
          </select>
        </label>
      )}
      <div className="node-meta">
        <span>{node.id}</span>
        <span>{Math.round(node.x)}, {Math.round(node.y)}</span>
      </div>
    </section>
  );
}

function CompilePanel({ compile }) {
  const diagnostics = compile.diagnostics ?? [];
  return (
    <section className="compile-panel">
      <header>
        <h2>编译</h2>
        <span className={compile.success ? 'compile-ok' : 'compile-bad'}>{compile.summary}</span>
      </header>
      <div className="diagnostics">
        {diagnostics.length === 0 ? (
          <p>No diagnostics.</p>
        ) : diagnostics.map((item, index) => (
          <article key={`${item.code}:${item.documentId}:${item.nodeId}:${index}`}>
            <strong>{item.code}</strong>
            <span>{item.documentId}{item.nodeId ? ` / ${item.nodeId}` : ''}</span>
            <p>{item.message}</p>
          </article>
        ))}
      </div>
    </section>
  );
}

function WorkbenchNode({ data }) {
  return (
    <div
      className={[
        'workbench-node',
        data.mode,
        data.active ? 'runtime-active' : '',
        data.implementationGraphId ? 'has-impl' : ''
      ].filter(Boolean).join(' ')}
    >
      <Handle type="target" position={Position.Left} />
      <div className="node-kind">{data.kind || data.mode}</div>
      <strong>{data.label}</strong>
      <span>{data.detail}</span>
      {data.implementationGraphId ? <i>impl</i> : null}
      <Handle type="source" position={Position.Right} />
    </div>
  );
}

const nodeTypes = { workbenchNode: WorkbenchNode };

function useGraphWorkbenchDataPlane() {
  const clientRef = useRef(null);
  const dirtyRef = useRef(false);
  const [snapshot, setSnapshot] = useState(null);
  const [draft, setDraft] = useState(null);
  const [dirty, setDirty] = useState(false);
  const [connection, setConnection] = useState(EMPTY_CONNECTION);

  useEffect(() => {
    let active = true;
    let client = null;
    let retryTimeout = null;

    const retry = () => {
      if (!active || retryTimeout != null) {
        return;
      }

      setConnection((current) => ({
        ...current,
        phase: 'waiting-for-host',
        transport: 'none',
        error: ''
      }));
      retryTimeout = globalThis.setTimeout?.(() => {
        retryTimeout = null;
        connect();
      }, 150) ?? null;
    };

    const connect = () => {
      let resolved;
      try {
        resolved = ensureLudotsDataPlaneTransport();
      } catch {
        retry();
        return;
      }

      client = createLudotsDataPlaneClient({
        transport: resolved.transport,
        hostBacked: resolved.hostBacked,
        diagnostics: (diagnostic) => {
          if (!active || diagnostic.level !== 'error') {
            return;
          }

          setConnection((current) => ({ ...current, error: diagnostic.message, lastPacket: diagnostic.type }));
        }
      });
      clientRef.current = client;
      setConnection((current) => ({
        ...current,
        phase: 'connecting',
        transport: resolved.transport?.name ?? 'unknown',
        error: ''
      }));

      client
        .handshake({ app: 'graph-workbench-showcase' })
        .then((handshake) => {
          if (!active) {
            return null;
          }

          setConnection((current) => ({
            ...current,
            phase: 'connected',
            sessionId: handshake.sessionId ?? handshake.payload?.sessionId ?? current.sessionId,
            transport: handshake.payload?.transportName ?? resolved.transport?.name ?? current.transport
          }));
          return client.subscribe(GRAPH_WORKBENCH_TOPIC, (event) => {
            if (!active) {
              return;
            }

            setSnapshot(event.payload);
            if (!dirtyRef.current) {
              setDraft(event.payload.document);
            }
            setConnection((current) => ({
              ...current,
              phase: 'streaming',
              lastPacket: event.kind,
              topic: event.topic ?? current.topic,
              sessionId: event.sessionId ?? current.sessionId
            }));
          });
        })
        .catch((error) => {
          if (!active) {
            return;
          }

          setConnection((current) => ({
            ...current,
            phase: 'stream-error',
            error: error instanceof Error ? error.message : String(error)
          }));
        });
    };

    connect();
    return () => {
      active = false;
      if (retryTimeout != null) {
        globalThis.clearTimeout?.(retryTimeout);
      }

      client?.close();
      clientRef.current = null;
    };
  }, []);

  useEffect(() => {
    if (!dirty || !draft) {
      return undefined;
    }

    const timeout = globalThis.setTimeout?.(() => {
      const client = clientRef.current;
      if (!client) {
        return;
      }

      client.command('editDocument', { document: draft }).catch((error) => {
        setConnection((current) => ({
          ...current,
          lastCommand: 'editDocument:error',
          error: error instanceof Error ? error.message : String(error)
        }));
      });
    }, 450);

    return () => {
      if (timeout != null) {
        globalThis.clearTimeout?.(timeout);
      }
    };
  }, [dirty, draft]);

  const command = useCallback(async (name, payload = {}) => {
    const client = clientRef.current;
    if (!client) {
      throw new Error('Graph Workbench DataPlane is not connected.');
    }

    setConnection((current) => ({ ...current, lastCommand: `${name}:pending`, error: '' }));
    const response = await client.command(name, payload);
    setConnection((current) => ({
      ...current,
      lastCommand: `${name}:ack`,
      commandAcks: current.commandAcks + 1,
      error: response.payload?.message ?? ''
    }));
    if (name === 'compileDocument') {
      dirtyRef.current = false;
      setDirty(false);
    }
    return response;
  }, []);

  const setDraftDocument = useCallback((updater) => {
    dirtyRef.current = true;
    setDirty(true);
    setDraft((current) => {
      const base = current ?? snapshot?.document;
      return typeof updater === 'function' ? updater(base) : updater;
    });
  }, [snapshot]);

  return {
    snapshot,
    draft,
    setDraftDocument,
    connection,
    command,
    dirty
  };
}

function buildFlow(document, view, runtime, selectedNodeId) {
  if (!document) {
    return { nodes: [], edges: [] };
  }

  const nodes = (document.nodes ?? []).map((node) => {
    const active = isNodeActiveForRuntime(node.id, view, runtime);
    return {
      id: node.id,
      type: 'workbenchNode',
      position: { x: node.x ?? 0, y: node.y ?? 0 },
      data: {
        label: node.label || node.id,
        kind: node.kind || node.op || view.mode,
        mode: view.mode,
        detail: view.mode === 'graph' ? (node.op || 'GraphOp') : (node.implementationGraphId || 'no implementation'),
        implementationGraphId: node.implementationGraphId || '',
        active,
        selected: selectedNodeId === node.id
      },
      className: selectedNodeId === node.id ? 'selected-flow-node' : '',
      sourcePosition: Position.Right,
      targetPosition: Position.Left
    };
  });

  const edges = (document.edges ?? []).map((edge) => {
    const active = isEdgeActiveForRuntime(edge, view, runtime);
    return {
      id: edge.id,
      source: edge.source,
      target: edge.target,
      label: edge.label,
      type: 'smoothstep',
      animated: active,
      className: active ? 'runtime-edge' : '',
      style: {
        strokeWidth: active ? 3.4 : 2,
        stroke: active ? '#ffe08a' : '#78909c'
      },
      markerEnd: { type: MarkerType.ArrowClosed, color: active ? '#ffe08a' : '#78909c' }
    };
  });

  return { nodes, edges };
}

function isNodeActiveForRuntime(nodeId, view, runtime) {
  if (view.mode === 'graph') {
    return runtime.currentGraphId === view.id && runtime.currentGraphNodeId === nodeId;
  }

  if (view.mode === 'fsm') {
    return runtime.currentStateMachineId === view.id && runtime.currentStateNodeId === nodeId;
  }

  return runtime.currentBehaviorTreeId === view.id && runtime.currentBehaviorNodeId === nodeId;
}

function isEdgeActiveForRuntime(edge, view, runtime) {
  if (view.mode === 'graph') {
    return edge.source === runtime.currentGraphNodeId || edge.target === runtime.currentGraphNodeId;
  }

  if (view.mode === 'fsm') {
    return edge.source === runtime.currentStateNodeId || edge.target === runtime.currentStateNodeId;
  }

  return edge.source === runtime.currentBehaviorNodeId || edge.target === runtime.currentBehaviorNodeId;
}

function resolveActiveDocument(document, view) {
  if (view.mode === 'graph') {
    return findGraph(document, view.id);
  }

  if (view.mode === 'fsm') {
    return document.stateMachines.find((item) => item.id === view.id) ?? null;
  }

  return document.behaviorTrees.find((item) => item.id === view.id) ?? null;
}

function resolveModeDefaultView(document, mode) {
  if (mode === 'graph') {
    const item = findGraph(document, document.activeGraphId) ?? document.graphs[0];
    return { mode, id: item?.id ?? '', title: item?.title ?? 'Graph' };
  }

  if (mode === 'fsm') {
    const item = document.stateMachines.find((candidate) => candidate.id === document.activeStateMachineId) ?? document.stateMachines[0];
    return { mode, id: item?.id ?? '', title: item?.title ?? 'FSM' };
  }

  const item = document.behaviorTrees.find((candidate) => candidate.id === document.activeBehaviorTreeId) ?? document.behaviorTrees[0];
  return { mode, id: item?.id ?? '', title: item?.title ?? 'BT' };
}

function modeToActivePayload(mode, id) {
  if (mode === 'graph') {
    return { graphId: id };
  }

  if (mode === 'fsm') {
    return { stateMachineId: id };
  }

  return { behaviorTreeId: id };
}

function findGraph(document, graphId) {
  return document.graphs.find((item) => item.id === graphId) ?? null;
}

function cloneDocument(document) {
  return JSON.parse(JSON.stringify(document));
}

function formatError(error) {
  return error instanceof Error ? error.message : String(error);
}

createRoot(document.getElementById('root')).render(<App />);
