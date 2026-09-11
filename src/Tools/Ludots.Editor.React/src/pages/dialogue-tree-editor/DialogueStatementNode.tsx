import { Handle, Position, type Node, type NodeProps } from '@xyflow/react';
import { STUDIO_THEME } from '../authoring-studio/authoringTheme';
import { choiceHandle, IN_HANDLE, NEXT_HANDLE, type DialogueStatementData } from './dialogueTreeModel';

export function DialogueStatementNode({ data, selected }: NodeProps<Node<DialogueStatementData>>) {
  const choiceCount = data.choiceIds.length;
  return (
    <div
      data-dialogue-node={data.nodeId}
      className={`min-w-[220px] max-w-[280px] rounded-lg border bg-studio-surface ${
        selected ? 'border-studio-blue ring-1 ring-studio-blue/70' : 'border-studio-elevated'
      } ${data.missingLine ? 'border-studio-red' : ''}`}
    >
      <Handle
        type="target"
        position={Position.Top}
        id={IN_HANDLE}
        className="!h-2.5 !w-2.5 !border-studio-bg"
        style={{ background: STUDIO_THEME.silver }}
      />
      <div className="flex items-center justify-between px-3 pt-2">
        <span className="text-[10px] uppercase tracking-wide text-studio-muted">
          {data.isEntry ? '入口 · 说话' : '说话'}
        </span>
        {data.isEntry ? (
          <span className="rounded bg-studio-blue/20 px-1.5 py-0.5 text-[10px] text-studio-blue">START</span>
        ) : null}
      </div>
      <div className="truncate px-3 pt-1 font-mono text-sm text-studio-label">{data.nodeId}</div>
      {data.speakerId ? (
        <div className="truncate px-3 text-[11px] text-studio-blue">{data.speakerId}</div>
      ) : null}
      <div className="px-3 pb-2 pt-1 text-[12px] leading-snug text-studio-secondary">
        {data.linePreview || data.lineId || '还没挂台词'}
      </div>
      {choiceCount > 0 ? (
        <div className="space-y-1 border-t border-studio-elevated px-3 py-2">
          {data.choiceIds.map((choiceId) => (
            <div key={choiceId} className="pr-2 text-[11px] text-studio-yellow">
              {choiceId}
            </div>
          ))}
        </div>
      ) : (
        <div className="px-3 pb-2 text-[10px] text-studio-muted">无选项 · 从下方接下句，或结束</div>
      )}
      {data.choiceIds.map((choiceId, index) => (
        <Handle
          key={choiceId}
          type="source"
          position={Position.Right}
          id={choiceHandle(choiceId)}
          className="!h-2.5 !w-2.5 !border-studio-bg"
          style={{
            background: STUDIO_THEME.yellow,
            top: `${62 + index * Math.max(10, 28 / Math.max(choiceCount, 1))}%`,
          }}
        />
      ))}
      <Handle
        type="source"
        position={Position.Bottom}
        id={NEXT_HANDLE}
        className="!h-2.5 !w-2.5 !border-studio-bg"
        style={{ background: STUDIO_THEME.blue }}
      />
    </div>
  );
}
