import type { ReactNode } from 'react';
import { cn } from '@/lib/utils';

/**
 * 版式合同：每个房间的骨架只有一种答案。
 * 标题行（h-12，标题+说明+动作靠右）｜左 rail（默认 264px，可调宽）｜主区｜
 * 右检查器（默认 320px）｜底部状态条（status 蓝 / error 红，全编辑器同一位置）。
 * 状态与错误必须走 footer，不许塞标题行，不许 window.alert。
 */
export function WorkspaceLayout({
  title,
  blurb,
  actions,
  rail,
  railWidthClass = 'w-64',
  inspector,
  inspectorWidthClass = 'w-80',
  status,
  error,
  children,
  bodyClassName,
}: {
  title: string;
  blurb?: string;
  actions?: ReactNode;
  rail?: ReactNode;
  railWidthClass?: string;
  inspector?: ReactNode;
  inspectorWidthClass?: string;
  status?: string;
  error?: string;
  children: ReactNode;
  bodyClassName?: string;
}) {
  return (
    <div data-workspace-layout={title} className="flex h-full flex-col bg-studio-bg text-studio-label">
      <div className="flex h-12 shrink-0 items-center gap-3 border-b border-studio-elevated bg-studio-surface px-4">
        <h1 className="shrink-0 text-sm font-semibold text-studio-label">{title}</h1>
        {blurb ? <span className="min-w-0 truncate text-xs text-studio-muted">{blurb}</span> : null}
        <div className="ml-auto flex shrink-0 items-center gap-2">{actions}</div>
      </div>
      <div className="flex min-h-0 flex-1">
        {rail ? (
          <aside className={cn('flex shrink-0 flex-col overflow-auto border-r border-studio-elevated bg-studio-surface p-3', railWidthClass)}>
            {rail}
          </aside>
        ) : null}
        <main className={cn('min-h-0 min-w-0 flex-1 overflow-hidden', bodyClassName)}>{children}</main>
        {inspector ? (
          <aside className={cn('flex shrink-0 flex-col overflow-auto border-l border-studio-elevated bg-studio-surface p-3', inspectorWidthClass)}>
            {inspector}
          </aside>
        ) : null}
      </div>
      <footer className="flex h-7 shrink-0 items-center gap-4 border-t border-studio-elevated bg-studio-surface px-4 text-[11px]">
        <span className="min-w-0 truncate text-studio-blue">{status}</span>
        <span className="min-w-0 truncate text-studio-red">{error}</span>
      </footer>
    </div>
  );
}
