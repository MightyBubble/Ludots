// 结构对照 Vercel AI Elements（MIT）的 Conversation 组件族，按 studio 主题自写适配。
import { useEffect, useRef, useState } from 'react';
import { type AgentMessage, Message } from './Message';

export type { AgentMessage };
import { Button } from '@/components/ui/Button';

export function Conversation({ messages }: { messages: AgentMessage[] }) {
  const scrollRef = useRef<HTMLDivElement | null>(null);
  const [pinned, setPinned] = useState(true);

  useEffect(() => {
    const node = scrollRef.current;
    if (node && pinned) node.scrollTop = node.scrollHeight;
  }, [messages, pinned]);

  const onScroll = () => {
    const node = scrollRef.current;
    if (!node) return;
    setPinned(node.scrollHeight - node.scrollTop - node.clientHeight < 24);
  };

  return (
    <div className="relative min-h-0 flex-1">
      <div ref={scrollRef} onScroll={onScroll} className="h-full space-y-2 overflow-auto pr-1">
        {messages.length === 0 ? (
          <p className="px-1 py-6 text-xs leading-relaxed text-studio-muted">
            还没开始。下面发一句，或在「工具」页直接跑编辑器工具——不配模型也能用。
          </p>
        ) : (
          messages.map((message, index) => <Message key={index} message={message} />)
        )}
      </div>
      {!pinned ? (
        <div className="absolute bottom-2 right-3">
          <Button
            size="sm"
            onClick={() => {
              setPinned(true);
              const node = scrollRef.current;
              if (node) node.scrollTop = node.scrollHeight;
            }}
          >
            回到底部
          </Button>
        </div>
      ) : null}
    </div>
  );
}
