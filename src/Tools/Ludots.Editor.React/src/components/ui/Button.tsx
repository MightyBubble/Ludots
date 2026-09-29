import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { cn } from '@/lib/utils';

export type ButtonVariant = 'primary' | 'ghost' | 'danger';
export type ButtonSize = 'sm' | 'md';

const VARIANT_CLASS: Record<ButtonVariant, string> = {
  primary:
    'rounded-md bg-studio-blue px-3 py-1.5 text-sm font-semibold text-studio-label hover:brightness-110 disabled:opacity-50',
  ghost:
    'rounded-md border border-studio-elevated bg-studio-surface px-3 py-1.5 text-sm text-studio-label hover:bg-studio-elevated disabled:opacity-50',
  danger:
    'rounded-md border border-studio-red/50 px-3 py-1.5 text-sm text-studio-red hover:bg-studio-red/10 disabled:opacity-50',
};

export function Button({
  variant = 'ghost',
  size = 'md',
  className,
  children,
  ...rest
}: ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: ButtonVariant;
  size?: ButtonSize;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      className={cn(VARIANT_CLASS[variant], size === 'sm' && 'px-2 py-1 text-xs', className)}
      {...rest}
    >
      {children}
    </button>
  );
}
