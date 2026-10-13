import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { cn } from '@/lib/utils';

export function NavTab({
  to,
  on,
  children,
  'data-authoring-tool': dataAuthoringTool,
}: {
  to: string;
  on: boolean;
  children: ReactNode;
  'data-authoring-tool'?: string;
}) {
  return (
    <Link
      to={to}
      data-authoring-tool={dataAuthoringTool}
      className={cn(
        'rounded-md px-2 py-1 text-xs',
        on ? 'bg-studio-elevated text-studio-label' : 'text-studio-muted hover:bg-studio-elevated hover:text-studio-label',
      )}
    >
      {children}
    </Link>
  );
}
