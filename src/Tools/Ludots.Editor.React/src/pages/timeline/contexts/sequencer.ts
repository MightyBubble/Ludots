import {
  asArray,
  asRecord,
  cloneJson,
  readNumber,
  readString,
  type TimelineAdapter,
  type TimelineClip,
  type TimelineDocument,
  type TimelineField,
  type TimelineLane,
  type TimelineMutation,
  type TimelinePaletteItem,
} from '../model.ts';

export type SequencerTrackRow = {
  type: string;
  profile?: string;
  lineId?: string;
  presentationProfile?: string;
  eventId?: string;
  actionGraphId?: string;
  start: number;
  duration?: number;
};

export type SequencerSource = {
  id: string;
  displayName?: string;
  clearCameraOnComplete?: boolean;
  clock?: { rate?: number; pausePolicy?: string };
  tracks: SequencerTrackRow[];
};

const LANES: TimelineLane[] = [
  { id: 'Camera', label: '镜头 Camera', colorClass: 'bg-sky-500/80 border-sky-300' },
  { id: 'Subtitle', label: '字幕 Subtitle', colorClass: 'bg-amber-500/80 border-amber-200' },
  { id: 'Signal', label: '信号 Signal', colorClass: 'bg-rose-500/70 border-rose-200' },
];

const PALETTE: TimelinePaletteItem[] = [
  { id: 'Camera', group: '轨道', label: '镜头', laneId: 'Camera', shape: 'interval', defaultDuration: 2 },
  { id: 'Subtitle', group: '轨道', label: '字幕', laneId: 'Subtitle', shape: 'interval', defaultDuration: 1.8 },
  { id: 'Signal', group: '轨道', label: '信号', laneId: 'Signal', shape: 'point', defaultDuration: 0.35 },
];

const TRACK_TYPES = new Set(['Camera', 'Subtitle', 'Signal']);

export function clipIdForTrack(index: number): string {
  return `track:${index}`;
}

export function trackIndexFromClipId(clipId: string): number | null {
  if (!clipId.startsWith('track:')) return null;
  const index = Number(clipId.slice('track:'.length));
  return Number.isInteger(index) && index >= 0 ? index : null;
}

function requireSource(source: unknown): SequencerSource | null {
  const record = asRecord(source);
  if (!record) return null;
  return {
    id: readString(record.id),
    displayName: readString(record.displayName, readString(record.id)),
    clearCameraOnComplete: record.clearCameraOnComplete !== false,
    clock: asRecord(record.clock)
      ? {
          rate: readNumber(asRecord(record.clock)?.rate, 1),
          pausePolicy: readString(asRecord(record.clock)?.pausePolicy, 'Independent'),
        }
      : { rate: 1, pausePolicy: 'Independent' },
    tracks: asArray<Record<string, unknown>>(record.tracks).map((track) => ({
      type: readString(track.type, 'Camera'),
      profile: readString(track.profile) || undefined,
      lineId: readString(track.lineId) || undefined,
      presentationProfile: readString(track.presentationProfile) || undefined,
      eventId: readString(track.eventId) || undefined,
      actionGraphId: readString(track.actionGraphId) || undefined,
      start: readNumber(track.start, 0),
      duration: track.duration === undefined ? undefined : readNumber(track.duration, 0),
    })),
  };
}

function trackLabel(track: SequencerTrackRow): string {
  if (track.type === 'Camera') return track.profile || '镜头';
  if (track.type === 'Subtitle') return track.lineId || '字幕';
  return track.eventId || track.actionGraphId || '信号';
}

function visualDuration(track: SequencerTrackRow): number {
  if (track.type === 'Signal') return 0.35;
  return Math.max(0.2, readNumber(track.duration, 0.2));
}

function clipFields(clip: TimelineClip): TimelineField[] {
  const type = readString(clip.payload.type, 'Camera');
  return [
    {
      key: 'type',
      label: '类型',
      type: 'select',
      options: [
        { value: 'Camera', label: 'Camera 镜头' },
        { value: 'Subtitle', label: 'Subtitle 字幕' },
        { value: 'Signal', label: 'Signal 信号' },
      ],
    },
    { key: 'start', label: '开始秒', type: 'number', step: 0.1, min: 0 },
    {
      key: 'duration',
      label: '持续秒',
      type: 'number',
      step: 0.1,
      min: 0.2,
      visibleWhen: (payload) => readString(payload.type) !== 'Signal',
    },
    { key: 'profile', label: '镜头配置', type: 'text', visibleWhen: () => type === 'Camera' },
    { key: 'lineId', label: '台词 ID', type: 'text', visibleWhen: () => type === 'Subtitle' },
    { key: 'presentationProfile', label: '表现配置', type: 'text', visibleWhen: () => type === 'Subtitle' },
    { key: 'eventId', label: '事件 ID', type: 'text', visibleWhen: () => type === 'Signal' },
    { key: 'actionGraphId', label: '动作图', type: 'text', visibleWhen: () => type === 'Signal' },
  ];
}

function writeTrack(track: SequencerTrackRow, payload: Record<string, unknown>): SequencerTrackRow {
  const type = TRACK_TYPES.has(readString(payload.type)) ? readString(payload.type) : track.type;
  const next: SequencerTrackRow = {
    type,
    start: readNumber(payload.start, track.start),
  };
  if (type === 'Signal') {
    next.eventId = readString(payload.eventId) || undefined;
    next.actionGraphId = readString(payload.actionGraphId) || undefined;
    return next;
  }
  next.duration = readNumber(payload.duration, track.duration ?? 0.2);
  if (type === 'Camera') {
    next.profile = readString(payload.profile) || undefined;
    return next;
  }
  next.lineId = readString(payload.lineId) || undefined;
  next.presentationProfile = readString(payload.presentationProfile) || undefined;
  return next;
}

export function projectSequencer(source: unknown): TimelineDocument {
  const row = requireSource(source) ?? { id: '', displayName: '', tracks: [] };
  const issues = [];
  if (row.tracks.length === 0) {
    issues.push({ level: 'error' as const, message: '演出序列至少要有一条轨道。' });
  }
  for (const [index, track] of row.tracks.entries()) {
    if (!TRACK_TYPES.has(track.type)) {
      issues.push({ level: 'error' as const, message: `轨道 ${index} 类型 ${track.type} 不在 Camera / Subtitle / Signal。`, clipId: clipIdForTrack(index) });
    }
    if (track.type !== 'Signal' && !(readNumber(track.duration, 0) > 0)) {
      issues.push({ level: 'error' as const, message: `轨道 ${index} 的持续秒必须大于 0。`, clipId: clipIdForTrack(index) });
    }
    if (track.type === 'Camera' && !track.profile) {
      issues.push({ level: 'error' as const, message: `镜头轨道 ${index} 缺少镜头配置。`, clipId: clipIdForTrack(index) });
    }
    if (track.type === 'Subtitle' && (!track.lineId || !track.presentationProfile)) {
      issues.push({ level: 'error' as const, message: `字幕轨道 ${index} 需要台词 ID 和表现配置。`, clipId: clipIdForTrack(index) });
    }
    if (track.type === 'Signal' && !track.actionGraphId) {
      issues.push({ level: 'error' as const, message: `信号轨道 ${index} 缺少动作图。`, clipId: clipIdForTrack(index) });
    }
  }

  return {
    id: row.id,
    displayName: row.displayName || row.id,
    timeUnit: 'seconds',
    clockLabel: `倍率 ${row.clock?.rate ?? 1}`,
    headerFields: [
      { key: 'id', label: '演出 ID', type: 'text' },
      { key: 'displayName', label: '显示名', type: 'text' },
      { key: 'rate', label: '时钟倍率', type: 'number', step: 0.1, min: 0.0001 },
      { key: 'clearCameraOnComplete', label: '结束时清镜头', type: 'checkbox' },
    ],
    headerValues: {
      id: row.id,
      displayName: row.displayName ?? '',
      rate: row.clock?.rate ?? 1,
      clearCameraOnComplete: row.clearCameraOnComplete !== false,
    },
    lanes: LANES,
    clips: row.tracks.map((track, index) => ({
      id: clipIdForTrack(index),
      laneId: TRACK_TYPES.has(track.type) ? track.type : 'Camera',
      start: Math.max(0, track.start),
      duration: visualDuration(track),
      shape: track.type === 'Signal' ? 'point' : 'interval',
      resizable: track.type !== 'Signal',
      movable: true,
      label: trackLabel(track),
      payload: { ...track },
    })),
    issues,
  };
}

export function applySequencerClipChange(source: unknown, clipId: string, patch: { start?: number; duration?: number; laneId?: string; payload?: Record<string, unknown> }): TimelineMutation {
  const row = requireSource(source);
  if (!row) return { ok: false, error: '演出序列源不是对象。' };
  const index = trackIndexFromClipId(clipId);
  if (index === null || !row.tracks[index]) return { ok: false, error: `找不到轨道 ${clipId}。` };
  const current = row.tracks[index];
  const payload = {
    ...current,
    ...(patch.payload ?? {}),
    start: patch.start ?? readNumber(patch.payload?.start, current.start),
    duration: patch.duration ?? readNumber(patch.payload?.duration, current.duration ?? 0.2),
    type: patch.laneId ?? readString(patch.payload?.type, current.type),
  };
  const next = cloneJson(row);
  next.tracks[index] = writeTrack(current, payload);
  return { ok: true, source: next };
}

export function addSequencerFromPalette(source: unknown, paletteId: string, start: number): TimelineMutation {
  const row = requireSource(source);
  if (!row) return { ok: false, error: '演出序列源不是对象。' };
  if (!TRACK_TYPES.has(paletteId)) return { ok: false, error: `未知轨道类型 ${paletteId}。` };
  const next = cloneJson(row);
  const track: SequencerTrackRow = { type: paletteId, start: Math.max(0, start) };
  if (paletteId === 'Camera') {
    track.profile = '';
    track.duration = 2;
  } else if (paletteId === 'Subtitle') {
    track.lineId = '';
    track.presentationProfile = 'story.immersive_subtitle';
    track.duration = 1.8;
  } else {
    track.eventId = '';
    track.actionGraphId = '';
  }
  next.tracks.push(track);
  return { ok: true, source: next };
}

export function removeSequencerClip(source: unknown, clipId: string): TimelineMutation {
  const row = requireSource(source);
  if (!row) return { ok: false, error: '演出序列源不是对象。' };
  const index = trackIndexFromClipId(clipId);
  if (index === null || !row.tracks[index]) return { ok: false, error: `找不到轨道 ${clipId}。` };
  const next = cloneJson(row);
  next.tracks.splice(index, 1);
  return { ok: true, source: next };
}

export function applySequencerHeader(source: unknown, values: Record<string, unknown>): TimelineMutation {
  const row = requireSource(source);
  if (!row) return { ok: false, error: '演出序列源不是对象。' };
  const next = cloneJson(row);
  next.id = readString(values.id, row.id);
  next.displayName = readString(values.displayName, row.displayName ?? '');
  next.clearCameraOnComplete = values.clearCameraOnComplete !== false;
  next.clock = {
    rate: Math.max(0.0001, readNumber(values.rate, row.clock?.rate ?? 1)),
    pausePolicy: row.clock?.pausePolicy ?? 'Independent',
  };
  return { ok: true, source: next };
}

export const sequencerAdapter: TimelineAdapter = {
  contextId: 'sequencer',
  label: '演出序列',
  blurb: '镜头、字幕、到点信号排在同一条秒轴上。',
  timeUnit: 'seconds',
  pixelsPerUnit: 96,
  unitLabel: 's',
  lanes: LANES,
  palette: PALETTE,
  clipFields,
  project: projectSequencer,
  applyClipChange: applySequencerClipChange,
  addFromPalette: addSequencerFromPalette,
  removeClip: removeSequencerClip,
  applyHeader: applySequencerHeader,
};
