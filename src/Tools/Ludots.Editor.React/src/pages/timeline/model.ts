export type TimeUnit = 'seconds' | 'ticks';
export type ClipShape = 'interval' | 'point';
export type TimelineContextId = 'sequencer' | 'ability-exec' | 'presenter-timer';

export type TimelineLane = {
  id: string;
  label: string;
  colorClass: string;
};

export type TimelineFieldOption = {
  value: string;
  label: string;
};

export type TimelineField = {
  key: string;
  label: string;
  type: 'text' | 'number' | 'select' | 'checkbox';
  step?: number;
  min?: number;
  options?: TimelineFieldOption[];
  visibleWhen?: (payload: Record<string, unknown>) => boolean;
};

export type TimelineClip = {
  id: string;
  laneId: string;
  start: number;
  duration: number;
  shape: ClipShape;
  resizable: boolean;
  movable: boolean;
  label: string;
  badges?: string[];
  payload: Record<string, unknown>;
};

export type TimelineIssue = {
  level: 'error' | 'warning';
  message: string;
  clipId?: string;
};

export type TimelineDocument = {
  id: string;
  displayName: string;
  timeUnit: TimeUnit;
  clockLabel?: string;
  headerFields: TimelineField[];
  headerValues: Record<string, unknown>;
  lanes: TimelineLane[];
  clips: TimelineClip[];
  issues: TimelineIssue[];
  occupancy?: { used: number; max: number };
};

export type TimelinePaletteItem = {
  id: string;
  group: string;
  label: string;
  laneId: string;
  shape: ClipShape;
  defaultDuration: number;
};

export type TimelineMutation =
  | { ok: true; source: unknown }
  | { ok: false; error: string };

export type TimelineClipPatch = {
  start?: number;
  duration?: number;
  laneId?: string;
  payload?: Record<string, unknown>;
};

export type TimelineAdapter = {
  contextId: TimelineContextId;
  label: string;
  blurb: string;
  timeUnit: TimeUnit;
  pixelsPerUnit: number;
  unitLabel: string;
  lanes: TimelineLane[];
  palette: TimelinePaletteItem[];
  clipFields(clip: TimelineClip): TimelineField[];
  project(source: unknown): TimelineDocument;
  applyClipChange(source: unknown, clipId: string, patch: TimelineClipPatch): TimelineMutation;
  addFromPalette(source: unknown, paletteId: string, start: number): TimelineMutation;
  removeClip(source: unknown, clipId: string): TimelineMutation;
  applyHeader(source: unknown, values: Record<string, unknown>): TimelineMutation;
};

export function snapTime(value: number, unit: TimeUnit): number {
  if (!Number.isFinite(value)) return 0;
  if (unit === 'ticks') return Math.max(0, Math.round(value));
  return Math.max(0, Math.round(value * 10) / 10);
}

export function asRecord(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

export function asArray<T>(value: unknown): T[] {
  return Array.isArray(value) ? (value as T[]) : [];
}

export function readString(value: unknown, fallback = ''): string {
  return typeof value === 'string' ? value : fallback;
}

export function readNumber(value: unknown, fallback = 0): number {
  if (typeof value === 'number' && Number.isFinite(value)) return value;
  if (typeof value === 'string' && value.trim() !== '') {
    const parsed = Number(value);
    if (Number.isFinite(parsed)) return parsed;
  }
  return fallback;
}

export function readStringList(value: unknown): string[] {
  if (!Array.isArray(value)) return [];
  return value.filter((item): item is string => typeof item === 'string' && item.length > 0);
}

export function cloneJson<T>(value: T): T {
  return structuredClone(value);
}
