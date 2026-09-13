import { Handle, Position, type Node, type NodeProps } from '@xyflow/react';
import { STUDIO_THEME } from '../authoring-studio/authoringTheme';
import { IN_HANDLE, NEXT_HANDLE, type DialogueSayData } from './dialogueTreeModel';

export function DialogueSayNode({ data, selected }: NodeProps<Node<DialogueSayData>>) {
  const headTone = data.isFinish ? 'finish' : '';
  return (
    <div
      data-dialogue-node={data.nodeId}
      data-dialogue-kind="say"
      className={`dialogue-nc-node dialogue-nc-say ${headTone} ${selected ? 'selected' : ''} ${
        data.missingLine ? 'missing' : ''
      }`}
      style={{ width: 248 }}
    >
      <Handle
        type="target"
        position={Position.Top}
        id={IN_HANDLE}
        className="!border-studio-bg"
        style={{ background: STUDIO_THEME.silver }}
      />
      <div className="dialogue-nc-head">
        <span>Say</span>
        {data.isEntry ? <span className="dialogue-nc-badge">START</span> : null}
        {data.isFinish ? <span className="dialogue-nc-badge">FINISH</span> : null}
      </div>
      <div className="dialogue-nc-body">
        <div className="dialogue-nc-id">{data.nodeId}</div>
        {data.speakerId ? <div className="dialogue-nc-speaker">{data.speakerId}</div> : null}
        <div className="dialogue-nc-line">{data.linePreview || data.lineId || '还没挂台词'}</div>
      </div>
      <Handle
        type="source"
        position={Position.Bottom}
        id={NEXT_HANDLE}
        isConnectable={!data.hasChoices}
        className="!border-studio-bg"
        style={{ background: data.hasChoices ? STUDIO_THEME.blue : data.isFinish ? STUDIO_THEME.red : STUDIO_THEME.blue }}
      />
    </div>
  );
}
