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

const VALUE_OUTPUT_TYPES = new Map([
  ['ConstBool', 'Bool'],
  ['ConstInt', 'Int'],
  ['ConstFloat', 'Float'],
  ['LoadCaster', 'Entity'],
  ['LoadExplicitTarget', 'Entity'],
  ['LoadViewer', 'Entity'],
  ['LoadEventPayloadInt', 'Int'],
  ['LoadEventPayloadFloat', 'Float'],
  ['CompareEqEntity', 'Bool'],
  ['RelationshipHasLink', 'Bool'],
  ['ControlDomainResolve', 'Entity'],
  ['ControlDomainControls', 'Bool'],
  ['KnowledgeHasProjection', 'Bool'],
  ['LoadContextSource', 'Entity'],
  ['LoadContextTarget', 'Entity'],
  ['LoadContextTargetContext', 'Entity'],
  ['RandomFloat01', 'Float'],
  ['LoadAttribute', 'Float'],
  ['LoadSelfAttribute', 'Float'],
  ['AddFloat', 'Float'],
  ['MulFloat', 'Float'],
  ['SubFloat', 'Float'],
  ['DivFloat', 'Float'],
  ['MinFloat', 'Float'],
  ['MaxFloat', 'Float'],
  ['ClampFloat', 'Float'],
  ['AbsFloat', 'Float'],
  ['NegFloat', 'Float'],
  ['AddInt', 'Int'],
  ['CompareGtFloat', 'Bool'],
  ['CompareLtInt', 'Bool'],
  ['CompareEqInt', 'Bool'],
  ['HasTag', 'Bool'],
  ['SelectEntity', 'Entity'],
  ['AggCount', 'Int'],
  ['AggMinByDistance', 'Entity'],
  ['TargetListGet', 'Entity'],
  ['ReadBlackboardFloat', 'Float'],
  ['ReadBlackboardInt', 'Int'],
  ['ReadBlackboardEntity', 'Entity'],
  ['LoadConfigFloat', 'Float'],
  ['LoadConfigInt', 'Int'],
  ['LoadConfigEffectId', 'Int'],
  ['RelationshipGetMetric', 'Int'],
  ['RelationshipHasFlag', 'Bool'],
  ['RelationshipAggSumMetric', 'Int'],
  ['RelationshipAggMaxMetric', 'Int'],
  ['RelationshipAggAverageMetric', 'Int'],
  ['RelationshipAggMinMetric', 'Int'],
  ['RelationshipAggMaxEntityByMetric', 'Entity'],
  ['RelationshipAggMinEntityByMetric', 'Entity'],
  ['AggSumAttribute', 'Float'],
  ['AggAverageAttribute', 'Float'],
  ['AggMaxAttribute', 'Float'],
  ['AggMinAttribute', 'Float'],
  ['AggMaxEntityByAttribute', 'Entity'],
  ['AggMinEntityByAttribute', 'Entity'],
  ['LoadTargetPosX', 'Int'],
  ['LoadTargetPosY', 'Int'],
  ['ClampTargetToRange', 'Bool'],
  ['IsPointInCircle', 'Bool'],
  ['SnapToNearestInCollection', 'Entity'],
  ['SnapToNearestGraphEdge', 'Bool']
]);

const GRAPH_PARAMETER_FIELDS = [
  ['intValue', 'Int', 'number'],
  ['floatValue', 'Float', 'number'],
  ['boolValue', 'Bool', 'checkbox'],
  ['tag', 'Tag', 'text'],
  ['attribute', 'Attribute', 'text'],
  ['template', 'Template', 'text'],
  ['collectionKey', 'Collection', 'text'],
  ['effectTemplate', 'Effect', 'text'],
  ['blackboardKey', 'Blackboard', 'text'],
  ['configKey', 'Config', 'text'],
  ['validOutput', 'Valid out', 'text'],
  ['droppedOutput', 'Dropped out', 'text'],
  ['queryCapacityPolicy', 'Capacity', 'text'],
  ['radiusCm', 'Radius', 'number'],
  ['rangeCm', 'Range', 'number'],
  ['directionDeg', 'Direction', 'number'],
  ['halfAngleDeg', 'Half angle', 'number'],
  ['lengthCm', 'Length', 'number'],
  ['halfWidthCm', 'Half width', 'number'],
  ['halfHeightCm', 'Half height', 'number'],
  ['rotationDeg', 'Rotation', 'number'],
  ['hexRadius', 'Hex radius', 'number'],
  ['layerMask', 'Layer mask', 'number'],
  ['relationshipMode', 'Relation mode', 'text'],
  ['limit', 'Limit', 'number'],
  ['teamId', 'Team', 'number'],
  ['sort', 'Sort', 'text'],
  ['relationshipType', 'Relation type', 'text'],
  ['metric', 'Metric', 'text'],
  ['flag', 'Flag', 'text'],
  ['reason', 'Reason', 'text'],
  ['payloadPreset', 'Payload', 'text'],
  ['builtinHandler', 'Builtin', 'text'],
  ['descending', 'Descending', 'checkbox'],
  ['slot', 'Slot', 'number']
];

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

const EMPTY_RUNTIME = Object.freeze({
  source: '',
  currentGraphId: '',
  currentGraphNodeId: '',
  currentStateMachineId: '',
  currentStateNodeId: '',
  currentBehaviorTreeId: '',
  currentBehaviorNodeId: ''
});

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
  const availableModes = useMemo(() => getAvailableModes(document), [document]);
  const [view, setView] = useState(() => ({
    ...resolveInitialView(document, runtime)
  }));
  const [isFollowingRuntime, setIsFollowingRuntime] = useState(true);
  const lastRuntimeSourceRef = useRef(runtime?.source ?? '');
  const [breadcrumbs, setBreadcrumbs] = useState([]);
  const [selectedNodeId, setSelectedNodeId] = useState('');
  const [notice, setNotice] = useState('');
  const [isCollapsed, setIsCollapsed] = useState(false);

  useEffect(() => {
    if (!isViewAvailable(document, view)) {
      setView(resolveInitialView(document, runtime));
      setSelectedNodeId('');
      setBreadcrumbs([]);
    }
  }, [document, runtime, view.id, view.mode]);

  useEffect(() => {
    const runtimeSource = runtime?.source ?? '';
    const sourceChanged = lastRuntimeSourceRef.current !== runtimeSource;
    lastRuntimeSourceRef.current = runtimeSource;
    if (sourceChanged && runtimeSource.startsWith('live-3d:')) {
      setIsFollowingRuntime(true);
    }
  }, [runtime?.source]);

  useEffect(() => {
    if (!isFollowingRuntime) {
      return;
    }

    const next = resolveInitialView(document, runtime);
    if (view.mode !== next.mode || view.id !== next.id) {
      setView(next);
      setSelectedNodeId('');
      setBreadcrumbs([]);
    }
  }, [
    document,
    isFollowingRuntime,
    runtime?.source,
    runtime?.currentGraphId,
    runtime?.currentStateMachineId,
    runtime?.currentStateNodeId,
    runtime?.currentBehaviorTreeId,
    runtime?.currentBehaviorNodeId,
    runtime?.entities,
    runtime?.selectedEntityId,
    view.id,
    view.mode
  ]);

  const activeDocument = useMemo(() => resolveActiveDocument(document, view), [document, view]);
  const selectedNode = useMemo(
    () => activeDocument?.nodes?.find((node) => node.id === selectedNodeId) ?? null,
    [activeDocument, selectedNodeId]
  );
  const activeEntity = useMemo(
    () => runtime.entities.find((entity) => entity.id === runtime.selectedEntityId) ?? runtime.entities[0],
    [runtime]
  );
  const runtimeActivity = useMemo(
    () => ({
      source: runtime?.source ?? '',
      currentGraphId: runtime?.currentGraphId ?? '',
      currentGraphNodeId: runtime?.currentGraphNodeId ?? '',
      currentStateMachineId: runtime?.currentStateMachineId ?? '',
      currentStateNodeId: runtime?.currentStateNodeId ?? '',
      currentBehaviorTreeId: runtime?.currentBehaviorTreeId ?? '',
      currentBehaviorNodeId: runtime?.currentBehaviorNodeId ?? ''
    }),
    [
      runtime?.source,
      runtime?.currentGraphId,
      runtime?.currentGraphNodeId,
      runtime?.currentStateMachineId,
      runtime?.currentStateNodeId,
      runtime?.currentBehaviorTreeId,
      runtime?.currentBehaviorNodeId
    ]
  );

  const openImplementationGraph = useCallback(
    (nodeId, implementationGraphId) => {
      const sourceNode = activeDocument?.nodes?.find((node) => node.id === nodeId);
      const targetGraphId = implementationGraphId || sourceNode?.implementationGraphId || '';
      if (!targetGraphId) {
        setSelectedNodeId(nodeId);
        setNotice('该节点没有实现图绑定。');
        return;
      }

      const graph = findGraph(document, targetGraphId);
      if (!graph) {
        setSelectedNodeId(nodeId);
        setNotice(`找不到实现图 ${targetGraphId}。`);
        return;
      }

      setBreadcrumbs((current) => [
        ...current,
        { mode: view.mode, id: view.id, title: activeDocument?.title ?? MODE_LABELS[view.mode], selectedNodeId: nodeId }
      ]);
      setIsFollowingRuntime(false);
      setView({ mode: 'graph', id: graph.id, title: graph.title });
      setSelectedNodeId('');
      setNotice('');
      void command('setActiveDocument', { graphId: graph.id }).catch((error) => {
        setNotice(formatError(error));
      });
    },
    [activeDocument, command, document, view]
  );

  const structuralFlow = useMemo(
    () => buildFlow(activeDocument, view, EMPTY_RUNTIME, '', openImplementationGraph),
    [activeDocument, openImplementationGraph, view.id, view.mode]
  );
  const [nodes, setNodes, onNodesChange] = useNodesState(structuralFlow.nodes);
  const [edges, setEdges, onEdgesChange] = useEdgesState(structuralFlow.edges);
  const isDraggingNodeRef = useRef(false);

  useEffect(() => {
    setNodes((current) => {
      const nextNodes = isDraggingNodeRef.current
        ? preserveCurrentNodePositions(structuralFlow.nodes, current)
        : structuralFlow.nodes;
      return applyRuntimeNodeState(nextNodes, view, runtimeActivity, selectedNodeId);
    });
    setEdges(applyRuntimeEdgeState(structuralFlow.edges, view, runtimeActivity));
  }, [runtimeActivity, selectedNodeId, setEdges, setNodes, structuralFlow, view.id, view.mode]);

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
      setIsFollowingRuntime(false);
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

  const onNodeClick = useCallback((_event, node) => {
    setSelectedNodeId(node.id);
    setNotice('');
  }, []);

  const onNodeDoubleClick = useCallback(
    (event, node) => {
      event?.stopPropagation?.();
      openImplementationGraph(node.id, node.data?.implementationGraphId ?? '');
    },
    [openImplementationGraph]
  );

  const onNodeDragStart = useCallback(() => {
    isDraggingNodeRef.current = true;
  }, []);

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
      const clearDragging = () => {
        isDraggingNodeRef.current = false;
      };
      if (typeof globalThis.requestAnimationFrame === 'function') {
        globalThis.requestAnimationFrame(clearDragging);
      } else {
        clearDragging();
      }
    },
    [updateDocument, view]
  );

  const onConnect = useCallback(
    (connection) => {
      const sourceHandle = connection.sourceHandle || '';
      const targetHandle = connection.targetHandle || '';
      if (!connection.source || !connection.target) {
        setNotice('连线缺少源节点或目标节点。');
        return;
      }

      const isInputConnection = targetHandle.startsWith('in:');
      const isExecConnection = sourceHandle === 'exec:next' && targetHandle === 'exec:in';
      if (!isInputConnection && !isExecConnection) {
        setNotice('只能把值输出接到参数输入，或把 exec:next 接到 exec:in。');
        return;
      }

      const sourceValue = isInputConnection
        ? resolveSourceValue(activeDocument, connection.source, sourceHandle)
        : connection.source;
      if (isInputConnection && !sourceValue) {
        setNotice(`源端口 ${sourceHandle || 'out:value'} 没有可传递的 Graph 值。`);
        return;
      }

      const inputIndex = isInputConnection ? parseInputPort(targetHandle) : -1;
      if (isInputConnection && inputIndex < 0) {
        setNotice(`目标端口 ${targetHandle} 不是有效参数输入。`);
        return;
      }

      const edgeId = `${connection.source}-${connection.target}-${Date.now().toString(16)}`;
      setEdges((current) => addEdge({
        ...connection,
        id: edgeId,
        label: isInputConnection ? `in[${inputIndex}]` : 'next',
        type: 'smoothstep',
        animated: true,
        markerEnd: { type: MarkerType.ArrowClosed }
      }, current));
      updateDocument((next) => {
        const target = resolveActiveDocument(next, view);
        if (!target) {
          return;
        }

        if (isInputConnection) {
          const targetNode = target.nodes?.find((candidate) => candidate.id === connection.target);
          if (targetNode) {
            targetNode.inputs ??= [];
            while (targetNode.inputs.length <= inputIndex) {
              targetNode.inputs.push('');
            }
            targetNode.inputs[inputIndex] = sourceValue;
          }

          target.edges = (target.edges ?? []).filter((edge) =>
            !(edge.target === connection.target && edge.targetPort === targetHandle));
        } else {
          target.edges = (target.edges ?? []).filter((edge) =>
            !(edge.source === connection.source && edge.sourcePort === 'exec:next' && edge.role === 'next'));
        }

        target.edges?.push({
          id: edgeId,
          source: connection.source,
          target: connection.target,
          label: isInputConnection ? `in[${inputIndex}]` : 'next',
          role: isInputConnection ? 'input' : 'next',
          sourcePort: sourceHandle || 'out:value',
          targetPort: targetHandle
        });
      });
      setNotice('');
    },
    [activeDocument, setEdges, updateDocument, view]
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
          template: '',
          collectionKey: '',
          blackboardKey: '',
          configKey: '',
          validOutput: '',
          droppedOutput: '',
          queryCapacityPolicy: '',
          radiusCm: 0,
          rangeCm: 0,
          directionDeg: 0,
          halfAngleDeg: 0,
          lengthCm: 0,
          halfWidthCm: 0,
          halfHeightCm: 0,
          rotationDeg: 0,
          hexRadius: 0,
          layerMask: 0,
          relationshipMode: '',
          limit: 0,
          teamId: 0,
          sort: '',
          relationshipType: '',
          metric: '',
          flag: '',
          reason: '',
          payloadPreset: '',
          builtinHandler: '',
          descending: false,
          slot: 0,
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

      setIsFollowingRuntime(false);
      setBreadcrumbs((current) => current.slice(0, index));
      setView({ mode: target.mode, id: target.id, title: target.title });
      setSelectedNodeId(target.selectedNodeId);
      setNotice('');
    },
    [breadcrumbs]
  );

  if (isCollapsed) {
    return (
      <CollapsedWorkbench
        runtime={runtime}
        compile={compile}
        connection={connection}
        dirty={dirty}
        onExpand={() => setIsCollapsed(false)}
      />
    );
  }

  return (
    <main className="workbench-shell">
      <header className="topbar">
        <div className="brand-block">
          <h1>Graph Workbench</h1>
          <span>{connection.phase} / rev {runtime.appliedRevision}</span>
        </div>
        <div className="mode-tabs" role="tablist">
          {availableModes.map(([mode, label]) => (
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
          <button className="dock-toggle" type="button" onClick={() => setIsCollapsed(true)}>
            收起
          </button>
          <button type="button" onClick={compileNow}>Compile</button>
        </div>
      </header>

      <section className="content-grid">
        <aside className="left-rail">
          <DocumentList
            document={document}
            view={view}
            onSelectView={(next) => {
              setIsFollowingRuntime(false);
              setBreadcrumbs([]);
              setView(next);
            }}
          />
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
              onNodeDragStart={onNodeDragStart}
              onNodeDragStop={onNodeDragStop}
              fitView
              zoomOnDoubleClick={false}
              nodeClickDistance={6}
              nodeDragThreshold={4}
              connectOnClick={false}
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
            palette={snapshot.palette}
            updateDocument={updateDocument}
          />
          <CompilePanel compile={compile} />
        </aside>
      </section>
    </main>
  );
}

function CollapsedWorkbench({ runtime, compile, connection, dirty, onExpand }) {
  const activeEntity = runtime.entities.find((entity) => entity.id === runtime.selectedEntityId) ?? runtime.entities[0];
  const compileState = compile.success ? 'Compiled' : 'Draft';

  return (
    <main className="workbench-shell collapsed">
      <button className="expand-tab" type="button" onClick={onExpand}>
        <strong>Graph Workbench</strong>
        <span>{compileState}</span>
        <span>{dirty ? 'editing' : connection.phase}</span>
        <span>{activeEntity?.label ?? 'No entity'}</span>
      </button>
    </main>
  );
}

function DocumentList({ document, view, onSelectView }) {
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
            onClick={() => onSelectView(row)}
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

function NodeInspector({ document, node, view, runtime, palette, updateDocument }) {
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
              {(palette?.graphOps ?? ['ConstInt', 'AddInt', 'CompareLtInt', 'CompareEqInt']).map((op) => (
                <option key={op} value={op}>{op}</option>
              ))}
            </select>
          </label>
          <div className="port-summary">
            <strong>Inputs</strong>
            {(node.inputs ?? []).length === 0 ? <span>无参数输入</span> : (node.inputs ?? []).map((input, index) => (
              <span key={`${node.id}:input:${index}`}>in:{index} {'<-'} {input || '-'}</span>
            ))}
            <strong>Outputs</strong>
            {getGraphNodeOutputPorts(node).map((port) => (
              <span key={`${node.id}:${port.id}`}>{port.id} / {port.type}</span>
            ))}
          </div>
          <div className="field-grid">
            {GRAPH_PARAMETER_FIELDS.map(([field, label, type]) => (
              <ParameterField
                key={field}
                field={field}
                label={label}
                type={type}
                node={node}
                updateNode={updateNode}
              />
            ))}
          </div>
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

function ParameterField({ field, label, type, node, updateNode }) {
  const value = node[field];
  if (!shouldShowParameter(field, value, node.op)) {
    return null;
  }

  if (type === 'checkbox') {
    return (
      <label className="inline-check">
        <input
          type="checkbox"
          checked={Boolean(value)}
          onChange={(event) => updateNode({ [field]: event.target.checked })}
        />
        {label}
      </label>
    );
  }

  return (
    <label>
      {label}
      <input
        type={type}
        value={value ?? (type === 'number' ? 0 : '')}
        onChange={(event) => updateNode({ [field]: type === 'number' ? Number(event.target.value) : event.target.value })}
      />
    </label>
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
  if (data.virtualOutput) {
    return (
      <div className="workbench-node graph-output-node">
        <Handle type="target" id="in:source" position={Position.Left} className="value-handle" />
        <div className="node-kind">{data.kind}</div>
        <strong>{data.label}</strong>
        <span>{data.detail}</span>
      </div>
    );
  }

  return (
    <div
      className={[
        'workbench-node',
        data.mode,
        data.active ? 'runtime-active' : '',
        data.implementationGraphId ? 'has-impl' : ''
      ].filter(Boolean).join(' ')}
      data-implementation-graph-id={data.implementationGraphId || undefined}
    >
      <div className="node-shell">
        <div className="port-column input-ports">
          {data.execInput ? (
            <div className="port-row exec-port">
              <Handle type="target" id="exec:in" position={Position.Left} className="exec-handle" />
              <span>exec</span>
            </div>
          ) : null}
          {(data.inputPorts ?? []).map((port) => (
            <div className="port-row input-port" key={port.id}>
              <Handle type="target" id={port.id} position={Position.Left} className="value-handle" />
              <span>{port.label}</span>
            </div>
          ))}
        </div>
        <div className="node-body">
          <div className="node-kind">{data.kind || data.mode}</div>
          <strong>{data.label}</strong>
          <span>{data.detail}</span>
          {(data.params ?? []).length > 0 ? (
            <div className="param-chips">
              {data.params.map((param) => <em key={param}>{param}</em>)}
            </div>
          ) : null}
        </div>
        <div className="port-column output-ports">
          {(data.outputPorts ?? []).map((port) => (
            <div className={port.exec ? 'port-row exec-port' : 'port-row output-port'} key={port.id}>
              <span>{port.label}</span>
              <Handle
                type="source"
                id={port.id}
                position={Position.Right}
                className={port.exec ? 'exec-handle' : 'value-handle'}
              />
            </div>
          ))}
        </div>
      </div>
      {data.implementationGraphId ? <i>impl</i> : null}
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

            if (!event.payload?.document) {
              setConnection((current) => ({
                ...current,
                phase: 'stream-error',
                lastPacket: event.kind,
                error: 'Graph Workbench snapshot is missing document.'
              }));
              return;
            }

            setSnapshot(event.payload);
            if (!dirtyRef.current) {
              setDraft((current) =>
                shouldAdoptDocumentSnapshot(current, event.payload.document)
                  ? event.payload.document
                  : current);
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

function buildFlow(document, view, runtime, selectedNodeId, onOpenImplementationGraph = null) {
  if (!document) {
    return { nodes: [], edges: [] };
  }

  const graphMode = view.mode === 'graph';
  const sourceLookup = graphMode ? createValueProducerLookup(document.nodes ?? []) : new Map();
  const maxNodeX = Math.max(0, ...(document.nodes ?? []).map((node) => node.x ?? 0));
  const nodes = (document.nodes ?? []).map((node) => {
    const active = isNodeActiveForRuntime(node.id, view, runtime);
    return {
      id: node.id,
      type: 'workbenchNode',
      position: { x: node.x ?? 0, y: node.y ?? 0 },
      data: {
        nodeId: node.id,
        label: node.label || node.id,
        kind: node.kind || node.op || view.mode,
        mode: view.mode,
        detail: view.mode === 'graph' ? (node.op || 'GraphOp') : (node.implementationGraphId || 'no implementation'),
        implementationGraphId: node.implementationGraphId || '',
        onOpenImplementationGraph,
        execInput: true,
        inputPorts: graphMode ? getGraphNodeInputPorts(node) : [],
        outputPorts: graphMode ? getGraphNodeOutputPorts(node) : getHighLevelOutputPorts(),
        params: graphMode ? describeGraphNodeParams(node) : [],
        active,
        selected: selectedNodeId === node.id
      },
      className: selectedNodeId === node.id ? 'selected-flow-node' : '',
      sourcePosition: Position.Right,
      targetPosition: Position.Left
    };
  });

  if (graphMode) {
    (document.outputs ?? []).forEach((output, index) => {
      nodes.push({
        id: outputNodeId(output),
        type: 'workbenchNode',
        position: {
          x: maxNodeX + 340,
          y: 92 + index * 118
        },
        data: {
          label: output.title || output.id,
          kind: 'GraphOutput',
          mode: view.mode,
          detail: `${output.destination || 'Summary'} / ${output.type || 'Value'} / ${output.key || output.collectionKey || output.source}`,
          virtualOutput: true
        },
        selectable: false,
        draggable: false,
        sourcePosition: Position.Right,
        targetPosition: Position.Left
      });
    });
  }

  const edges = (document.edges ?? []).map((edge) => {
    const active = isEdgeActiveForRuntime(edge, view, runtime);
    const isInput = edge.role === 'input' || (edge.targetPort ?? '').startsWith('in:');
    const flowEdge = {
      id: edge.id,
      source: edge.source,
      target: edge.target,
      sourceHandle: edge.sourcePort || (isInput ? 'out:value' : 'exec:next'),
      targetHandle: edge.targetPort || (isInput ? 'in:0' : 'exec:in'),
      label: edge.label,
      type: 'smoothstep',
      markerEnd: { type: MarkerType.ArrowClosed }
    };
    return { ...flowEdge, ...createFlowEdgeVisual(flowEdge, active) };
  });

  if (graphMode) {
    (document.outputs ?? []).forEach((output) => {
      const source = sourceLookup.get(output.source);
      if (!source) {
        return;
      }

      const active = runtime.currentGraphId === view.id && runtime.currentGraphNodeId === source.nodeId;
      const flowEdge = {
        id: `${output.id}:source`,
        source: source.nodeId,
        target: outputNodeId(output),
        sourceHandle: source.sourcePort,
        targetHandle: 'in:source',
        label: output.key || output.id,
        type: 'smoothstep',
        markerEnd: { type: MarkerType.ArrowClosed }
      };
      edges.push({ ...flowEdge, ...createFlowEdgeVisual(flowEdge, active) });
    });
  }

  return { nodes, edges };
}

function preserveCurrentNodePositions(nextNodes, currentNodes) {
  const currentPositions = new Map((currentNodes ?? []).map((node) => [node.id, node.position]));
  return (nextNodes ?? []).map((node) => {
    const position = currentPositions.get(node.id);
    if (!position || (node.position?.x === position.x && node.position?.y === position.y)) {
      return node;
    }

    return { ...node, position: { x: position.x, y: position.y } };
  });
}

function applyRuntimeNodeState(nodes, view, runtime, selectedNodeId) {
  return (nodes ?? []).map((node) => {
    if (node.data?.virtualOutput) {
      return node;
    }

    const active = isNodeActiveForRuntime(node.id, view, runtime);
    const selected = selectedNodeId === node.id;
    const className = selected ? 'selected-flow-node' : '';
    if (node.data?.active === active && node.data?.selected === selected && node.className === className) {
      return node;
    }

    return {
      ...node,
      className,
      data: {
        ...node.data,
        active,
        selected
      }
    };
  });
}

function applyRuntimeEdgeState(edges, view, runtime) {
  return (edges ?? []).map((edge) => {
    const active = isEdgeActiveForRuntime(edge, view, runtime);
    const visual = createFlowEdgeVisual(edge, active);
    if (
      edge.animated === visual.animated &&
      edge.className === visual.className &&
      edge.style?.stroke === visual.style.stroke &&
      edge.style?.strokeWidth === visual.style.strokeWidth
    ) {
      return edge;
    }

    return { ...edge, ...visual };
  });
}

function createFlowEdgeVisual(edge, active) {
  const targetHandle = edge.targetHandle ?? '';
  const currentClass = edge.className ?? '';
  const isOutput = targetHandle === 'in:source' || currentClass.includes('output-edge');
  const isInput = !isOutput && targetHandle.startsWith('in:');
  const baseClass = isOutput ? 'output-edge' : isInput ? 'data-edge' : 'exec-edge';
  const inactiveStroke = isOutput ? '#e9b85d' : isInput ? '#66d6c0' : '#78909c';
  const stroke = active ? '#ffe08a' : inactiveStroke;
  return {
    animated: active,
    className: [active ? 'runtime-edge' : '', baseClass].filter(Boolean).join(' '),
    style: {
      strokeWidth: active ? 3.4 : isOutput ? 2 : isInput ? 1.8 : 2.2,
      stroke
    },
    markerEnd: { type: MarkerType.ArrowClosed, color: stroke }
  };
}

function getGraphNodeInputPorts(node) {
  return (node.inputs ?? []).map((input, index) => ({
    id: `in:${index}`,
    label: `in:${index}`,
    value: input
  }));
}

function getGraphNodeOutputPorts(node) {
  const ports = [];
  const outputType = VALUE_OUTPUT_TYPES.get(node.op);
  if (outputType) {
    ports.push({ id: 'out:value', label: outputType, type: outputType });
  }

  if (node.validOutput) {
    ports.push({ id: 'out:valid', label: 'valid', type: 'Bool' });
  }

  if (node.droppedOutput) {
    ports.push({ id: 'out:dropped', label: 'dropped', type: 'Int' });
  }

  ports.push({ id: 'exec:next', label: 'next', type: 'Exec', exec: true });
  return ports;
}

function getHighLevelOutputPorts() {
  return [{ id: 'exec:next', label: 'next', type: 'Exec', exec: true }];
}

function createValueProducerLookup(nodes) {
  const lookup = new Map();
  (nodes ?? []).forEach((node) => {
    lookup.set(node.id, { nodeId: node.id, sourcePort: 'out:value' });
    if (node.validOutput) {
      lookup.set(node.validOutput, { nodeId: node.id, sourcePort: 'out:valid' });
    }
    if (node.droppedOutput) {
      lookup.set(node.droppedOutput, { nodeId: node.id, sourcePort: 'out:dropped' });
    }
  });
  return lookup;
}

function resolveSourceValue(document, sourceNodeId, sourceHandle) {
  const source = document?.nodes?.find((node) => node.id === sourceNodeId);
  if (!source) {
    return '';
  }

  if (!sourceHandle || sourceHandle === 'out:value') {
    return VALUE_OUTPUT_TYPES.has(source.op) ? source.id : '';
  }

  if (sourceHandle === 'out:valid') {
    return source.validOutput || '';
  }

  if (sourceHandle === 'out:dropped') {
    return source.droppedOutput || '';
  }

  return '';
}

function parseInputPort(port) {
  if (!port?.startsWith('in:')) {
    return -1;
  }

  const parsed = Number(port.slice(3));
  return Number.isInteger(parsed) && parsed >= 0 ? parsed : -1;
}

function describeGraphNodeParams(node) {
  const params = [];
  if (node.op === 'ConstInt') {
    params.push(`int=${node.intValue ?? 0}`);
  }
  if (node.op === 'ConstFloat') {
    params.push(`float=${node.floatValue ?? 0}`);
  }
  if (node.op === 'ConstBool') {
    params.push(`bool=${Boolean(node.boolValue)}`);
  }

  [
    ['attribute', 'attr'],
    ['tag', 'tag'],
    ['template', 'template'],
    ['collectionKey', 'collection'],
    ['effectTemplate', 'effect'],
    ['blackboardKey', 'bb'],
    ['configKey', 'config'],
    ['relationshipType', 'rel'],
    ['metric', 'metric'],
    ['flag', 'flag'],
    ['builtinHandler', 'builtin'],
    ['validOutput', 'valid'],
    ['droppedOutput', 'dropped']
  ].forEach(([field, label]) => {
    if (node[field]) {
      params.push(`${label}=${node[field]}`);
    }
  });

  if (node.limit) params.push(`limit=${node.limit}`);
  if (node.slot) params.push(`slot=${node.slot}`);
  if (node.queryCapacityPolicy) params.push(node.queryCapacityPolicy);
  return params.slice(0, 5);
}

function shouldShowParameter(field, value, op) {
  if (field === 'intValue') {
    return op === 'ConstInt' || op === 'Jump' || op === 'JumpIfFalse' || Number(value) !== 0;
  }

  if (field === 'floatValue') {
    return op === 'ConstFloat' || Number(value) !== 0;
  }

  if (field === 'boolValue') {
    return op === 'ConstBool' || Boolean(value);
  }

  if (typeof value === 'boolean') {
    return Boolean(value);
  }

  if (typeof value === 'number') {
    return Number(value) !== 0;
  }

  return Boolean(value) || isLikelyParameterForOp(field, op);
}

function isLikelyParameterForOp(field, op) {
  const opName = op || '';
  if (field === 'attribute') return opName.includes('Attribute');
  if (field === 'tag') return opName.includes('Tag') || opName === 'SendEvent';
  if (field === 'template') return opName.includes('Template') && !opName.includes('Effect');
  if (field === 'collectionKey') return opName.includes('Collection');
  if (field === 'effectTemplate') return opName.includes('Effect');
  if (field === 'blackboardKey') return opName.includes('Blackboard');
  if (field === 'configKey') return opName.includes('Config');
  if (field === 'validOutput') return opName === 'TargetListGet' || opName === 'SnapToNearestInCollection';
  if (field === 'droppedOutput' || field === 'queryCapacityPolicy') return opName.startsWith('Query');
  if (field === 'relationshipType' || field === 'relationshipMode' || field === 'metric' || field === 'flag') return opName.includes('Relationship');
  if (field === 'payloadPreset') return opName.includes('Dispatch');
  if (field === 'builtinHandler') return opName === 'InvokeBuiltin';
  if (field === 'slot') return opName.includes('EventPayload');
  if (field === 'limit') return opName === 'QueryLimit';
  return false;
}

function outputNodeId(output) {
  return `__graph_output:${output.id}`;
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

function getAvailableModes(document) {
  const modes = [];
  if (document.graphs.length > 0) {
    modes.push(['graph', MODE_LABELS.graph]);
  }

  if (document.stateMachines.length > 0) {
    modes.push(['fsm', MODE_LABELS.fsm]);
  }

  if (document.behaviorTrees.length > 0) {
    modes.push(['bt', MODE_LABELS.bt]);
  }

  return modes;
}

function isViewAvailable(document, view) {
  return Boolean(resolveActiveDocument(document, view));
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

function resolveInitialView(document, runtime) {
  if (runtime?.source === 'live-3d:graph_stress_field') {
    const graph = findGraph(document, runtime.currentGraphId);
    if (graph) {
      return { mode: 'graph', id: graph.id, title: graph.title };
    }
  }

  const behaviorTreeId = resolveRuntimeBehaviorTreeId(document, runtime);
  if (behaviorTreeId) {
    const tree = document.behaviorTrees.find((item) => item.id === behaviorTreeId);
    if (tree) {
      return { mode: 'bt', id: tree.id, title: tree.title };
    }
  }

  const stateMachineId = resolveRuntimeStateMachineId(document, runtime);
  if (stateMachineId) {
    const machine = document.stateMachines.find((item) => item.id === stateMachineId);
    if (machine) {
      return { mode: 'fsm', id: machine.id, title: machine.title };
    }
  }

  if (runtime?.currentGraphId) {
    const graph = findGraph(document, runtime.currentGraphId);
    if (graph) {
      return { mode: 'graph', id: graph.id, title: graph.title };
    }
  }

  return resolveModeDefaultView(document, 'graph');
}

function resolveRuntimeStateMachineId(document, runtime) {
  if (runtime?.currentStateMachineId) {
    return runtime.currentStateMachineId;
  }

  const entity = runtime?.entities?.find((item) => item.id === runtime.selectedEntityId) ?? runtime?.entities?.[0];
  if (entity?.currentStateMachineId) {
    return entity.currentStateMachineId;
  }

  return runtime?.currentStateNodeId && document.stateMachines.length === 1
    ? document.stateMachines[0].id
    : '';
}

function resolveRuntimeBehaviorTreeId(document, runtime) {
  if (runtime?.currentBehaviorTreeId) {
    return runtime.currentBehaviorTreeId;
  }

  const entity = runtime?.entities?.find((item) => item.id === runtime.selectedEntityId) ?? runtime?.entities?.[0];
  if (entity?.currentBehaviorTreeId) {
    return entity.currentBehaviorTreeId;
  }

  return runtime?.currentBehaviorNodeId && document.behaviorTrees.length === 1
    ? document.behaviorTrees[0].id
    : '';
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

function shouldAdoptDocumentSnapshot(current, incoming) {
  if (!incoming) {
    return false;
  }

  if (!current) {
    return true;
  }

  return (
    current.schemaVersion !== incoming.schemaVersion ||
    current.revision !== incoming.revision ||
    current.activeGraphId !== incoming.activeGraphId ||
    current.activeStateMachineId !== incoming.activeStateMachineId ||
    current.activeBehaviorTreeId !== incoming.activeBehaviorTreeId ||
    documentShapeSignature(current) !== documentShapeSignature(incoming)
  );
}

function documentShapeSignature(document) {
  return [
    document.graphs.map((item) => item.id).join(','),
    document.stateMachines.map((item) => item.id).join(','),
    document.behaviorTrees.map((item) => item.id).join(',')
  ].join('|');
}

function formatError(error) {
  return error instanceof Error ? error.message : String(error);
}

createRoot(document.getElementById('root')).render(<App />);
