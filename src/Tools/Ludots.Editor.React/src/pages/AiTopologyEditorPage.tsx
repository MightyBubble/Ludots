import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';

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

export const AiTopologyEditorPage: React.FC<{ kind: TopologyKind }> = ({ kind }) => {
  const navigate = useNavigate();
  const isBt = kind === 'behavior-trees';
  const title = isBt ? '行为树拓扑' : '状态机拓扑';
  const subtitle = isBt
    ? '编辑 AI/behavior_trees.json · 叶子挂 ActionLib · 双击叶子进函数图'
    : '编辑 AI/hfsm.json · 生命周期 / 条件挂 ActionLib · 双击进函数图';
  const actionHost = isBt ? 'BehaviorTree' : 'Hfsm';

  const [sources, setSources] = useState<CatalogSource[]>([]);
  const [sourceId, setSourceId] = useState('core');
  const [items, setItems] = useState<Array<BtTree | HfsmMachine>>([]);
  const [selectedId, setSelectedId] = useState('');
  const [selectedNodeId, setSelectedNodeId] = useState('');
  const [actions, setActions] = useState<ActionLibEntry[]>([]);
  const [status, setStatus] = useState('');
  const [error, setError] = useState('');
  const [advancedJson, setAdvancedJson] = useState(false);
  const [itemsText, setItemsText] = useState('[]');

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

  const loadItems = useCallback(async (source: string) => {
    setStatus('加载中…');
    const endpoint = isBt ? '/api/ai/behavior-trees' : '/api/ai/hfsm';
    const res = await fetch(`${endpoint}?source=${encodeURIComponent(source)}`);
    const json = await res.json();
    if (!json.ok) {
      setError(json.error ?? '加载失败');
      setStatus('');
      setItems([]);
      return;
    }
    const next = asArray<BtTree | HfsmMachine>(json.items);
    setItems(next);
    setItemsText(JSON.stringify(next, null, 2));
    setSelectedId(next[0]?.id ?? '');
    setSelectedNodeId('');
    setError('');
    setStatus(`已加载 ${source} · ${next.length} 条`);
  }, [isBt]);

  useEffect(() => {
    void loadCatalog();
    void loadActions();
  }, [loadCatalog, loadActions]);

  useEffect(() => {
    void loadItems(sourceId);
  }, [sourceId, loadItems]);

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
    setItemsText(JSON.stringify(next, null, 2));
    setError('');
    setStatus(`已写入 ${json.path}`);
    void loadCatalog();
  }, [isBt, sourceId, loadCatalog]);

  const updateSelected = useCallback(
    (mutator: (row: BtTree | HfsmMachine) => BtTree | HfsmMachine) => {
      if (!selected) return;
      const next = items.map((row) => (row.id === selected.id ? mutator(row) : row));
      setItems(next);
      setItemsText(JSON.stringify(next, null, 2));
    },
    [items, selected],
  );

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
      navigate(`/gas-graphs?graph=${encodeURIComponent(entry.graph)}`);
    },
    [actions, navigate],
  );

  const renderBtTree = (tree: BtTree) => {
    const byId = new Map(tree.nodes.map((n) => [n.id, n]));
    const walk = (nodeId: string, depth: number): React.ReactNode => {
      const node = byId.get(nodeId);
      if (!node) {
        return (
          <div key={nodeId} className="text-rose-400 text-xs pl-2">
            缺失节点 {nodeId}
          </div>
        );
      }
      const isLeaf = node.kind === 'Action' || node.kind === 'Condition';
      const selectedCls = selectedNodeId === node.id ? 'border-emerald-400 bg-emerald-500/10' : 'border-zinc-700 bg-zinc-900/80';
      return (
        <div key={node.id} className="mt-1">
          <button
            type="button"
            className={`w-full text-left rounded border px-2 py-1.5 ${selectedCls}`}
            style={{ marginLeft: depth * 14 }}
            onClick={() => setSelectedNodeId(node.id)}
            onDoubleClick={() => {
              if (isLeaf) openLeafGraph(node.action);
            }}
          >
            <span className="font-mono text-xs text-zinc-300">{node.kind}</span>
            <span className="ml-2 text-sm text-zinc-100">{node.id}</span>
            {node.action ? (
              <span className="ml-2 text-xs text-sky-300">{node.action}</span>
            ) : null}
            {isLeaf ? (
              <span className="ml-2 text-[10px] text-zinc-500">双击进函数图</span>
            ) : null}
          </button>
          {(node.children ?? []).map((child) => walk(child, depth + 1))}
        </div>
      );
    };
    return <div className="space-y-1">{walk(tree.root, 0)}</div>;
  };

  const renderHfsm = (machine: HfsmMachine) => {
    const byId = new Map(machine.states.map((s) => [s.id, s]));
    const walk = (stateId: string, depth: number): React.ReactNode => {
      const state = byId.get(stateId);
      if (!state) {
        return (
          <div key={stateId} className="text-rose-400 text-xs pl-2">
            缺失状态 {stateId}
          </div>
        );
      }
      const selectedCls = selectedNodeId === state.id ? 'border-fuchsia-400 bg-fuchsia-500/10' : 'border-zinc-700 bg-zinc-900/80';
      const lifecycle = [state.onEnter, state.onTick, state.onExit].filter(Boolean).join(' · ');
      return (
        <div key={state.id} className="mt-1">
          <button
            type="button"
            className={`w-full text-left rounded border px-2 py-1.5 ${selectedCls}`}
            style={{ marginLeft: depth * 14 }}
            onClick={() => setSelectedNodeId(state.id)}
            onDoubleClick={() => {
              const first = state.onTick || state.onEnter || state.onExit;
              if (first) openLeafGraph(first);
            }}
          >
            <span className="font-mono text-xs text-zinc-300">{state.kind}</span>
            <span className="ml-2 text-sm text-zinc-100">{state.id}</span>
            {lifecycle ? <span className="ml-2 text-xs text-sky-300">{lifecycle}</span> : null}
          </button>
          {(state.children ?? []).map((child) => walk(child, depth + 1))}
        </div>
      );
    };

    return (
      <div className="space-y-3">
        <div className="space-y-1">{walk(machine.root, 0)}</div>
        <div>
          <div className="text-xs text-zinc-500 mb-1">转移</div>
          <div className="space-y-1">
            {(machine.transitions ?? []).map((t, idx) => (
              <div key={`${t.from}-${t.to}-${idx}`} className="rounded border border-zinc-800 bg-zinc-950 px-2 py-1 text-xs text-zinc-300 font-mono">
                {t.from} → {t.to} · {t.predicate}
                {t.condition ? ` · ${t.condition}` : ''}
              </div>
            ))}
          </div>
        </div>
      </div>
    );
  };

  const inspector = () => {
    if (!selected) return <div className="text-sm text-zinc-500">先选一条拓扑</div>;
    if (isBt) {
      const tree = selected as BtTree;
      const node = tree.nodes.find((n) => n.id === selectedNodeId);
      if (!node) return <div className="text-sm text-zinc-500">点左边的节点看详情</div>;
      const isLeaf = node.kind === 'Action' || node.kind === 'Condition';
      return (
        <div className="space-y-3">
          <label className={labelClass}>
            节点 id
            <input className={fieldClass} value={node.id} readOnly />
          </label>
          <label className={labelClass}>
            kind
            <select
              className={fieldClass}
              value={node.kind}
              onChange={(e) => {
                const kindValue = e.target.value as BtNode['kind'];
                updateSelected((row) => {
                  const t = row as BtTree;
                  return {
                    ...t,
                    nodes: t.nodes.map((n) =>
                      n.id === node.id
                        ? {
                            ...n,
                            kind: kindValue,
                            children: kindValue === 'Sequence' || kindValue === 'Selector' ? n.children ?? [] : undefined,
                            leaf: kindValue === 'Action' || kindValue === 'Condition' ? n.leaf ?? 'ScriptSlice' : undefined,
                            action: kindValue === 'Action' || kindValue === 'Condition' ? n.action ?? '' : undefined,
                          }
                        : n,
                    ),
                  };
                });
              }}
            >
              <option value="Selector">Selector</option>
              <option value="Sequence">Sequence</option>
              <option value="Action">Action</option>
              <option value="Condition">Condition</option>
            </select>
          </label>
          {isLeaf ? (
            <>
              <label className={labelClass}>
                leaf
                <select
                  className={fieldClass}
                  value={node.leaf ?? 'ScriptSlice'}
                  onChange={(e) => {
                    updateSelected((row) => {
                      const t = row as BtTree;
                      return {
                        ...t,
                        nodes: t.nodes.map((n) => (n.id === node.id ? { ...n, leaf: e.target.value } : n)),
                      };
                    });
                  }}
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
                  value={node.action ?? ''}
                  onChange={(e) => {
                    updateSelected((row) => {
                      const t = row as BtTree;
                      return {
                        ...t,
                        nodes: t.nodes.map((n) => (n.id === node.id ? { ...n, action: e.target.value } : n)),
                      };
                    });
                  }}
                >
                  <option value="">（未挂）</option>
                  {actions.map((a) => (
                    <option key={a.name} value={a.name}>
                      {a.name} → {a.graph}
                    </option>
                  ))}
                </select>
              </label>
              <button
                type="button"
                className="rounded border border-sky-500/50 px-3 py-1.5 text-xs text-sky-300 hover:bg-sky-500/10"
                onClick={() => openLeafGraph(node.action)}
              >
                打开叶子函数图
              </button>
            </>
          ) : (
            <label className={labelClass}>
              children（逗号分隔）
              <input
                className={fieldClass}
                value={(node.children ?? []).join(',')}
                onChange={(e) => {
                  const children = e.target.value
                    .split(',')
                    .map((s) => s.trim())
                    .filter(Boolean);
                  updateSelected((row) => {
                    const t = row as BtTree;
                    return {
                      ...t,
                      nodes: t.nodes.map((n) => (n.id === node.id ? { ...n, children } : n)),
                    };
                  });
                }}
              />
            </label>
          )}
        </div>
      );
    }

    const machine = selected as HfsmMachine;
    const state = machine.states.find((s) => s.id === selectedNodeId);
    if (!state) return <div className="text-sm text-zinc-500">点左边的状态看详情</div>;
    return (
      <div className="space-y-3">
        <label className={labelClass}>
          状态 id
          <input className={fieldClass} value={state.id} readOnly />
        </label>
        <label className={labelClass}>
          kind
          <select
            className={fieldClass}
            value={state.kind}
            onChange={(e) => {
              const kindValue = e.target.value as HfsmState['kind'];
              updateSelected((row) => {
                const m = row as HfsmMachine;
                return {
                  ...m,
                  states: m.states.map((s) =>
                    s.id === state.id
                      ? {
                          ...s,
                          kind: kindValue,
                          children: kindValue === 'Compound' ? s.children ?? [] : undefined,
                          defaultChild: kindValue === 'Compound' ? s.defaultChild : undefined,
                        }
                      : s,
                  ),
                };
              });
            }}
          >
            <option value="Compound">Compound</option>
            <option value="Leaf">Leaf</option>
          </select>
        </label>
        {state.kind === 'Compound' ? (
          <>
            <label className={labelClass}>
              children（逗号分隔）
              <input
                className={fieldClass}
                value={(state.children ?? []).join(',')}
                onChange={(e) => {
                  const children = e.target.value
                    .split(',')
                    .map((s) => s.trim())
                    .filter(Boolean);
                  updateSelected((row) => {
                    const m = row as HfsmMachine;
                    return {
                      ...m,
                      states: m.states.map((s) => (s.id === state.id ? { ...s, children } : s)),
                    };
                  });
                }}
              />
            </label>
            <label className={labelClass}>
              defaultChild
              <input
                className={fieldClass}
                value={state.defaultChild ?? ''}
                onChange={(e) => {
                  updateSelected((row) => {
                    const m = row as HfsmMachine;
                    return {
                      ...m,
                      states: m.states.map((s) =>
                        s.id === state.id ? { ...s, defaultChild: e.target.value } : s,
                      ),
                    };
                  });
                }}
              />
            </label>
          </>
        ) : (
          (['onEnter', 'onTick', 'onExit'] as const).map((field) => (
            <label key={field} className={labelClass}>
              {field}
              <select
                className={fieldClass}
                value={state[field] ?? ''}
                onChange={(e) => {
                  updateSelected((row) => {
                    const m = row as HfsmMachine;
                    return {
                      ...m,
                      states: m.states.map((s) =>
                        s.id === state.id ? { ...s, [field]: e.target.value || undefined } : s,
                      ),
                    };
                  });
                }}
              >
                <option value="">（无）</option>
                {actions.map((a) => (
                  <option key={a.name} value={a.name}>
                    {a.name} → {a.graph}
                  </option>
                ))}
              </select>
            </label>
          ))
        )}
        {(state.onEnter || state.onTick || state.onExit) && (
          <button
            type="button"
            className="rounded border border-sky-500/50 px-3 py-1.5 text-xs text-sky-300 hover:bg-sky-500/10"
            onClick={() => openLeafGraph(state.onTick || state.onEnter || state.onExit)}
          >
            打开叶子函数图
          </button>
        )}
      </div>
    );
  };

  return (
    <div className="min-h-screen bg-zinc-950 text-zinc-100">
      <header className="border-b border-zinc-800 px-4 py-3 flex items-center justify-between gap-4">
        <div>
          <div className="text-lg font-semibold tracking-wide">{title}</div>
          <div className="text-xs text-zinc-500">{subtitle}</div>
        </div>
        <div className="flex items-center gap-2 text-xs">
          <Link to="/" className="rounded border border-zinc-700 px-2 py-1 hover:bg-zinc-900">
            返回主编辑器
          </Link>
          <Link to="/gas-graphs" className="rounded border border-sky-500/40 px-2 py-1 text-sky-300 hover:bg-sky-500/10">
            函数图
          </Link>
          <Link
            to={isBt ? '/fsm-editor' : '/bt-editor'}
            className="rounded border border-violet-500/40 px-2 py-1 text-violet-300 hover:bg-violet-500/10"
          >
            {isBt ? '状态机' : '行为树'}
          </Link>
        </div>
      </header>

      <div className="grid grid-cols-12 gap-0 min-h-[calc(100vh-57px)]">
        <aside className="col-span-3 border-r border-zinc-800 p-3 space-y-3">
          <label className={labelClass}>
            数据源
            <select className={fieldClass} value={sourceId} onChange={(e) => setSourceId(e.target.value)}>
              {sources.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name} ({s.id})
                </option>
              ))}
              {sources.length === 0 ? <option value="core">Core</option> : null}
            </select>
          </label>
          <div className="flex gap-2">
            <button
              type="button"
              className="rounded border border-zinc-600 px-2 py-1 text-xs hover:bg-zinc-900"
              onClick={() => void loadItems(sourceId)}
            >
              重新加载
            </button>
            <button
              type="button"
              className="rounded border border-emerald-500/50 px-2 py-1 text-xs text-emerald-300 hover:bg-emerald-500/10"
              onClick={() => void saveItems(items)}
            >
              保存
            </button>
          </div>
          <div className="space-y-1">
            {items.map((row) => (
              <button
                key={row.id}
                type="button"
                className={`w-full text-left rounded border px-2 py-1.5 text-sm ${
                  selectedId === row.id ? 'border-emerald-400 bg-emerald-500/10' : 'border-zinc-800 bg-zinc-900/60'
                }`}
                onClick={() => {
                  setSelectedId(row.id);
                  setSelectedNodeId('');
                }}
              >
                {row.id}
              </button>
            ))}
          </div>
          <button
            type="button"
            className="w-full rounded border border-dashed border-zinc-600 px-2 py-1.5 text-xs text-zinc-400 hover:bg-zinc-900"
            onClick={() => {
              const id = isBt ? `bt.new.${items.length + 1}` : `hfsm.new.${items.length + 1}`;
              const next = [...items, isBt ? emptyBtTree(id) : emptyHfsm(id)];
              setItems(next);
              setItemsText(JSON.stringify(next, null, 2));
              setSelectedId(id);
              setSelectedNodeId(isBt ? 'root' : 'root');
            }}
          >
            + 新建
          </button>
          <label className="flex items-center gap-2 text-xs text-zinc-500">
            <input type="checkbox" checked={advancedJson} onChange={(e) => setAdvancedJson(e.target.checked)} />
            高级 JSON
          </label>
        </aside>

        <main className="col-span-6 border-r border-zinc-800 p-4 overflow-auto">
          {advancedJson ? (
            <div className="space-y-2">
              <textarea
                className={`${fieldClass} min-h-[60vh] font-mono text-xs`}
                value={itemsText}
                onChange={(e) => setItemsText(e.target.value)}
              />
              <button
                type="button"
                className="rounded border border-emerald-500/50 px-3 py-1.5 text-xs text-emerald-300 hover:bg-emerald-500/10"
                onClick={() => {
                  try {
                    const parsed = JSON.parse(itemsText);
                    if (!Array.isArray(parsed)) throw new Error('必须是数组');
                    void saveItems(parsed);
                  } catch (e: any) {
                    setError(e?.message ?? 'JSON 无效');
                  }
                }}
              >
                从 JSON 保存
              </button>
            </div>
          ) : selected ? (
            isBt ? renderBtTree(selected as BtTree) : renderHfsm(selected as HfsmMachine)
          ) : (
            <div className="text-sm text-zinc-500">左边选一条拓扑</div>
          )}
        </main>

        <aside className="col-span-3 p-4 overflow-auto space-y-4">
          <div>
            <div className="text-xs uppercase tracking-wide text-zinc-500 mb-2">节点</div>
            {inspector()}
          </div>
          {status ? <div className="text-xs text-emerald-400">{status}</div> : null}
          {error ? <div className="text-xs text-rose-400">{error}</div> : null}
        </aside>
      </div>
    </div>
  );
};
