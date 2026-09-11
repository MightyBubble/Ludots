/**
 * 作者工作室画布皮：苹果 HIG Dark Mode 的银底 + 红 / 黄 / 蓝。
 * 色值是 HIG 文档给设计参考的 Dark 系统色（systemGray 阶、systemRed/Yellow/Blue），
 * 不是另一套品牌彩虹。语义固定：蓝=结构/数据/主操作，黄=控制流/时间，红=事件/动作/危险。
 */
export type StudioAccent = 'blue' | 'yellow' | 'red';

export const STUDIO_THEME = {
  bg: '#1c1c1e',
  surface: '#2c2c2e',
  elevated: '#3a3a3c',
  fill: '#48484a',
  label: '#f5f5f7',
  secondary: 'rgba(235, 235, 245, 0.6)',
  muted: '#8e8e93',
  separator: 'rgba(84, 84, 88, 0.65)',
  silver: '#8e8e93',
  red: '#ff453a',
  yellow: '#ffd60a',
  blue: '#0a84ff',
  onYellow: '#1c1c1e',
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
    'rounded-md bg-studio-blue px-3 py-1.5 text-sm font-semibold text-white hover:brightness-110 disabled:opacity-50',
  btnGhost:
    'rounded-md border border-studio-elevated bg-studio-surface px-3 py-1.5 text-sm text-studio-label hover:bg-studio-elevated',
  btnDanger: 'rounded-md border border-studio-red/50 px-3 py-1.5 text-sm text-studio-red hover:bg-studio-red/10',
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
