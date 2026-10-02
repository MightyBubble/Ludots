import type { ReactNode } from 'react';
import { cn } from '@/lib/utils';

export type BadgeTone = 'blue' | 'red' | 'yellow' | 'muted';

const TONE_CLASS: Record<BadgeTone, string> = {
  blue: 'bg-studio-blue/20 text-studio-blue',
  red: 'bg-studio-red/15 text-studio-red',
  yellow: 'bg-studio-yellow/15 text-studio-yellow',
  muted: 'bg-studio-elevated text-studio-muted',
};

export function Badge({
  tone = 'muted',
  children,
  className,
  ...rest
}: {
  tone?: BadgeTone;
  children: ReactNode;
  className?: string;
} & Record<string, unknown>) {
  return (
    <span className={cn('shrink-0 rounded-md px-2 py-0.5 text-[10px]', TONE_CLASS[tone], className)} {...rest}>
      {children}
    </span>
  );
}
