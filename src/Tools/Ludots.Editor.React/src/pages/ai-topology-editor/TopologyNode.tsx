import React from 'react';
import { Handle, Position, type Node, type NodeProps } from '@xyflow/react';

export type TopologyNodeRole = 'composite' | 'leaf' | 'compound' | 'state';

export type TopologyNodeData = {
  label: string;
  kind: string;
  role: TopologyNodeRole;
  subtitle?: string;
  selected?: boolean;
  childCount?: number;
};

const roleTone: Record<TopologyNodeRole, string> = {
  composite: 'border-violet-400/70 bg-violet-950/80',
  leaf: 'border-sky-400/70 bg-sky-950/80',
  compound: 'border-fuchsia-400/70 bg-fuchsia-950/80',
  state: 'border-amber-400/70 bg-amber-950/80',
};

export function TopologyNodeView({ data, selected }: NodeProps<Node<TopologyNodeData>>) {
  const tone = roleTone[data.role] ?? roleTone.leaf;
  const isComposite = data.role === 'composite' || data.role === 'compound';
  return (
    <div
      className={`min-w-[160px] max-w-[220px] rounded-md border px-3 py-2 shadow-lg ${tone} ${
        selected ? 'ring-2 ring-emerald-400/80' : ''
      }`}
    >
      <Handle
        type="target"
        position={Position.Top}
        id="in"
        className="!h-2.5 !w-2.5 !border-slate-900 !bg-slate-200"
      />
      <div className="text-[10px] uppercase tracking-wide text-slate-400">{data.kind}</div>
      <div className="truncate font-mono text-sm text-slate-50">{data.label}</div>
      {data.subtitle ? (
        <div className="mt-1 truncate text-[11px] text-sky-200/90">{data.subtitle}</div>
      ) : null}
      {isComposite ? (
        <div className="mt-1 text-[10px] text-slate-500">
          子节点 {data.childCount ?? 0} · 从下方拖出连线
        </div>
      ) : (
        <div className="mt-1 text-[10px] text-slate-500">双击打开叶子函数图</div>
      )}
      <Handle
        type="source"
        position={Position.Bottom}
        id="out"
        className="!h-2.5 !w-2.5 !border-slate-900 !bg-emerald-300"
      />
    </div>
  );
}
