/**
 * 工作室画布皮。色值只在 index.css 的 --studio-*。
 * 灰阶是 shadcn zinc dark，语义色是同一张表的 chart-1 / chart-3 / chart-5。
 * 蓝=结构/数据/主操作，黄=控制流/时间，红=事件/结束/危险。
 */
export type StudioAccent = 'blue' | 'yellow' | 'red';

export const STUDIO_THEME = {
  bg: 'var(--studio-bg)',
  surface: 'var(--studio-surface)',
  elevated: 'var(--studio-elevated)',
  fill: 'var(--studio-fill)',
  label: 'var(--studio-label)',
  secondary: 'var(--studio-secondary)',
  muted: 'var(--studio-muted)',
  separator: 'var(--studio-separator)',
  silver: 'var(--studio-muted)',
  red: 'var(--studio-red)',
  yellow: 'var(--studio-yellow)',
  blue: 'var(--studio-blue)',
  onYellow: 'var(--studio-bg)',
} as const;

export const STUDIO_ROLE = {
  structure: STUDIO_THEME.blue,
  control: STUDIO_THEME.yellow,
  event: STUDIO_THEME.red,
} as const;

export const STUDIO_CHROME = {
  page: 'h-full bg-studio-bg text-studio-label',
  field:
    'mt-1 w-full rounded-md border border-studio-elevated bg-studio-bg px-2 py-1.5 text-sm text-studio-label',
  label: 'block text-xs text-studio-muted',
  btnPrimary:
    'rounded-md bg-studio-blue px-3 py-1.5 text-sm font-semibold text-studio-label hover:brightness-110 disabled:opacity-50',
  btnGhost:
    'rounded-md border border-studio-elevated bg-studio-surface px-3 py-1.5 text-sm text-studio-label hover:bg-studio-elevated',
  btnDanger: 'rounded-md border border-studio-red/50 px-3 py-1.5 text-sm text-studio-red hover:bg-studio-red/10 disabled:opacity-50',
  navOn: 'rounded-md bg-studio-elevated px-2 py-1 text-xs text-studio-label',
  navOff: 'rounded-md px-2 py-1 text-xs text-studio-muted hover:bg-studio-elevated hover:text-studio-label',
} as const;

export function diskSaveStatus(path: string): string {
  return `已写入 ${path}。正在玩的局要重开才会按这份走。`;
}

export const TOOL_ACCENT: Record<string, StudioAccent> = {
  blueprint: 'blue',
  bt: 'red',
  fsm: 'yellow',
  dialogue: 'blue',
  timeline: 'yellow',
};
