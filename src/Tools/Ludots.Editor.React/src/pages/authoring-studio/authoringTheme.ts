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

export function diskSaveStatus(path: string): string {
  return `已写入 ${path}。正在玩的局要重开才会按这份走。`;
}

export const TOOL_ACCENT: Record<string, StudioAccent> = {
  blueprint: 'blue',
  bt: 'red',
  fsm: 'yellow',
  dialogue: 'blue',
  timeline: 'yellow',
  map: 'blue',
  panels: 'yellow',
  run: 'red',
};
