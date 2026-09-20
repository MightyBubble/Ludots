import React from 'react';
import { Handle, Position, type Node, type NodeProps } from '@xyflow/react';
import { STUDIO_THEME } from '../authoring-studio/authoringTheme';

export type TopologyNodeRole = 'composite' | 'leaf' | 'compound' | 'state';

export type TopologyNodeData = {
  label: string;
  kind: string;
  role: TopologyNodeRole;
  subtitle?: string;
  selected?: boolean;
  childCount?: number;
};

const roleBorder: Record<TopologyNodeRole, string> = {
  composite: 'border-studio-blue/70',
  leaf: 'border-studio-red/70',
  compound: 'border-studio-yellow/70',
  state: 'border-studio-yellow/70',
};

const roleHandle: Record<TopologyNodeRole, string> = {
  composite: STUDIO_THEME.blue,
  leaf: STUDIO_THEME.red,
  compound: STUDIO_THEME.yellow,
  state: STUDIO_THEME.yellow,
};

export function TopologyNodeView({ data, selected }: NodeProps<Node<TopologyNodeData>>) {
  const isComposite = data.role === 'composite' || data.role === 'compound';
  return (
    <div
      className={`min-w-[160px] max-w-[220px] rounded-lg border bg-studio-surface px-3 py-2 ${
        roleBorder[data.role] ?? 'border-studio-elevated'
      } ${selected ? 'ring-1 ring-studio-blue/80' : ''}`}
    >
      <Handle
        type="target"
        position={Position.Top}
        id="in"
        className="!h-2.5 !w-2.5 !border-studio-bg"
        style={{ background: STUDIO_THEME.silver }}
      />
      <div className="text-[10px] uppercase tracking-wide text-studio-muted">{data.kind}</div>
      <div className="truncate font-mono text-sm text-studio-label">{data.label}</div>
      {data.subtitle ? (
        <div className="mt-1 truncate text-[11px] text-studio-secondary">{data.subtitle}</div>
      ) : null}
      {isComposite ? (
        <div className="mt-1 text-[10px] text-studio-muted">
          子节点 {data.childCount ?? 0} · 从下方拖出连线
        </div>
      ) : (
        <div className="mt-1 text-[10px] text-studio-muted">双击打开叶子函数图</div>
      )}
      <Handle
        type="source"
        position={Position.Bottom}
        id="out"
        className="!h-2.5 !w-2.5 !border-studio-bg"
        style={{ background: roleHandle[data.role] ?? STUDIO_THEME.blue }}
      />
    </div>
  );
}
