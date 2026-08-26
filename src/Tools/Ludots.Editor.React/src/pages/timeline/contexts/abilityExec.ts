import {
  asArray,
  asRecord,
  cloneJson,
  readNumber,
  readString,
  readStringList,
  type TimelineAdapter,
  type TimelineClip,
  type TimelineDocument,
  type TimelineField,
  type TimelineIssue,
  type TimelineLane,
  type TimelineMutation,
  type TimelinePaletteItem,
} from '../model.ts';

export const ABILITY_EXEC_MAX_ITEMS = 16;

export type AbilityExecItem = {
  kind: string;
  tick: number;
  duration?: number;
  durationTicks?: number;
  clockId?: string;
  tag?: string;
  template?: string;
  callerParamsIdx?: number;
  payloadA?: number;
  dispatchTarget?: string;
};

export type AbilityExecSource = {
  id: string;
  presentation?: { displayName?: string };
  exec?: {
    clockId?: string;
    interruptAny?: string[];
    callerParams?: unknown[];
    items?: AbilityExecItem[];
  };
  [key: string]: unknown;
};

const LANE_BY_KIND: Record<string, string> = {
  EffectClip: 'clip',
  TagClip: 'clip',
  TagClipTarget: 'clip',
  EffectSignal: 'signal',
  EventSignal: 'signal',
  TagSignal: 'signal',
  TagSignalTarget: 'signal',
  InputGate: 'gate',
  EventGate: 'gate',
  TargetCollectionGate: 'gate',
  End: 'end',
};

const LANES: TimelineLane[] = [
  { id: 'clip', label: '持续 Clip', colorClass: 'bg-sky-500/80 border-sky-300' },
  { id: 'signal', label: '瞬发 Signal', colorClass: 'bg-amber-500/80 border-amber-200' },
  { id: 'gate', label: '等待 Gate', colorClass: 'bg-violet-500/80 border-violet-200' },
  { id: 'end', label: '收束 End', colorClass: 'bg-rose-500/70 border-rose-200' },
];

const KIND_LABEL: Record<string, string> = {
  EffectClip: '效果持续',
  TagClip: '自身标记持续',
  TagClipTarget: '目标标记持续',
  EffectSignal: '效果瞬发',
  EventSignal: '事件瞬发',
  TagSignal: '自身标记',
  TagSignalTarget: '目标标记',
  InputGate: '等玩家确认',
  EventGate: '等事件',
  TargetCollectionGate: '等目标收集',
  End: '收束',
};

const PALETTE: TimelinePaletteItem[] = [
  { id: 'EffectClip', group: '持续', label: '效果持续', laneId: 'clip', shape: 'interval', defaultDuration: 30 },
  { id: 'TagClip', group: '持续', label: '自身标记持续', laneId: 'clip', shape: 'interval', defaultDuration: 30 },
  { id: 'TagClipTarget', group: '持续', label: '目标标记持续', laneId: 'clip', shape: 'interval', defaultDuration: 30 },
  { id: 'EffectSignal', group: '瞬发', label: '效果瞬发', laneId: 'signal', shape: 'point', defaultDuration: 4 },
  { id: 'EventSignal', group: '瞬发', label: '事件瞬发', laneId: 'signal', shape: 'point', defaultDuration: 4 },
  { id: 'TagSignal', group: '瞬发', label: '自身标记', laneId: 'signal', shape: 'point', defaultDuration: 4 },
  { id: 'TagSignalTarget', group: '瞬发', label: '目标标记', laneId: 'signal', shape: 'point', defaultDuration: 4 },
  { id: 'InputGate', group: '等待', label: '等玩家确认', laneId: 'gate', shape: 'point', defaultDuration: 6 },
  { id: 'EventGate', group: '等待', label: '等事件', laneId: 'gate', shape: 'interval', defaultDuration: 30 },
  { id: 'TargetCollectionGate', group: '等待', label: '等目标收集', laneId: 'gate', shape: 'point', defaultDuration: 6 },
  { id: 'End', group: '收束', label: '收束', laneId: 'end', shape: 'point', defaultDuration: 4 },
];

const CLOCK_OPTIONS = [
  { value: 'FixedFrame', label: 'FixedFrame' },
  { value: 'Step', label: 'Step' },
  { value: 'EntityLocal', label: 'EntityLocal' },
];

const KIND_OPTIONS = Object.keys(LANE_BY_KIND).map((kind) => ({
  value: kind,
  label: KIND_LABEL[kind] ?? kind,
}));

function skeleton(kind: string, tick: number): AbilityExecItem {
  switch (kind) {
    case 'EffectClip':
      return { kind, tick, durationTicks: 30, template: '' };
    case 'TagClip':
    case 'TagClipTarget':
      return { kind, tick, duration: 30, tag: '' };
    case 'EffectSignal':
      return { kind, tick, template: '' };
    case 'EventSignal':
      return { kind, tick, tag: '' };
    case 'TagSignal':
    case 'TagSignalTarget':
      return { kind, tick, tag: '', payloadA: 0 };
    case 'InputGate':
    case 'TargetCollectionGate':
      return { kind, tick, payloadA: 0 };
    case 'EventGate':
      return { kind, tick, tag: '', payloadA: 0 };
    case 'End':
      return { kind, tick };
    default:
      throw new Error(`未知 exec kind ${kind}`);
  }
}

export function clipIdForItem(index: number): string {
  return `item:${index}`;
}

export function itemIndexFromClipId(clipId: string): number | null {
  if (!clipId.startsWith('item:')) return null;
  const index = Number(clipId.slice('item:'.length));
  return Number.isInteger(index) && index >= 0 ? index : null;
}

function requireSource(source: unknown): AbilityExecSource | null {
  const record = asRecord(source);
  if (!record) return null;
  const exec = asRecord(record.exec) ?? {};
  const items = asArray<Record<string, unknown>>(exec.items).map((item) => {
    const next: AbilityExecItem = {
      kind: readString(item.kind),
      tick: readNumber(item.tick, 0),
    };
    if (item.duration !== undefined) next.duration = readNumber(item.duration, 0);
    if (item.durationTicks !== undefined) next.durationTicks = readNumber(item.durationTicks, 0);
    if (item.clockId !== undefined || item.clock !== undefined) {
      next.clockId = readString(item.clockId ?? item.clock);
    }
    if (item.tag !== undefined) next.tag = readString(item.tag);
    if (item.template !== undefined) next.template = readString(item.template);
    if (item.callerParamsIdx !== undefined) next.callerParamsIdx = readNumber(item.callerParamsIdx, 0);
    if (item.payloadA !== undefined) next.payloadA = readNumber(item.payloadA, 0);
    if (item.dispatchTarget !== undefined) next.dispatchTarget = readString(item.dispatchTarget);
    return next;
  });
  return {
    ...record,
    id: readString(record.id),
    presentation: asRecord(record.presentation)
      ? { displayName: readString(asRecord(record.presentation)?.displayName) }
      : undefined,
    exec: {
      clockId: readString(exec.clockId, 'FixedFrame'),
      interruptAny: readStringList(exec.interruptAny),
      callerParams: asArray(exec.callerParams),
      items,
    },
  };
}

function itemDuration(item: AbilityExecItem): number {
  if (item.kind === 'EffectClip') return Math.max(1, item.durationTicks ?? 0);
  if (item.kind === 'TagClip' || item.kind === 'TagClipTarget') return Math.max(1, item.duration ?? 0);
  if (item.kind === 'EventGate') {
    const timeout = item.payloadA ?? 0;
    return timeout > 0 ? timeout : 8;
  }
  return 4;
}

function itemShape(kind: string): 'interval' | 'point' {
  return kind === 'EffectClip' || kind === 'TagClip' || kind === 'TagClipTarget' || kind === 'EventGate'
    ? 'interval'
    : 'point';
}

function itemResizable(kind: string): boolean {
  return itemShape(kind) === 'interval';
}

function itemLabel(item: AbilityExecItem): string {
  return item.template || item.tag || KIND_LABEL[item.kind] || item.kind || '条目';
}

function itemBadges(item: AbilityExecItem): string[] {
  const badges = [KIND_LABEL[item.kind] ?? item.kind];
  if (item.kind === 'EventGate' && (item.payloadA ?? 0) === 0) badges.push('无限等待');
  if (item.kind === 'TagSignal' || item.kind === 'TagSignalTarget') {
    badges.push((item.payloadA ?? 0) === 1 ? '删' : '加');
  }
  if (item.dispatchTarget) badges.push(item.dispatchTarget);
  return badges;
}

function clipFields(clip: TimelineClip): TimelineField[] {
  const kind = readString(clip.payload.kind);
  return [
    { key: 'kind', label: '条目种类', type: 'select', options: KIND_OPTIONS },
    { key: 'tick', label: '到达 tick', type: 'number', step: 1, min: 0 },
    {
      key: 'durationTicks',
      label: '持续 tick',
      type: 'number',
      step: 1,
      min: 0,
      visibleWhen: (payload) => readString(payload.kind) === 'EffectClip',
    },
    {
      key: 'duration',
      label: '持续 tick',
      type: 'number',
      step: 1,
      min: 0,
      visibleWhen: (payload) => {
        const value = readString(payload.kind);
        return value === 'TagClip' || value === 'TagClipTarget';
      },
    },
    {
      key: 'tag',
      label: '标记',
      type: 'text',
      visibleWhen: (payload) => {
        const value = readString(payload.kind);
        return value.includes('Tag') || value === 'EventSignal' || value === 'EventGate';
      },
    },
    {
      key: 'template',
      label: '效果模板',
      type: 'text',
      visibleWhen: (payload) => readString(payload.kind).startsWith('Effect'),
    },
    {
      key: 'dispatchTarget',
      label: '派发目标',
      type: 'select',
      options: [
        { value: '', label: 'Default' },
        { value: 'Source', label: 'Source' },
        { value: 'Target', label: 'Target' },
        { value: 'TargetContext', label: 'TargetContext' },
      ],
      visibleWhen: (payload) => readString(payload.kind).startsWith('Effect'),
    },
    {
      key: 'tagMode',
      label: '标记动作',
      type: 'select',
      options: [
        { value: '0', label: '加' },
        { value: '1', label: '删' },
      ],
      visibleWhen: (payload) => {
        const value = readString(payload.kind);
        return value === 'TagSignal' || value === 'TagSignalTarget';
      },
    },
    {
      key: 'requestId',
      label: '请求号（0 = 用订单号）',
      type: 'number',
      step: 1,
      min: 0,
      visibleWhen: (payload) => {
        const value = readString(payload.kind);
        return value === 'InputGate' || value === 'TargetCollectionGate';
      },
    },
    {
      key: 'timeoutTicks',
      label: '超时 tick（0 = 无限等）',
      type: 'number',
      step: 1,
      min: 0,
      visibleWhen: (payload) => readString(payload.kind) === 'EventGate',
    },
    { key: 'clockId', label: '条目时钟覆盖', type: 'select', options: [{ value: '', label: '跟随整轴' }, ...CLOCK_OPTIONS] },
    { key: 'callerParamsIdx', label: '参数池下标', type: 'number', step: 1, min: 0 },
  ];
}

function writeItem(current: AbilityExecItem, payload: Record<string, unknown>): AbilityExecItem {
  const kind = LANE_BY_KIND[readString(payload.kind)] ? readString(payload.kind) : current.kind;
  const next: AbilityExecItem = {
    kind,
    tick: readNumber(payload.tick, current.tick),
  };
  if (payload.clockId) next.clockId = readString(payload.clockId);
  if (payload.callerParamsIdx !== undefined && payload.callerParamsIdx !== '' && payload.callerParamsIdx !== null) {
    next.callerParamsIdx = readNumber(payload.callerParamsIdx);
  }
  if (kind === 'EffectClip') {
    next.durationTicks = readNumber(payload.durationTicks, current.durationTicks ?? 0);
    next.template = readString(payload.template, current.template ?? '');
    if (payload.dispatchTarget) next.dispatchTarget = readString(payload.dispatchTarget);
    return next;
  }
  if (kind === 'TagClip' || kind === 'TagClipTarget') {
    next.duration = readNumber(payload.duration, current.duration ?? 0);
    next.tag = readString(payload.tag, current.tag ?? '');
    return next;
  }
  if (kind === 'EffectSignal') {
    next.template = readString(payload.template, current.template ?? '');
    if (payload.dispatchTarget) next.dispatchTarget = readString(payload.dispatchTarget);
    return next;
  }
  if (kind === 'EventSignal' || kind === 'EventGate') {
    next.tag = readString(payload.tag, current.tag ?? '');
    if (kind === 'EventGate') next.payloadA = readNumber(payload.timeoutTicks, current.payloadA ?? 0);
    return next;
  }
  if (kind === 'TagSignal' || kind === 'TagSignalTarget') {
    next.tag = readString(payload.tag, current.tag ?? '');
    next.payloadA = readNumber(payload.tagMode, current.payloadA ?? 0);
    return next;
  }
  if (kind === 'InputGate' || kind === 'TargetCollectionGate') {
    next.payloadA = readNumber(payload.requestId, current.payloadA ?? 0);
    return next;
  }
  return next;
}

function collectIssues(row: AbilityExecSource): TimelineIssue[] {
  const items = row.exec?.items ?? [];
  const issues: TimelineIssue[] = [];
  if (items.length > ABILITY_EXEC_MAX_ITEMS) {
    issues.push({ level: 'error', message: `条目数 ${items.length} 超过上限 ${ABILITY_EXEC_MAX_ITEMS}。` });
  }
  if (!row.exec?.clockId) {
    issues.push({ level: 'error', message: 'exec.clockId 必填。' });
  }
  let previousTick = Number.NEGATIVE_INFINITY;
  for (const [index, item] of items.entries()) {
    const clipId = clipIdForItem(index);
    if (!LANE_BY_KIND[item.kind]) {
      issues.push({ level: 'error', message: `条目 ${index} 的 kind ${item.kind} 不在白名单。`, clipId });
    }
    if (item.kind === 'EffectClip' && (item.durationTicks === undefined || item.durationTicks < 0)) {
      issues.push({ level: 'error', message: `条目 ${index} 的 EffectClip 需要 durationTicks。`, clipId });
    }
    if ((item.kind === 'EffectClip' || item.kind === 'EffectSignal') && !item.template) {
      issues.push({ level: 'error', message: `条目 ${index} 缺少效果模板。`, clipId });
    }
    if (item.kind.includes('Tag') && !item.tag) {
      issues.push({ level: 'error', message: `条目 ${index} 缺少标记。`, clipId });
    }
    if (item.tick < previousTick) {
      issues.push({
        level: 'warning',
        message: `条目 ${index} 的 tick ${item.tick} 小于前一条；运行时按数组序消费，不会重排。`,
        clipId,
      });
    }
    previousTick = item.tick;
  }
  if (items.length >= ABILITY_EXEC_MAX_ITEMS - 2 && items.length <= ABILITY_EXEC_MAX_ITEMS) {
    issues.push({ level: 'warning', message: `条目占用 ${items.length}/${ABILITY_EXEC_MAX_ITEMS}。` });
  }
  return issues;
}

export function projectAbilityExec(source: unknown): TimelineDocument {
  const row = requireSource(source) ?? { id: '', exec: { clockId: 'FixedFrame', items: [] } };
  const items = row.exec?.items ?? [];
  return {
    id: row.id,
    displayName: row.presentation?.displayName || row.id,
    timeUnit: 'ticks',
    clockLabel: row.exec?.clockId ?? 'FixedFrame',
    headerFields: [
      { key: 'id', label: '技能 ID', type: 'text' },
      {
        key: 'clockId',
        label: '整轴时钟',
        type: 'select',
        options: CLOCK_OPTIONS,
      },
      { key: 'interruptAny', label: '打断标记（逗号分隔）', type: 'text' },
    ],
    headerValues: {
      id: row.id,
      clockId: row.exec?.clockId ?? 'FixedFrame',
      interruptAny: (row.exec?.interruptAny ?? []).join(', '),
    },
    lanes: LANES,
    clips: items.map((item, index) => ({
      id: clipIdForItem(index),
      laneId: LANE_BY_KIND[item.kind] ?? 'signal',
      start: Math.max(0, item.tick),
      duration: itemDuration(item),
      shape: itemShape(item.kind),
      resizable: itemResizable(item.kind),
      movable: true,
      label: itemLabel(item),
      badges: itemBadges(item),
      payload: {
        ...item,
        tagMode: String(item.payloadA ?? 0),
        requestId: item.payloadA ?? 0,
        timeoutTicks: item.payloadA ?? 0,
      },
    })),
    issues: collectIssues(row),
    occupancy: { used: items.length, max: ABILITY_EXEC_MAX_ITEMS },
  };
}

export function applyAbilityExecClipChange(
  source: unknown,
  clipId: string,
  patch: { start?: number; duration?: number; laneId?: string; payload?: Record<string, unknown> },
): TimelineMutation {
  const row = requireSource(source);
  if (!row) return { ok: false, error: '技能源不是对象。' };
  const index = itemIndexFromClipId(clipId);
  const items = row.exec?.items ?? [];
  if (index === null || !items[index]) return { ok: false, error: `找不到条目 ${clipId}。` };
  const current = items[index];
  const payload = {
    ...current,
    tagMode: String(current.payloadA ?? 0),
    requestId: current.payloadA ?? 0,
    timeoutTicks: current.payloadA ?? 0,
    ...(patch.payload ?? {}),
    tick: patch.start ?? readNumber(patch.payload?.tick, current.tick),
  };
  if (patch.duration !== undefined) {
    if (current.kind === 'EffectClip') payload.durationTicks = patch.duration;
    else if (current.kind === 'TagClip' || current.kind === 'TagClipTarget') payload.duration = patch.duration;
    else if (current.kind === 'EventGate') payload.timeoutTicks = patch.duration;
  }
  const next = cloneJson(row);
  next.exec = next.exec ?? { clockId: 'FixedFrame', items: [] };
  next.exec.items = items.slice();
  next.exec.items[index] = writeItem(current, payload);
  return { ok: true, source: next };
}

export function addAbilityExecFromPalette(source: unknown, paletteId: string, start: number): TimelineMutation {
  const row = requireSource(source);
  if (!row) return { ok: false, error: '技能源不是对象。' };
  if (!LANE_BY_KIND[paletteId]) return { ok: false, error: `未知 exec kind ${paletteId}。` };
  const items = row.exec?.items ?? [];
  if (items.length >= ABILITY_EXEC_MAX_ITEMS) {
    return { ok: false, error: `条目已满 ${ABILITY_EXEC_MAX_ITEMS}，不能再加。` };
  }
  const next = cloneJson(row);
  next.exec = next.exec ?? { clockId: 'FixedFrame', items: [] };
  next.exec.items = items.slice();
  next.exec.items.push(skeleton(paletteId, Math.max(0, Math.round(start))));
  return { ok: true, source: next };
}

export function removeAbilityExecClip(source: unknown, clipId: string): TimelineMutation {
  const row = requireSource(source);
  if (!row) return { ok: false, error: '技能源不是对象。' };
  const index = itemIndexFromClipId(clipId);
  const items = row.exec?.items ?? [];
  if (index === null || !items[index]) return { ok: false, error: `找不到条目 ${clipId}。` };
  const next = cloneJson(row);
  next.exec = next.exec ?? { clockId: 'FixedFrame', items: [] };
  next.exec.items = items.filter((_, i) => i !== index);
  return { ok: true, source: next };
}

export function applyAbilityExecHeader(source: unknown, values: Record<string, unknown>): TimelineMutation {
  const row = requireSource(source);
  if (!row) return { ok: false, error: '技能源不是对象。' };
  const next = cloneJson(row);
  next.id = readString(values.id, row.id);
  next.exec = next.exec ?? { items: [] };
  next.exec.clockId = readString(values.clockId, row.exec?.clockId ?? 'FixedFrame');
  const raw = readString(values.interruptAny);
  next.exec.interruptAny = raw
    .split(',')
    .map((part) => part.trim())
    .filter((part) => part.length > 0);
  return { ok: true, source: next };
}

export const abilityExecAdapter: TimelineAdapter = {
  contextId: 'ability-exec',
  label: '技能时间轴',
  blurb: '一条 tick 轴上排持续、瞬发、等待和收束。拖动只改到达时刻，不重排数组。',
  timeUnit: 'ticks',
  pixelsPerUnit: 6,
  unitLabel: 't',
  lanes: LANES,
  palette: PALETTE,
  clipFields,
  project: projectAbilityExec,
  applyClipChange: applyAbilityExecClipChange,
  addFromPalette: addAbilityExecFromPalette,
  removeClip: removeAbilityExecClip,
  applyHeader: applyAbilityExecHeader,
};
