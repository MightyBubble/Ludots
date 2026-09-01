import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import {
  Background,
  Controls,
  MiniMap,
  ReactFlow,
  addEdge,
  useEdgesState,
  useNodesState,
  type Connection,
  type Edge,
  type Node,
  type OnConnect,
  type ReactFlowInstance,
  SelectionMode,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import { TopologyNodeView, type TopologyNodeData } from './ai-topology-editor/TopologyNode';
import { computeTopologyTreeLayout } from './ai-topology-editor/topologyLayout';

type TopologyKind = 'behavior-trees' | 'hfsm';

type CatalogSource = {
  id: string;
  name: string;
  kind: string;
  behaviorTrees: { path: string; exists: boolean; items: Array<{ id: string }> };
  hfsm: { path: string; exists: boolean; items: Array<{ id: string }> };
};

type ActionLibEntry = { name: string; host: string; graph: string };

type BtNode = {
  id: string;
  kind: 'Sequence' | 'Selector' | 'Condition' | 'Action';
  children?: string[];
  leaf?: string;
  action?: string;
};

type BtTree = {
  id: string;
  root: string;
  nodes: BtNode[];
};

type HfsmState = {
  id: string;
  kind: 'Leaf' | 'Compound';
  children?: string[];
  defaultChild?: string;
  onEnter?: string;
  onTick?: string;
  onExit?: string;
};

type HfsmTransition = {
  from: string;
  to: string;
  predicate: string;
  condition?: string;
  priority?: number;
};

type HfsmMachine = {
  id: string;
  root: string;
  states: HfsmState[];
  transitions?: HfsmTransition[];
};

type TopologyEdgeData = {
  kind: 'child' | 'transition';
  predicate?: string;
  condition?: string;
};

const nodeTypes = { topology: TopologyNodeView };

const fieldClass =
  'mt-1 w-full bg-zinc-950 border border-zinc-700 rounded px-2 py-1.5 text-sm text-zinc-100';
const labelClass = 'block text-xs text-zinc-400';

function asArray<T>(value: unknown): T[] {
  return Array.isArray(value) ? (value as T[]) : [];
}

function emptyBtTree(id: string): BtTree {
  return {
    id,
    root: 'root',
    nodes: [{ id: 'root', kind: 'Selector', children: [] }],
  };
}

function emptyHfsm(id: string): HfsmMachine {
  return {
    id,
    root: 'root',
    states: [
      { id: 'root', kind: 'Compound', children: ['idle'], defaultChild: 'idle' },
      { id: 'idle', kind: 'Leaf' },
    ],
    transitions: [],
  };
}

function uniqueId(prefix: string, existing: Set<string>): string {
  let i = 1;
  while (existing.has(`${prefix}${i}`)) i += 1;
  return `${prefix}${i}`;
}

function btToFlow(tree: BtTree): { nodes: Node<TopologyNodeData>[]; edges: Edge<TopologyEdgeData>[] } {
  const nodes: Node<TopologyNodeData>[] = tree.nodes.map((n) => {
    const isLeaf = n.kind === 'Action' || n.kind === 'Condition';
    return {
      id: n.id,
      type: 'topology',
      position: { x: 0, y: 0 },
      data: {
        label: n.id,
        kind: n.kind,
        role: isLeaf ? 'leaf' : 'composite',
        subtitle: n.action || undefined,
        childCount: n.children?.length ?? 0,
      },
    };
  });

  const edges: Edge<TopologyEdgeData>[] = [];
  for (const n of tree.nodes) {
    for (const child of n.children ?? []) {
      edges.push({
        id: `child:${n.id}->${child}`,
        source: n.id,
        target: child,
        sourceHandle: 'out',
        targetHandle: 'in',
        data: { kind: 'child' },
        style: { stroke: '#a78bfa', strokeWidth: 2 },
        animated: false,
      });
    }
  }

  const positions = computeTopologyTreeLayout(nodes, edges, tree.root);
  for (const node of nodes) {
    node.position = positions[node.id] ?? node.position;
  }
  return { nodes, edges };
}

function hfsmToFlow(machine: HfsmMachine): { nodes: Node<TopologyNodeData>[]; edges: Edge<TopologyEdgeData>[] } {
  const nodes: Node<TopologyNodeData>[] = machine.states.map((s) => {
    const lifecycle = [s.onEnter, s.onTick, s.onExit].filter(Boolean).join(' · ');
    return {
      id: s.id,
      type: 'topology',
      position: { x: 0, y: 0 },
      data: {
        label: s.id,
        kind: s.kind,
        role: s.kind === 'Compound' ? 'compound' : 'state',
        subtitle: lifecycle || undefined,
        childCount: s.children?.length ?? 0,
      },
    };
  });

  const edges: Edge<TopologyEdgeData>[] = [];
  for (const s of machine.states) {
    for (const child of s.children ?? []) {
      edges.push({
        id: `child:${s.id}->${child}`,
        source: s.id,
        target: child,
        sourceHandle: 'out',
        targetHandle: 'in',
        data: { kind: 'child' },
        style: { stroke: '#e879f9', strokeWidth: 1.5, strokeDasharray: '4 4' },
        label: 'child',
        labelStyle: { fill: '#c026d3', fontSize: 10 },
      });
    }
  }

  for (const [idx, t] of (machine.transitions ?? []).entries()) {
    edges.push({
      id: `trans:${t.from}->${t.to}:${idx}`,
      source: t.from,
      target: t.to,
      sourceHandle: 'out',
      targetHandle: 'in',
      data: { kind: 'transition', predicate: t.predicate, condition: t.condition },
      style: { stroke: '#fbbf24', strokeWidth: 2.5 },
      animated: true,
      label: t.condition ? `${t.predicate} · ${t.condition}` : t.predicate,
      labelStyle: { fill: '#fcd34d', fontSize: 10 },
      labelBgStyle: { fill: '#1c1917', fillOpacity: 0.85 },
    });
  }

  const positions = computeTopologyTreeLayout(nodes, edges.filter((e) => e.data?.kind === 'child'), machine.root);
  for (const node of nodes) {
    node.position = positions[node.id] ?? node.position;
  }
  return { nodes, edges };
}

function flowToBt(treeId: string, root: string, nodes: Node<TopologyNodeData>[], edges: Edge<TopologyEdgeData>[], previous: BtTree): BtTree {
  const prevById = new Map(previous.nodes.map((n) => [n.id, n]));
  const childMap = new Map<string, string[]>();
  for (const edge of edges) {
    if (edge.data?.kind === 'transition') continue;
    const list = childMap.get(edge.source) ?? [];
    if (!list.includes(edge.target)) list.push(edge.target);
    childMap.set(edge.source, list);
  }

  const nextNodes: BtNode[] = nodes.map((n) => {
    const prev = prevById.get(n.id);
    const kind = (n.data.kind as BtNode['kind']) || prev?.kind || 'Action';
    const isLeaf = kind === 'Action' || kind === 'Condition';
    return {
      id: n.id,
      kind,
      children: isLeaf ? undefined : childMap.get(n.id) ?? [],
      leaf: isLeaf ? prev?.leaf ?? 'ScriptSlice' : undefined,
      action: isLeaf ? (n.data.subtitle || prev?.action || '') : undefined,
    };
  });

  const rootId = nodes.some((n) => n.id === root) ? root : (nodes[0]?.id ?? 'root');
  return { id: treeId, root: rootId, nodes: nextNodes };
}

function flowToHfsm(
  machineId: string,
  root: string,
  nodes: Node<TopologyNodeData>[],
  edges: Edge<TopologyEdgeData>[],
  previous: HfsmMachine,
): HfsmMachine {
  const prevById = new Map(previous.states.map((s) => [s.id, s]));
  const childMap = new Map<string, string[]>();
  const transitions: HfsmTransition[] = [];

  for (const edge of edges) {
    if (edge.data?.kind === 'transition') {
      transitions.push({
        from: edge.source,
        to: edge.target,
        predicate: edge.data.predicate || 'Always',
        condition: edge.data.condition || undefined,
      });
      continue;
    }
    const list = childMap.get(edge.source) ?? [];
    if (!list.includes(edge.target)) list.push(edge.target);
    childMap.set(edge.source, list);
  }

  const states: HfsmState[] = nodes.map((n) => {
    const prev = prevById.get(n.id);
    const kind = (n.data.kind as HfsmState['kind']) || prev?.kind || 'Leaf';
    const children = kind === 'Compound' ? childMap.get(n.id) ?? [] : undefined;
    return {
      id: n.id,
      kind,
      children,
      defaultChild: kind === 'Compound' ? prev?.defaultChild ?? children?.[0] : undefined,
      onEnter: prev?.onEnter,
      onTick: prev?.onTick,
      onExit: prev?.onExit,
    };
  });

  const rootId = nodes.some((n) => n.id === root) ? root : (nodes[0]?.id ?? 'root');
  return { id: machineId, root: rootId, states, transitions };
}

export const AiTopologyEditorPage: React.FC<{ kind: TopologyKind }> = ({ kind }) => {
  const navigate = useNavigate();
  const isBt = kind === 'behavior-trees';
  const title = isBt ? '行为树拓扑' : '状态机拓扑';
  const subtitle = isBt
    ? '图画布编辑 AI/behavior_trees.json · 拖线挂子节点 · 双击叶子进函数图'
    : '图画布编辑 AI/hfsm.json · 虚线=层级 · 黄线=转移 · 双击叶子进函数图';
  const actionHost = isBt ? 'BehaviorTree' : 'Hfsm';

  const [sources, setSources] = useState<CatalogSource[]>([]);
  const [sourceId, setSourceId] = useState('core');
  const [items, setItems] = useState<Array<BtTree | HfsmMachine>>([]);
  const [selectedId, setSelectedId] = useState('');
  const [selectedNodeId, setSelectedNodeId] = useState('');
  const [selectedEdgeId, setSelectedEdgeId] = useState('');
  const [actions, setActions] = useState<ActionLibEntry[]>([]);
  const [status, setStatus] = useState('');
  const [error, setError] = useState('');
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [connectMode, setConnectMode] = useState<'child' | 'transition'>('child');
  const reactFlowRef = useRef<ReactFlowInstance | null>(null);

  const [nodes, setNodes, onNodesChange] = useNodesState<Node<TopologyNodeData>>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge<TopologyEdgeData>>([]);

  const selected = useMemo(
    () => items.find((row) => row.id === selectedId) ?? null,
    [items, selectedId],
  );

  const loadCatalog = useCallback(async () => {
    const res = await fetch('/api/ai/topology-catalog');
    const json = await res.json();
    if (!json.ok) {
      setError(json.error ?? '加载拓扑目录失败');
      return;
    }
    setSources(json.sources ?? []);
    setError('');
  }, []);

  const loadActions = useCallback(async () => {
    const res = await fetch(`/api/ai/action-lib?host=${encodeURIComponent(actionHost)}`);
    const json = await res.json();
    if (!json.ok) {
      setActions([]);
      return;
    }
    setActions(asArray<ActionLibEntry>(json.actions));
  }, [actionHost]);

  const applySelectionToFlow = useCallback((row: BtTree | HfsmMachine | null) => {
    if (!row) {
      setNodes([]);
      setEdges([]);
      return;
    }
    const flow = isBt ? btToFlow(row as BtTree) : hfsmToFlow(row as HfsmMachine);
    setNodes(flow.nodes);
    setEdges(flow.edges);
    setSelectedNodeId('');
    setSelectedEdgeId('');
    requestAnimationFrame(() => reactFlowRef.current?.fitView({ padding: 0.2 }));
  }, [isBt, setEdges, setNodes]);

  const loadItems = useCallback(async (source: string) => {
    setStatus('加载中…');
    const endpoint = isBt ? '/api/ai/behavior-trees' : '/api/ai/hfsm';
    const res = await fetch(`${endpoint}?source=${encodeURIComponent(source)}`);
    const json = await res.json();
    if (!json.ok) {
      setError(json.error ?? '加载失败');
      setStatus('');
      setItems([]);
      applySelectionToFlow(null);
      return;
    }
    const next = asArray<BtTree | HfsmMachine>(json.items);
    setItems(next);
    const first = next[0] ?? null;
    setSelectedId(first?.id ?? '');
    applySelectionToFlow(first);
    setError('');
    setStatus(`已加载 ${source} · ${next.length} 条`);
  }, [applySelectionToFlow, isBt]);

  useEffect(() => {
    void loadCatalog();
    void loadActions();
  }, [loadCatalog, loadActions]);

  useEffect(() => {
    void loadItems(sourceId);
  }, [sourceId, loadItems]);

  const syncItemsFromFlow = useCallback(
    (nextNodes: Node<TopologyNodeData>[], nextEdges: Edge<TopologyEdgeData>[]) => {
      if (!selected) return;
      const updated = isBt
        ? flowToBt(selected.id, (selected as BtTree).root, nextNodes, nextEdges, selected as BtTree)
        : flowToHfsm(selected.id, (selected as HfsmMachine).root, nextNodes, nextEdges, selected as HfsmMachine);
      setItems((prev) => prev.map((row) => (row.id === selected.id ? updated : row)));
    },
    [isBt, selected],
  );

  const saveItems = useCallback(async (next: Array<BtTree | HfsmMachine>) => {
    setStatus('保存中…');
    const endpoint = isBt ? '/api/ai/behavior-trees' : '/api/ai/hfsm';
    const res = await fetch(`${endpoint}?source=${encodeURIComponent(sourceId)}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ items: next }),
    });
    const json = await res.json();
    if (!json.ok) {
      setError(json.error ?? '保存失败');
      setStatus('');
      return;
    }
    setItems(next);
    setError('');
    setStatus(`已写入 ${json.path}`);
    void loadCatalog();
  }, [isBt, sourceId, loadCatalog]);

  const openLeafGraph = useCallback(
    (actionName: string | undefined) => {
      if (!actionName) {
        setError('这个叶子还没挂 ActionLib 名字');
        return;
      }
      const entry = actions.find((a) => a.name === actionName);
      if (!entry?.graph) {
        setError(`ActionLib 里找不到 ${actionName}，或没有 graph 字段`);
        return;
      }
      navigate(`/gas-graphs?mod=core&graph=${encodeURIComponent(entry.graph)}`);
    },
    [actions, navigate],
  );

  const onConnect: OnConnect = useCallback(
    (connection: Connection) => {
      if (!connection.source || !connection.target || connection.source === connection.target) return;
      const kind = !isBt && connectMode === 'transition' ? 'transition' : 'child';
      const edge: Edge<TopologyEdgeData> = {
        id: `${kind}:${connection.source}->${connection.target}:${Date.now()}`,
        source: connection.source,
        target: connection.target,
        sourceHandle: connection.sourceHandle ?? 'out',
        targetHandle: connection.targetHandle ?? 'in',
        data:
          kind === 'transition'
            ? { kind: 'transition', predicate: 'Always' }
            : { kind: 'child' },
        style:
          kind === 'transition'
            ? { stroke: '#fbbf24', strokeWidth: 2.5 }
            : isBt
              ? { stroke: '#a78bfa', strokeWidth: 2 }
              : { stroke: '#e879f9', strokeWidth: 1.5, strokeDasharray: '4 4' },
        animated: kind === 'transition',
        label: kind === 'transition' ? 'Always' : kind === 'child' && !isBt ? 'child' : undefined,
      };
      setEdges((eds) => {
        const next = addEdge(edge, eds);
        syncItemsFromFlow(nodes, next);
        return next;
      });
    },
    [connectMode, isBt, nodes, setEdges, syncItemsFromFlow],
  );

  const addNode = useCallback(
    (kindValue: string) => {
      if (!selected) return;
      const existing = new Set(nodes.map((n) => n.id));
      const id = uniqueId(isBt ? 'n' : 's', existing);
      const isLeaf = isBt
        ? kindValue === 'Action' || kindValue === 'Condition'
        : kindValue === 'Leaf';
      const role = isBt
        ? (isLeaf ? 'leaf' : 'composite')
        : (kindValue === 'Compound' ? 'compound' : 'state');
      const position = reactFlowRef.current?.screenToFlowPosition({
        x: window.innerWidth / 2,
        y: window.innerHeight / 2,
      }) ?? { x: 120 + nodes.length * 24, y: 80 + nodes.length * 24 };

      const node: Node<TopologyNodeData> = {
        id,
        type: 'topology',
        position,
        data: {
          label: id,
          kind: kindValue,
          role,
          childCount: 0,
        },
      };
      const nextNodes = [...nodes, node];
      setNodes(nextNodes);
      syncItemsFromFlow(nextNodes, edges);
      setSelectedNodeId(id);
      setPaletteOpen(false);
    },
    [edges, isBt, nodes, selected, setNodes, syncItemsFromFlow],
  );

  const updateSelectedNode = useCallback(
    (patch: Record<string, unknown>) => {
      if (!selected || !selectedNodeId) return;
      if (isBt) {
        const tree = selected as BtTree;
        const nextTree: BtTree = {
          ...tree,
          nodes: tree.nodes.map((n) => {
            if (n.id !== selectedNodeId) return n;
            const kind = (patch.kind as BtNode['kind'] | undefined) ?? n.kind;
            const isLeaf = kind === 'Action' || kind === 'Condition';
            return {
              ...n,
              kind,
              children: isLeaf ? undefined : n.children ?? [],
              leaf: isLeaf
                ? ((patch.leaf as string | undefined) ?? n.leaf ?? 'ScriptSlice')
                : undefined,
              action: isLeaf
                ? ((patch.action as string | undefined) ?? n.action ?? '')
                : undefined,
            };
          }),
        };
        setItems((prev) => prev.map((row) => (row.id === tree.id ? nextTree : row)));
        applySelectionToFlow(nextTree);
      } else {
        const machine = selected as HfsmMachine;
        const nextMachine: HfsmMachine = {
          ...machine,
          states: machine.states.map((s) => {
            if (s.id !== selectedNodeId) return s;
            const kind = (patch.kind as HfsmState['kind'] | undefined) ?? s.kind;
            return {
              ...s,
              kind,
              children: kind === 'Compound' ? s.children ?? [] : undefined,
              defaultChild: kind === 'Compound'
                ? ((patch.defaultChild as string | undefined) ?? s.defaultChild)
                : undefined,
              onEnter: patch.onEnter !== undefined ? (patch.onEnter as string | undefined) : s.onEnter,
              onTick: patch.onTick !== undefined ? (patch.onTick as string | undefined) : s.onTick,
              onExit: patch.onExit !== undefined ? (patch.onExit as string | undefined) : s.onExit,
            };
          }),
        };
        setItems((prev) => prev.map((row) => (row.id === machine.id ? nextMachine : row)));
        applySelectionToFlow(nextMachine);
      }
    },
    [applySelectionToFlow, isBt, selected, selectedNodeId],
  );

  const updateSelectedTransition = useCallback(
    (patch: Partial<HfsmTransition>) => {
      if (isBt || !selected || !selectedEdgeId) return;
      const edge = edges.find((e) => e.id === selectedEdgeId);
      if (!edge || edge.data?.kind !== 'transition') return;
      const nextEdges = edges.map((e) => {
        if (e.id !== selectedEdgeId) return e;
        const predicate = patch.predicate ?? e.data?.predicate ?? 'Always';
        const condition = patch.condition !== undefined ? patch.condition : e.data?.condition;
        return {
          ...e,
          data: { kind: 'transition' as const, predicate, condition: condition || undefined },
          label: condition ? `${predicate} · ${condition}` : predicate,
        };
      });
      setEdges(nextEdges);
      syncItemsFromFlow(nodes, nextEdges);
    },
    [edges, isBt, nodes, selected, selectedEdgeId, setEdges, syncItemsFromFlow],
  );

  const selectedBtNode = isBt && selected
    ? (selected as BtTree).nodes.find((n) => n.id === selectedNodeId)
    : undefined;
  const selectedHfsmState = !isBt && selected
    ? (selected as HfsmMachine).states.find((s) => s.id === selectedNodeId)
    : undefined;
  const selectedTransition = !isBt
    ? edges.find((e) => e.id === selectedEdgeId && e.data?.kind === 'transition')
    : undefined;

  return (
    <div className="flex h-screen w-screen flex-col bg-zinc-950 text-zinc-100">
      <header className="flex flex-wrap items-center gap-3 border-b border-zinc-800 bg-zinc-900 px-4 py-3">
        <div className="min-w-40">
          <div className="text-sm font-semibold text-white">{title}</div>
          <div className="text-[10px] text-zinc-500">{subtitle}</div>
        </div>
        <Link to="/" className="rounded border border-zinc-700 px-2 py-1 text-xs text-zinc-300 hover:bg-zinc-800">
          主编辑器
        </Link>
        <Link to="/gas-graphs" className="rounded border border-sky-500/40 px-2 py-1 text-xs text-sky-300 hover:bg-sky-500/10">
          函数图
        </Link>
        <Link
          to={isBt ? '/fsm-editor' : '/bt-editor'}
          className="rounded border border-violet-500/40 px-2 py-1 text-xs text-violet-300 hover:bg-violet-500/10"
        >
          {isBt ? '状态机' : '行为树'}
        </Link>
        <label className="flex items-center gap-2 text-xs text-zinc-400">
          数据源
          <select
            className="rounded border border-zinc-700 bg-zinc-950 px-2 py-1 text-zinc-100"
            value={sourceId}
            onChange={(e) => setSourceId(e.target.value)}
          >
            {sources.map((s) => (
              <option key={s.id} value={s.id}>{s.name} ({s.id})</option>
            ))}
            {sources.length === 0 ? <option value="core">Core</option> : null}
          </select>
        </label>
        {!isBt ? (
          <label className="flex items-center gap-2 text-xs text-zinc-400">
            连线模式
            <select
              className="rounded border border-zinc-700 bg-zinc-950 px-2 py-1 text-zinc-100"
              value={connectMode}
              onChange={(e) => setConnectMode(e.target.value as 'child' | 'transition')}
            >
              <option value="child">层级（Compound→子状态）</option>
              <option value="transition">转移（状态→状态）</option>
            </select>
          </label>
        ) : null}
        <button
          type="button"
          className="rounded border border-zinc-600 px-2 py-1 text-xs hover:bg-zinc-800"
          onClick={() => void loadItems(sourceId)}
        >
          重新加载
        </button>
        <button
          type="button"
          className="rounded border border-emerald-500/50 px-2 py-1 text-xs text-emerald-300 hover:bg-emerald-500/10"
          onClick={() => {
            // Flush current canvas into items then save.
            if (selected) {
              const flushed = isBt
                ? flowToBt(selected.id, (selected as BtTree).root, nodes, edges, selected as BtTree)
                : flowToHfsm(selected.id, (selected as HfsmMachine).root, nodes, edges, selected as HfsmMachine);
              const next = items.map((row) => (row.id === selected.id ? flushed : row));
              void saveItems(next);
            } else {
              void saveItems(items);
            }
          }}
        >
          保存
        </button>
        {status ? <span className="text-xs text-emerald-400">{status}</span> : null}
        {error ? <span className="text-xs text-rose-400">{error}</span> : null}
      </header>

      <div className="grid min-h-0 flex-1 grid-cols-12">
        <aside className="col-span-2 space-y-2 overflow-auto border-r border-zinc-800 p-3">
          <div className="text-[10px] uppercase tracking-wide text-zinc-500">拓扑清单</div>
          {items.map((row) => (
            <button
              key={row.id}
              type="button"
              className={`w-full rounded border px-2 py-1.5 text-left text-sm ${
                selectedId === row.id ? 'border-emerald-400 bg-emerald-500/10' : 'border-zinc-800 bg-zinc-900/60'
              }`}
              onClick={() => {
                if (selected) {
                  const flushed = isBt
                    ? flowToBt(selected.id, (selected as BtTree).root, nodes, edges, selected as BtTree)
                    : flowToHfsm(selected.id, (selected as HfsmMachine).root, nodes, edges, selected as HfsmMachine);
                  setItems((prev) => prev.map((r) => (r.id === selected.id ? flushed : r)));
                }
                setSelectedId(row.id);
                applySelectionToFlow(row);
              }}
            >
              {row.id}
            </button>
          ))}
          <button
            type="button"
            className="w-full rounded border border-dashed border-zinc-600 px-2 py-1.5 text-xs text-zinc-400 hover:bg-zinc-900"
            onClick={() => {
              const id = isBt ? `bt.new.${items.length + 1}` : `hfsm.new.${items.length + 1}`;
              const created = isBt ? emptyBtTree(id) : emptyHfsm(id);
              const next = [...items, created];
              setItems(next);
              setSelectedId(id);
              applySelectionToFlow(created);
            }}
          >
            + 新建拓扑
          </button>
        </aside>

        <main className="relative col-span-7 min-h-0 border-r border-zinc-800">
          {selected ? (
            <>
              <ReactFlow
                className="topology-flow"
                nodes={nodes}
                edges={edges}
                nodeTypes={nodeTypes}
                onInit={(instance) => { reactFlowRef.current = instance; }}
                onNodesChange={onNodesChange}
                onEdgesChange={onEdgesChange}
                onConnect={onConnect}
                onNodeClick={(_, node) => {
                  setSelectedNodeId(node.id);
                  setSelectedEdgeId('');
                }}
                onEdgeClick={(_, edge) => {
                  setSelectedEdgeId(edge.id);
                  setSelectedNodeId('');
                }}
                onNodeDoubleClick={(_, node) => {
                  const actionName = node.data.subtitle?.split(' · ')[0];
                  if (node.data.role === 'leaf' || node.data.role === 'state') {
                    openLeafGraph(actionName);
                  }
                }}
                onPaneClick={() => {
                  setSelectedNodeId('');
                  setSelectedEdgeId('');
                  setPaletteOpen(false);
                }}
                onPaneContextMenu={(event) => {
                  event.preventDefault();
                  setPaletteOpen(true);
                }}
                panOnDrag={[1]}
                selectionOnDrag
                selectionMode={SelectionMode.Partial}
                fitView
                minZoom={0.15}
                maxZoom={1.8}
                proOptions={{ hideAttribution: true }}
              >
                <Background gap={18} color="#3f3f46" />
                <Controls />
                <MiniMap
                  pannable
                  zoomable
                  bgColor="#09090b"
                  maskColor="rgba(9,9,11,0.4)"
                  nodeColor={(node) => {
                    const role = (node.data as TopologyNodeData).role;
                    if (role === 'composite') return '#a78bfa';
                    if (role === 'leaf') return '#38bdf8';
                    if (role === 'compound') return '#e879f9';
                    return '#fbbf24';
                  }}
                />
              </ReactFlow>
              <div className="pointer-events-none absolute left-3 top-3 z-10 rounded border border-zinc-800 bg-zinc-950/80 px-2 py-1 text-[10px] text-zinc-400">
                中键平移 · 左键框选 · 右键添加节点 · 从节点下方拖线连接
              </div>
              <div className="absolute bottom-3 left-3 z-10 flex gap-2">
                <button
                  type="button"
                  className="rounded border border-zinc-600 bg-zinc-950/90 px-2 py-1 text-xs text-zinc-200 hover:bg-zinc-800"
                  onClick={() => setPaletteOpen((v) => !v)}
                >
                  添加节点
                </button>
                <button
                  type="button"
                  className="rounded border border-zinc-600 bg-zinc-950/90 px-2 py-1 text-xs text-zinc-200 hover:bg-zinc-800"
                  onClick={() => {
                    if (!selected) return;
                    const flow = isBt ? btToFlow(selected as BtTree) : hfsmToFlow(selected as HfsmMachine);
                    setNodes(flow.nodes);
                    setEdges(flow.edges);
                    requestAnimationFrame(() => reactFlowRef.current?.fitView({ padding: 0.2 }));
                  }}
                >
                  自动排版
                </button>
              </div>
              {paletteOpen ? (
                <div className="absolute bottom-14 left-3 z-20 w-56 rounded border border-zinc-700 bg-zinc-950 p-2 shadow-xl">
                  <div className="mb-2 text-[10px] uppercase tracking-wide text-zinc-500">调色板</div>
                  {(isBt
                    ? ['Selector', 'Sequence', 'Action', 'Condition']
                    : ['Compound', 'Leaf']
                  ).map((k) => (
                    <button
                      key={k}
                      type="button"
                      className="mb-1 flex w-full items-center rounded px-2 py-1.5 text-left text-xs text-zinc-100 hover:bg-zinc-800"
                      onClick={() => addNode(k)}
                    >
                      {k}
                    </button>
                  ))}
                </div>
              ) : null}
            </>
          ) : (
            <div className="flex h-full items-center justify-center text-sm text-zinc-500">
              左边选一条拓扑
            </div>
          )}
        </main>

        <aside className="col-span-3 space-y-4 overflow-auto p-4">
          <div className="text-[10px] uppercase tracking-wide text-zinc-500">检查器</div>
          {selectedBtNode ? (
            <div className="space-y-3">
              <label className={labelClass}>
                节点 id
                <input className={fieldClass} value={selectedBtNode.id} readOnly />
              </label>
              <label className={labelClass}>
                kind
                <select
                  className={fieldClass}
                  value={selectedBtNode.kind}
                  onChange={(e) => updateSelectedNode({ kind: e.target.value as BtNode['kind'] })}
                >
                  <option value="Selector">Selector</option>
                  <option value="Sequence">Sequence</option>
                  <option value="Action">Action</option>
                  <option value="Condition">Condition</option>
                </select>
              </label>
              {(selectedBtNode.kind === 'Action' || selectedBtNode.kind === 'Condition') ? (
                <>
                  <label className={labelClass}>
                    leaf
                    <select
                      className={fieldClass}
                      value={selectedBtNode.leaf ?? 'ScriptSlice'}
                      onChange={(e) => updateSelectedNode({ leaf: e.target.value })}
                    >
                      <option value="ScriptSlice">ScriptSlice</option>
                      <option value="AlwaysSuccess">AlwaysSuccess</option>
                      <option value="AlwaysFailure">AlwaysFailure</option>
                      <option value="HoldRunning">HoldRunning</option>
                      <option value="None">None</option>
                    </select>
                  </label>
                  <label className={labelClass}>
                    ActionLib
                    <select
                      className={fieldClass}
                      value={selectedBtNode.action ?? ''}
                      onChange={(e) => updateSelectedNode({ action: e.target.value })}
                    >
                      <option value="">（未挂）</option>
                      {actions.map((a) => (
                        <option key={a.name} value={a.name}>{a.name} → {a.graph}</option>
                      ))}
                    </select>
                  </label>
                  <button
                    type="button"
                    className="rounded border border-sky-500/50 px-3 py-1.5 text-xs text-sky-300 hover:bg-sky-500/10"
                    onClick={() => openLeafGraph(selectedBtNode.action)}
                  >
                    打开叶子函数图
                  </button>
                </>
              ) : (
                <div className="text-xs text-zinc-500">
                  组合节点：从下方手柄拖线到子节点。子序 = 连线顺序。
                </div>
              )}
            </div>
          ) : null}

          {selectedHfsmState ? (
            <div className="space-y-3">
              <label className={labelClass}>
                状态 id
                <input className={fieldClass} value={selectedHfsmState.id} readOnly />
              </label>
              <label className={labelClass}>
                kind
                <select
                  className={fieldClass}
                  value={selectedHfsmState.kind}
                  onChange={(e) => updateSelectedNode({ kind: e.target.value as HfsmState['kind'] })}
                >
                  <option value="Compound">Compound</option>
                  <option value="Leaf">Leaf</option>
                </select>
              </label>
              {selectedHfsmState.kind === 'Compound' ? (
                <label className={labelClass}>
                  defaultChild
                  <input
                    className={fieldClass}
                    value={selectedHfsmState.defaultChild ?? ''}
                    onChange={(e) => updateSelectedNode({ defaultChild: e.target.value })}
                  />
                </label>
              ) : (
                (['onEnter', 'onTick', 'onExit'] as const).map((field) => (
                  <label key={field} className={labelClass}>
                    {field}
                    <select
                      className={fieldClass}
                      value={selectedHfsmState[field] ?? ''}
                      onChange={(e) => updateSelectedNode({ [field]: e.target.value || undefined })}
                    >
                      <option value="">（无）</option>
                      {actions.map((a) => (
                        <option key={a.name} value={a.name}>{a.name} → {a.graph}</option>
                      ))}
                    </select>
                  </label>
                ))
              )}
              {(selectedHfsmState.onEnter || selectedHfsmState.onTick || selectedHfsmState.onExit) ? (
                <button
                  type="button"
                  className="rounded border border-sky-500/50 px-3 py-1.5 text-xs text-sky-300 hover:bg-sky-500/10"
                  onClick={() => openLeafGraph(selectedHfsmState.onTick || selectedHfsmState.onEnter || selectedHfsmState.onExit)}
                >
                  打开叶子函数图
                </button>
              ) : null}
            </div>
          ) : null}

          {selectedTransition ? (
            <div className="space-y-3">
              <div className="text-xs text-amber-200">
                转移 {selectedTransition.source} → {selectedTransition.target}
              </div>
              <label className={labelClass}>
                predicate
                <select
                  className={fieldClass}
                  value={selectedTransition.data?.predicate ?? 'Always'}
                  onChange={(e) => updateSelectedTransition({ predicate: e.target.value })}
                >
                  <option value="Never">Never</option>
                  <option value="Always">Always</option>
                  <option value="StimulusLatched">StimulusLatched</option>
                </select>
              </label>
              <label className={labelClass}>
                condition（ActionLib）
                <select
                  className={fieldClass}
                  value={selectedTransition.data?.condition ?? ''}
                  onChange={(e) => updateSelectedTransition({ condition: e.target.value })}
                >
                  <option value="">（无）</option>
                  {actions.map((a) => (
                    <option key={a.name} value={a.name}>{a.name}</option>
                  ))}
                </select>
              </label>
            </div>
          ) : null}

          {!selectedBtNode && !selectedHfsmState && !selectedTransition ? (
            <div className="text-sm text-zinc-500">点画布上的节点或转移边看详情</div>
          ) : null}
        </aside>
      </div>
    </div>
  );
};
