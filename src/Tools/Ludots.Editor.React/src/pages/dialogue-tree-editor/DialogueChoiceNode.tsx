import { Handle, Position, type Node, type NodeProps } from '@xyflow/react';
import { STUDIO_THEME } from '../authoring-studio/authoringTheme';
import { choiceHandle, hubWidth, IN_HANDLE, type DialogueChoiceHubData } from './dialogueTreeModel';

export function DialogueChoiceNode({ data, selected }: NodeProps<Node<DialogueChoiceHubData>>) {
  const n = Math.max(data.choices.length, 1);
  const width = hubWidth(data.choices.length);
  return (
    <div
      data-dialogue-node={data.ownerId}
      data-dialogue-kind="choice"
      className={`dialogue-nc-node dialogue-nc-choice ${selected ? 'selected' : ''}`}
      style={{ width }}
    >
      <Handle
        type="target"
        position={Position.Top}
        id={IN_HANDLE}
        isConnectable={false}
        className="!border-studio-bg"
        style={{ background: STUDIO_THEME.blue }}
      />
      <div className="dialogue-nc-head">
        <span>Multiple Choice</span>
      </div>
      <div className="dialogue-nc-choice-row">
        {data.choices.map((choice) => (
          <div key={choice.id} className="dialogue-nc-choice-cell">
            <div className="dialogue-nc-choice-label">{choice.preview || choice.id}</div>
            {choice.hasCondition ? <div className="dialogue-nc-choice-meta">条件</div> : null}
          </div>
        ))}
      </div>
      {data.choices.map((choice, index) => (
        <Handle
          key={choice.id}
          type="source"
          position={Position.Bottom}
          id={choiceHandle(choice.id)}
          className="!border-studio-bg"
          style={{
            background: STUDIO_THEME.yellow,
            left: `${((index + 0.5) / n) * 100}%`,
          }}
        />
      ))}
    </div>
  );
}
