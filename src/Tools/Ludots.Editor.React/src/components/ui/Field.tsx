import type { ReactElement } from 'react';
import { cloneElement, isValidElement } from 'react';
import { cn } from '@/lib/utils';

export const fieldControlClass =
  'mt-1 w-full rounded-md border border-studio-elevated bg-studio-bg px-2 py-1.5 text-sm text-studio-label';

/**
 * Label + control in one primitive. The single child element (input / select /
 * textarea) gets the studio field skin injected, so pages never spell the
 * control classes out again.
 */
export function Field({
  label,
  children,
  className,
  hint,
}: {
  label: string;
  children: ReactElement;
  className?: string;
  hint?: string;
}) {
  const control = isValidElement(children)
    ? cloneElement(children as ReactElement<{ className?: string }>, {
        className: cn(fieldControlClass, (children.props as { className?: string }).className),
      })
    : children;
  return (
    <label className={cn('block text-xs text-studio-muted', className)}>
      {label}
      {control}
      {hint ? <span className="mt-1 block text-[10px] text-studio-muted">{hint}</span> : null}
    </label>
  );
}
