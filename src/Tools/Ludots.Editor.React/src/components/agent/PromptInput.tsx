// 结构对照 Vercel AI Elements（MIT）的 PromptInput 组件族，按 studio 主题自写适配。
import type { ReactNode } from 'react';
import { Button } from '@/components/ui/Button';

export function PromptInput({
  value,
  onChange,
  onSubmit,
  busy,
  disabled,
  placeholder,
  footer,
}: {
  value: string;
  onChange: (next: string) => void;
  onSubmit: () => void;
  busy?: boolean;
  disabled?: boolean;
  placeholder?: string;
  footer?: ReactNode;
}) {
  return (
    <div className="shrink-0 space-y-2">
      <textarea
        className="mt-1 w-full rounded-md border border-studio-elevated bg-studio-bg px-2 py-1.5 text-xs leading-relaxed text-studio-label"
        rows={3}
        value={value}
        placeholder={placeholder}
        onChange={(e) => onChange(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
            e.preventDefault();
            onSubmit();
          }
        }}
      />
      <div className="flex items-center gap-2">
        <Button variant="primary" size="sm" disabled={busy || disabled || value.trim().length === 0} onClick={onSubmit}>
          {busy ? '在想…' : '发送'}
        </Button>
        {footer}
      </div>
    </div>
  );
}
