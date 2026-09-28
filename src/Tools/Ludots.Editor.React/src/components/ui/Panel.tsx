import type { ReactNode } from 'react';
import { cn } from '@/lib/utils';

export function Panel({
  children,
  className,
  surface = true,
}: {
  children: ReactNode;
  className?: string;
  surface?: boolean;
}) {
  return (
    <div
      className={cn(
        'rounded-md border border-studio-elevated',
        surface && 'bg-studio-surface',
        className,
      )}
    >
      {children}
    </div>
  );
}
