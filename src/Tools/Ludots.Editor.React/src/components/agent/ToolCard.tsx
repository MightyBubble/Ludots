// 结构对照 Vercel AI Elements（MIT）的 Tool 组件族，按 studio 主题自写适配。
import type { ToolResult } from './tools';
import { Badge } from '@/components/ui/Badge';
import { Panel } from '@/components/ui/Panel';

export function ToolCard({ result }: { result: ToolResult }) {
  return (
    <Panel className="p-2">
      <div className="flex items-center gap-2">
        <span className="text-xs font-semibold text-studio-label">{result.title}</span>
        <Badge tone={result.ok ? 'blue' : 'red'}>{result.ok ? '成功' : '失败'}</Badge>
      </div>
      <p className="mt-1 text-xs leading-relaxed text-studio-secondary">{result.summary}</p>
      {result.detail && result.detail.length > 0 ? (
        <ul className="mt-1 space-y-0.5 font-mono text-[10px] text-studio-muted">
          {result.detail.map((line) => (
            <li key={line}>{line}</li>
          ))}
        </ul>
      ) : null}
    </Panel>
  );
}
