import { useEffect, useRef } from 'react';
import { cssToArgbHex, parseTemplatePreview, wrapSelection, type PreviewRun } from './textBankModel';
import './textBank.css';

export function RunSpan({ run }: { run: PreviewRun }) {
  if (run.placeholder !== undefined) {
    return <span className="text-bank-run-placeholder">{run.text}</span>;
  }
  const style: React.CSSProperties = {};
  if (run.bold) style.fontWeight = 700;
  if (run.italic) style.fontStyle = 'italic';
  if (run.color) style.color = run.color;
  return <span style={style}>{run.text}</span>;
}

/**
 * Shared restricted-markup editor: toolbar (B / I / color) wraps the current
 * textarea selection with engine markup; preview renders styled runs with the
 * same parser the loader uses. One component for the text bank cells and the
 * dialogue inline statement editor so markup UX cannot drift between rooms.
 */
export function RichTextArea({
  value,
  onChange,
  rows = 4,
  placeholder,
  ariaLabel,
  autoFocusToEnd = false,
  onCommitHotkey,
  onCancel,
  onBlur,
}: {
  value: string;
  onChange: (next: string) => void;
  rows?: number;
  placeholder?: string;
  ariaLabel?: string;
  autoFocusToEnd?: boolean;
  onCommitHotkey?: () => void;
  onCancel?: () => void;
  onBlur?: () => void;
}) {
  const ref = useRef<HTMLTextAreaElement | null>(null);
  const lastColor = useRef('#FFF6C56B');

  useEffect(() => {
    if (!autoFocusToEnd) return;
    const node = ref.current;
    if (!node) return;
    node.focus();
    node.setSelectionRange(node.value.length, node.value.length);
  }, [autoFocusToEnd]);

  const applyMarkup = (kind: 'b' | 'i' | 'color', colorCss?: string) => {
    const node = ref.current;
    if (!node) return;
    const argb = kind === 'color' ? cssToArgbHex(colorCss ?? '') : undefined;
    if (kind === 'color') {
      if (!colorCss || !argb) return;
      lastColor.current = colorCss;
    }
    const next = wrapSelection(value, node.selectionStart, node.selectionEnd, kind, argb ?? undefined);
    onChange(next.text);
    requestAnimationFrame(() => {
      node.setSelectionRange(next.selectionStart, next.selectionEnd);
      node.focus();
    });
  };

  return (
    <div>
      <div className="text-bank-markup-bar">
        <button type="button" title="加粗" onClick={() => applyMarkup('b')}>
          B
        </button>
        <button type="button" title="斜体" onClick={() => applyMarkup('i')}>
          I
        </button>
        <input
          type="color"
          title="给选中文字上色"
          value={lastColor.current}
          onChange={(e) => applyMarkup('color', e.target.value)}
        />
      </div>
      <textarea
        ref={ref}
        className="text-bank-editor"
        style={{ height: `${rows * 1.75}rem` }}
        aria-label={ariaLabel}
        value={value}
        spellCheck={false}
        placeholder={placeholder}
        onChange={(e) => onChange(e.target.value)}
        onBlur={onBlur}
        onKeyDown={(e) => {
          if (e.key === 'Escape' && onCancel) {
            e.preventDefault();
            onCancel();
          }
          if (e.key === 'Enter' && (e.ctrlKey || e.metaKey) && onCommitHotkey) {
            e.preventDefault();
            onCommitHotkey();
          }
        }}
      />
    </div>
  );
}

export function MarkupPreview({ source, argCount = 0 }: { source: string; argCount?: number }) {
  if (!source.trim()) {
    return <p className="text-[10px] text-studio-muted">预览：还没写正文。</p>;
  }
  const preview = parseTemplatePreview(source, argCount);
  if (preview.error) {
    return <p className="text-[10px] text-studio-red">预览：{preview.error}</p>;
  }
  return (
    <p className="text-xs leading-relaxed text-studio-secondary">
      预览：
      {preview.runs.map((run, index) => (
        <RunSpan key={index} run={run} />
      ))}
    </p>
  );
}
