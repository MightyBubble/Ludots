// 结构对照 Vercel AI Elements（MIT）的 Message 组件族，按 studio 主题自写适配。
import type { ReactNode } from 'react';
import type { ToolResult } from './tools';
import { ToolCard } from './ToolCard';

export type AgentMessage =
  | { kind: 'user'; text: string }
  | { kind: 'assistant'; text: string }
  | { kind: 'tool'; result: ToolResult };

export function Message({ message }: { message: AgentMessage }) {
  if (message.kind === 'tool') {
    return <ToolCard result={message.result} />;
  }
  const isUser = message.kind === 'user';
  const body: ReactNode = message.text;
  return (
    <div className={isUser ? 'flex justify-end' : 'flex justify-start'}>
      <div
        className={`max-w-[92%] whitespace-pre-wrap rounded-lg px-3 py-2 text-xs leading-relaxed ${
          isUser
            ? 'bg-studio-blue/20 text-studio-label'
            : 'border border-studio-elevated bg-studio-surface text-studio-secondary'
        }`}
      >
        {body}
      </div>
    </div>
  );
}
