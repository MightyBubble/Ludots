import type { ReactNode } from 'react';
import { cn } from '@/lib/utils';

export function Collapse({
  summary,
  children,
  className,
}: {
  summary: string;
  children: ReactNode;
  className?: string;
}) {
  return (
    <details className={cn('rounded-md border border-studio-elevated px-2 py-1.5', className)}>
      <summary className="cursor-pointer text-xs text-studio-muted">{summary}</summary>
      <div className="mt-2 space-y-2">{children}</div>
    </details>
  );
}
