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
  type TimelineIssue,
  type TimelineLane,
  type TimelineMutation,
  type TimelinePaletteItem,
} from '../model.ts';

export const PRESENTER_DURATION_TIMER_NAME = 'presenter.duration';

export type PresenterEvent = {
  kind?: string;
  keyId?: string;
};

export type PresenterCommand = {
  kind?: string;
  timerName?: string;
  durationSeconds?: number;
  durationRangeSeconds?: number;
  [key: string]: unknown;
};

export type PresenterRule = {
  event?: PresenterEvent;
  condition?: { inline?: string };
  command?: PresenterCommand;
};

export type PresenterSource = {
  id: string;
  lifecycle?: { durationSeconds?: number; persistence?: string };
  rules?: PresenterRule[];
  [key: string]: unknown;
};

const FIXED_LANES: TimelineLane[] = [
  { id: 'lifecycle', label: '生命周期', colorClass: 'bg-zinc-400/80 border-zinc-200' },
  { id: 'interrupt', label: '打断 TimerKill', colorClass: 'bg-rose-500/70 border-rose-200' },
  { id: 'wildcard', label: '通配到期', colorClass: 'bg-violet-500/80 border-violet-200' },
];

const TIMER_COLORS = [
  'bg-sky-500/80 border-sky-300',
  'bg-amber-500/80 border-amber-200',
  'bg-emerald-500/80 border-emerald-200',
  'bg-fuchsia-500/70 border-fuchsia-200',
];

const EVENT_KINDS = [
  'GameplayEvent',
  'TagEffectiveChanged',
  'PresenterCreated',
  'PresenterDestroyed',
  'TimerExpired',
];

const COMMAND_KINDS = [
  'TimerSet',
  'TimerKill',
  'SetParam',
  'CreatePresenter',
  'DestroyPresenter',
  'DestroyPresenterScope',
  'ActivateBehavior',
  'DeactivateBehavior',
];

const PALETTE: TimelinePaletteItem[] = [
  { id: 'TimerSet', group: '计时', label: '启动计时', laneId: 'timer', shape: 'interval', defaultDuration: 0.6 },
  { id: 'Reaction', group: '计时', label: '到期反应', laneId: 'timer', shape: 'point', defaultDuration: 0.25 },
  { id: 'TimerKill', group: '计时', label: '打断计时', laneId: 'interrupt', shape: 'point', defaultDuration: 0.25 },
  { id: 'Lifecycle', group: '计时', label: '生命周期', laneId: 'lifecycle', shape: 'interval', defaultDuration: 0.22 },
];

export function clipIdForRule(index: number): string {
  return `rule:${index}`;
}

export function ruleIndexFromClipId(clipId: string): number | null {
  if (clipId === 'lifecycle') return null;
  if (!clipId.startsWith('rule:')) return null;
  const index = Number(clipId.slice('rule:'.length));
  return Number.isInteger(index) && index >= 0 ? index : null;
}

function requireSource(source: unknown): PresenterSource | null {
  const record = asRecord(source);
  if (!record) return null;
  const lifecycle = asRecord(record.lifecycle);
  return {
    ...record,
    id: readString(record.id),
    lifecycle: lifecycle
      ? {
          durationSeconds:
            lifecycle.durationSeconds === undefined ? undefined : readNumber(lifecycle.durationSeconds),
          persistence: lifecycle.persistence === undefined ? undefined : readString(lifecycle.persistence),
        }
      : undefined,
    rules: asArray<Record<string, unknown>>(record.rules).map((rule) => {
      const event = asRecord(rule.event);
      const command = asRecord(rule.command);
      const condition = asRecord(rule.condition);
      return {
        event: event ? { kind: readString(event.kind) || undefined, keyId: readString(event.keyId) || undefined } : undefined,
        condition: condition ? { inline: readString(condition.inline) || undefined } : undefined,
        command: command ? { ...command, kind: readString(command.kind) } : undefined,
      };
    }),
  };
}

function timerNames(rules: PresenterRule[]): string[] {
  const names: string[] = [];
  const seen = new Set<string>();
  for (const rule of rules) {
    const command = rule.command;
    if (command?.kind === 'TimerSet' && command.timerName && command.timerName !== '*') {
      if (!seen.has(command.timerName)) {
        seen.add(command.timerName);
        names.push(command.timerName);
      }
    }
  }
  for (const rule of rules) {
    const keyId = rule.event?.kind === 'TimerExpired' ? rule.event.keyId : undefined;
    if (keyId && keyId !== '*' && !seen.has(keyId)) {
      seen.add(keyId);
      names.push(keyId);
    }
    if (rule.command?.kind === 'TimerKill' && rule.command.timerName && rule.command.timerName !== '*' && !seen.has(rule.command.timerName)) {
      seen.add(rule.command.timerName);
      names.push(rule.command.timerName);
    }
  }
  return names;
}

function lastTimerSetIndex(rules: PresenterRule[], timerName: string): number {
  for (let i = rules.length - 1; i >= 0; i -= 1) {
    if (rules[i]?.command?.kind === 'TimerSet' && rules[i]?.command?.timerName === timerName) return i;
  }
  return -1;
}

function resolveTimerSetStarts(rules: PresenterRule[]): Map<number, number> {
  const starts = new Map<number, number>();
  for (let pass = 0; pass < rules.length + 1; pass += 1) {
    let changed = false;
    for (const [index, rule] of rules.entries()) {
      if (rule.command?.kind !== 'TimerSet') continue;
      const event = rule.event;
      let start = 0;
      if (event?.kind === 'TimerExpired' && event.keyId && event.keyId !== '*') {
        const pred = lastTimerSetIndex(rules, event.keyId);
        if (pred < 0) {
          start = 0;
        } else if (!starts.has(pred)) {
          continue;
        } else {
          start = starts.get(pred)! + Math.max(0, readNumber(rules[pred]?.command?.durationSeconds, 0));
        }
      }
      if (starts.get(index) !== start) {
        starts.set(index, start);
        changed = true;
      }
    }
    if (!changed) break;
  }
  return starts;
}

function timerEnd(rules: PresenterRule[], starts: Map<number, number>, timerName: string): number | null {
  const index = lastTimerSetIndex(rules, timerName);
  if (index < 0 || !starts.has(index)) return null;
  return starts.get(index)! + Math.max(0, readNumber(rules[index]?.command?.durationSeconds, 0));
}

function laneForTimer(timerName: string): string {
  return `timer:${timerName}`;
}

function buildLanes(names: string[], source: PresenterSource): TimelineLane[] {
  const lanes: TimelineLane[] = [];
  if (source.lifecycle?.durationSeconds) lanes.push(FIXED_LANES[0]);
  if ((source.rules ?? []).some((rule) => rule.command?.kind === 'TimerKill')) lanes.push(FIXED_LANES[1]);
  if ((source.rules ?? []).some((rule) => rule.event?.kind === 'TimerExpired' && rule.event.keyId === '*')) {
    lanes.push(FIXED_LANES[2]);
  }
  names.forEach((name, index) => {
    lanes.push({
      id: laneForTimer(name),
      label: name,
      colorClass: TIMER_COLORS[index % TIMER_COLORS.length],
    });
  });
  return lanes.length > 0 ? lanes : [{ id: 'timer:new', label: '计时', colorClass: TIMER_COLORS[0] }];
}

function triggerBadge(rule: PresenterRule): string {
  const kind = rule.event?.kind || '事件';
  const key = rule.event?.keyId ? `:${rule.event.keyId}` : '';
  const condition = rule.condition?.inline ? `/${rule.condition.inline}` : '';
  return `${kind}${key}${condition}`;
}

function commandLabel(command: PresenterCommand | undefined): string {
  if (!command?.kind) return '命令';
  if (command.kind === 'TimerSet') return command.timerName || 'TimerSet';
  if (command.kind === 'TimerKill') return `Kill ${command.timerName || '?'}`;
  if (command.kind === 'SetParam') return `SetParam ${readString(command.paramKey)}`;
  if (command.kind === 'CreatePresenter') return `Create ${readString(command.definitionId)}`;
  return command.kind;
}

function collectIssues(row: PresenterSource, starts: Map<number, number>): TimelineIssue[] {
  const rules = row.rules ?? [];
  const issues: TimelineIssue[] = [];
  const names = new Map<string, number>();
  for (const [index, rule] of rules.entries()) {
    const clipId = clipIdForRule(index);
    const command = rule.command;
    if (!command?.kind) {
      issues.push({ level: 'error', message: `规则 ${index} 缺少 command.kind。`, clipId });
      continue;
    }
    if (command.kind === 'TimerSet') {
      if (!command.timerName) {
        issues.push({ level: 'error', message: `规则 ${index} 的 TimerSet 缺少 timerName。`, clipId });
      } else if (command.timerName === '*' || command.timerName === PRESENTER_DURATION_TIMER_NAME) {
        issues.push({
          level: 'error',
          message: `规则 ${index} 不能把 timerName 写成 * 或 ${PRESENTER_DURATION_TIMER_NAME}。`,
          clipId,
        });
      }
      if (!(readNumber(command.durationSeconds, 0) > 0)) {
        issues.push({ level: 'error', message: `规则 ${index} 的 durationSeconds 必须大于 0。`, clipId });
      }
      if (command.timerName) {
        if (names.has(command.timerName)) {
          issues.push({
            level: 'warning',
            message: `计时名 ${command.timerName} 出现多次：后写的 TimerSet 会替换同实例上的同名计时。`,
            clipId,
          });
        }
        names.set(command.timerName, index);
      }
      if (rule.event?.kind === 'TimerExpired' && rule.event.keyId && rule.event.keyId !== '*' && lastTimerSetIndex(rules, rule.event.keyId) < 0) {
        issues.push({
          level: 'warning',
          message: `规则 ${index} 等 ${rule.event.keyId} 到期，但没有对应的 TimerSet。`,
          clipId,
        });
      }
      if (rule.event?.kind === 'TimerExpired' && rule.event.keyId && rule.event.keyId !== '*' && !starts.has(index)) {
        issues.push({ level: 'warning', message: `规则 ${index} 的链式开始时刻还解析不出来。`, clipId });
      }
    }
    if (command.kind === 'TimerKill' && !command.timerName) {
      issues.push({ level: 'error', message: `规则 ${index} 的 TimerKill 缺少 timerName。`, clipId });
    }
    if (rule.event?.kind === 'TimerExpired' && rule.event.keyId && rule.event.keyId !== '*' && lastTimerSetIndex(rules, rule.event.keyId) < 0) {
      issues.push({
        level: 'warning',
        message: `规则 ${index} 监听未定义的计时 ${rule.event.keyId}。`,
        clipId,
      });
    }
  }
  if (row.lifecycle?.durationSeconds !== undefined && !(row.lifecycle.durationSeconds > 0)) {
    issues.push({ level: 'error', message: 'lifecycle.durationSeconds 必须大于 0。', clipId: 'lifecycle' });
  }
  return issues;
}

function clipFields(clip: TimelineClip): TimelineField[] {
  const role = readString(clip.payload.role);
  if (role === 'lifecycle') {
    return [{ key: 'durationSeconds', label: '寿命秒', type: 'number', step: 0.05, min: 0.01 }];
  }
  const fields: TimelineField[] = [
    {
      key: 'eventKind',
      label: '触发事件',
      type: 'select',
      options: EVENT_KINDS.map((kind) => ({ value: kind, label: kind })),
    },
    { key: 'eventKeyId', label: '事件 keyId', type: 'text' },
    { key: 'conditionInline', label: '条件（如 TagLost）', type: 'text' },
    {
      key: 'commandKind',
      label: '命令',
      type: 'select',
      options: COMMAND_KINDS.map((kind) => ({ value: kind, label: kind })),
    },
  ];
  if (role === 'timer-set') {
    fields.push(
      { key: 'timerName', label: '计时名', type: 'text' },
      { key: 'durationSeconds', label: '持续秒', type: 'number', step: 0.05, min: 0.01 },
      { key: 'durationRangeSeconds', label: '抖动秒', type: 'number', step: 0.05, min: 0 },
    );
  } else if (role === 'kill') {
    fields.push({ key: 'timerName', label: '计时名（* = 全清）', type: 'text' });
  } else {
    fields.push(
      { key: 'timerName', label: '到期计时名', type: 'text', visibleWhen: (payload) => readString(payload.eventKind) === 'TimerExpired' },
      { key: 'paramKey', label: '参数键', type: 'text', visibleWhen: (payload) => readString(payload.commandKind) === 'SetParam' },
      { key: 'definitionId', label: '要创建的 Presenter', type: 'text', visibleWhen: (payload) => readString(payload.commandKind) === 'CreatePresenter' },
    );
  }
  return fields;
}

export function projectPresenterTimer(source: unknown): TimelineDocument {
  const row = requireSource(source) ?? { id: '', rules: [] };
  const rules = row.rules ?? [];
  const starts = resolveTimerSetStarts(rules);
  const names = timerNames(rules);
  const clips: TimelineClip[] = [];

  if (row.lifecycle?.durationSeconds) {
    clips.push({
      id: 'lifecycle',
      laneId: 'lifecycle',
      start: 0,
      duration: row.lifecycle.durationSeconds,
      shape: 'interval',
      resizable: true,
      movable: false,
      label: 'presenter.duration',
      badges: ['编译寿命'],
      payload: { role: 'lifecycle', durationSeconds: row.lifecycle.durationSeconds },
    });
  }

  for (const [index, rule] of rules.entries()) {
    const command = rule.command;
    if (!command?.kind) continue;
    if (command.kind === 'TimerSet') {
      const name = command.timerName || `unnamed-${index}`;
      clips.push({
        id: clipIdForRule(index),
        laneId: laneForTimer(name),
        start: starts.get(index) ?? 0,
        duration: Math.max(0.05, readNumber(command.durationSeconds, 0.05)),
        shape: 'interval',
        resizable: true,
        movable: false,
        label: name,
        badges: [triggerBadge(rule)],
        payload: {
          role: 'timer-set',
          ruleIndex: index,
          timerName: command.timerName ?? '',
          durationSeconds: command.durationSeconds ?? 0,
          durationRangeSeconds: command.durationRangeSeconds ?? 0,
          eventKind: rule.event?.kind ?? '',
          eventKeyId: rule.event?.keyId ?? '',
          conditionInline: rule.condition?.inline ?? '',
          commandKind: 'TimerSet',
        },
      });
      continue;
    }
    if (command.kind === 'TimerKill') {
      clips.push({
        id: clipIdForRule(index),
        laneId: 'interrupt',
        start: 0,
        duration: 0.25,
        shape: 'point',
        resizable: false,
        movable: false,
        label: commandLabel(command),
        badges: [triggerBadge(rule)],
        payload: {
          role: 'kill',
          ruleIndex: index,
          timerName: command.timerName ?? '*',
          eventKind: rule.event?.kind ?? '',
          eventKeyId: rule.event?.keyId ?? '',
          conditionInline: rule.condition?.inline ?? '',
          commandKind: 'TimerKill',
        },
      });
      continue;
    }
    if (rule.event?.kind === 'TimerExpired') {
      const keyId = rule.event.keyId ?? '*';
      const end = keyId === '*' ? Math.max(0, ...[...starts.entries()].map(([i, start]) => start + readNumber(rules[i]?.command?.durationSeconds, 0))) : timerEnd(rules, starts, keyId);
      clips.push({
        id: clipIdForRule(index),
        laneId: keyId === '*' ? 'wildcard' : laneForTimer(keyId),
        start: end ?? 0,
        duration: 0.25,
        shape: 'point',
        resizable: false,
        movable: false,
        label: commandLabel(command),
        badges: [triggerBadge(rule)],
        payload: {
          role: 'reaction',
          ruleIndex: index,
          timerName: keyId,
          eventKind: 'TimerExpired',
          eventKeyId: keyId,
          conditionInline: rule.condition?.inline ?? '',
          commandKind: command.kind,
          paramKey: readString(command.paramKey),
          definitionId: readString(command.definitionId),
        },
      });
    }
  }

  return {
    id: row.id,
    displayName: row.id,
    timeUnit: 'seconds',
    clockLabel: '从触发算起',
    headerFields: [
      { key: 'id', label: 'Presenter ID', type: 'text' },
      { key: 'lifecycleDuration', label: '生命周期秒（空=不编译寿命）', type: 'text' },
    ],
    headerValues: {
      id: row.id,
      lifecycleDuration: row.lifecycle?.durationSeconds ? String(row.lifecycle.durationSeconds) : '',
    },
    lanes: buildLanes(names, row),
    clips,
    issues: collectIssues(row, starts),
  };
}

function writeRule(rule: PresenterRule, payload: Record<string, unknown>): PresenterRule {
  const next = cloneJson(rule);
  next.event = {
    kind: readString(payload.eventKind, rule.event?.kind ?? ''),
    keyId: readString(payload.eventKeyId, rule.event?.keyId ?? ''),
  };
  const inline = readString(payload.conditionInline);
  next.condition = inline ? { inline } : undefined;
  next.command = { ...(rule.command ?? {}), kind: readString(payload.commandKind, rule.command?.kind ?? '') };
  if (next.command.kind === 'TimerSet' || next.command.kind === 'TimerKill') {
    next.command.timerName = readString(payload.timerName, rule.command?.timerName ?? '');
  }
  if (next.command.kind === 'TimerSet') {
    next.command.durationSeconds = readNumber(payload.durationSeconds, rule.command?.durationSeconds ?? 0.3);
    const jitter = readNumber(payload.durationRangeSeconds, rule.command?.durationRangeSeconds ?? 0);
    if (jitter > 0) next.command.durationRangeSeconds = jitter;
    else delete next.command.durationRangeSeconds;
  }
  if (payload.paramKey !== undefined) next.command.paramKey = readString(payload.paramKey);
  if (payload.definitionId !== undefined) next.command.definitionId = readString(payload.definitionId);
  return next;
}

export function applyPresenterTimerClipChange(
  source: unknown,
  clipId: string,
  patch: { start?: number; duration?: number; payload?: Record<string, unknown> },
): TimelineMutation {
  const row = requireSource(source);
  if (!row) return { ok: false, error: 'Presenter 源不是对象。' };
  const next = cloneJson(row);
  if (clipId === 'lifecycle') {
    const duration = patch.duration ?? readNumber(patch.payload?.durationSeconds, row.lifecycle?.durationSeconds ?? 0);
    if (!(duration > 0)) return { ok: false, error: 'lifecycle.durationSeconds 必须大于 0。' };
    next.lifecycle = { ...(row.lifecycle ?? {}), durationSeconds: duration };
    return { ok: true, source: next };
  }
  const index = ruleIndexFromClipId(clipId);
  const rules = row.rules ?? [];
  if (index === null || !rules[index]) return { ok: false, error: `找不到规则 ${clipId}。` };
  const current = rules[index];
  const payload = {
    role: current.command?.kind === 'TimerSet' ? 'timer-set' : current.command?.kind === 'TimerKill' ? 'kill' : 'reaction',
    eventKind: current.event?.kind ?? '',
    eventKeyId: current.event?.keyId ?? '',
    conditionInline: current.condition?.inline ?? '',
    commandKind: current.command?.kind ?? '',
    timerName: current.command?.timerName ?? current.event?.keyId ?? '',
    durationSeconds: current.command?.durationSeconds ?? 0,
    durationRangeSeconds: current.command?.durationRangeSeconds ?? 0,
    paramKey: readString(current.command?.paramKey),
    definitionId: readString(current.command?.definitionId),
    ...(patch.payload ?? {}),
  };
  if (patch.duration !== undefined && current.command?.kind === 'TimerSet') {
    payload.durationSeconds = patch.duration;
  }
  if (payload.timerName === PRESENTER_DURATION_TIMER_NAME) {
    return { ok: false, error: `${PRESENTER_DURATION_TIMER_NAME} 只留给编译寿命，不能手写。` };
  }
  if (payload.commandKind === 'TimerSet' && payload.timerName === '*') {
    return { ok: false, error: 'TimerSet 不能使用通配名 *。' };
  }
  next.rules = rules.slice();
  next.rules[index] = writeRule(current, payload);
  return { ok: true, source: next };
}

export function addPresenterTimerFromPalette(source: unknown, paletteId: string, _start: number): TimelineMutation {
  const row = requireSource(source);
  if (!row) return { ok: false, error: 'Presenter 源不是对象。' };
  const next = cloneJson(row);
  next.rules = (row.rules ?? []).slice();
  if (paletteId === 'Lifecycle') {
    if (next.lifecycle?.durationSeconds) return { ok: false, error: '已经有生命周期秒，不能再加一条。' };
    next.lifecycle = { durationSeconds: 0.22 };
    return { ok: true, source: next };
  }
  if (paletteId === 'TimerSet') {
    const existing = new Set(timerNames(next.rules));
    let name = 'timer.custom';
    let suffix = 1;
    while (existing.has(name)) {
      suffix += 1;
      name = `timer.custom.${suffix}`;
    }
    next.rules.push({
      event: { kind: 'GameplayEvent', keyId: '' },
      command: { kind: 'TimerSet', timerName: name, durationSeconds: 0.6 },
    });
    return { ok: true, source: next };
  }
  if (paletteId === 'Reaction') {
    const names = timerNames(next.rules);
    if (names.length === 0) return { ok: false, error: '先加一条启动计时，才能挂到期反应。' };
    next.rules.push({
      event: { kind: 'TimerExpired', keyId: names[names.length - 1] },
      command: { kind: 'SetParam', paramKey: '' },
    });
    return { ok: true, source: next };
  }
  if (paletteId === 'TimerKill') {
    next.rules.push({
      event: { kind: 'TagEffectiveChanged', keyId: '' },
      condition: { inline: 'TagGained' },
      command: { kind: 'TimerKill', timerName: '*' },
    });
    return { ok: true, source: next };
  }
  return { ok: false, error: `未知调色板项 ${paletteId}。` };
}

export function removePresenterTimerClip(source: unknown, clipId: string): TimelineMutation {
  const row = requireSource(source);
  if (!row) return { ok: false, error: 'Presenter 源不是对象。' };
  const next = cloneJson(row);
  if (clipId === 'lifecycle') {
    if (!next.lifecycle) return { ok: false, error: '没有生命周期可删。' };
    delete next.lifecycle;
    return { ok: true, source: next };
  }
  const index = ruleIndexFromClipId(clipId);
  const rules = row.rules ?? [];
  if (index === null || !rules[index]) return { ok: false, error: `找不到规则 ${clipId}。` };
  next.rules = rules.filter((_, i) => i !== index);
  return { ok: true, source: next };
}

export function applyPresenterTimerHeader(source: unknown, values: Record<string, unknown>): TimelineMutation {
  const row = requireSource(source);
  if (!row) return { ok: false, error: 'Presenter 源不是对象。' };
  const next = cloneJson(row);
  next.id = readString(values.id, row.id);
  const raw = readString(values.lifecycleDuration).trim();
  if (raw === '') {
    if (next.lifecycle) {
      const copy = { ...next.lifecycle };
      delete copy.durationSeconds;
      if (copy.persistence) next.lifecycle = copy;
      else delete next.lifecycle;
    }
    return { ok: true, source: next };
  }
  const duration = Number(raw);
  if (!(duration > 0)) return { ok: false, error: '生命周期秒必须大于 0，或留空。' };
  next.lifecycle = { ...(next.lifecycle ?? {}), durationSeconds: duration };
  return { ok: true, source: next };
}

export const presenterTimerAdapter: TimelineAdapter = {
  contextId: 'presenter-timer',
  label: '演出计时',
  blurb: '把命名倒计时和到期反应投影到时间轴上。拖右边改持续秒，开始时刻由触发链推出来。',
  timeUnit: 'seconds',
  pixelsPerUnit: 160,
  unitLabel: 's',
  lanes: FIXED_LANES,
  palette: PALETTE,
  clipFields,
  project: projectPresenterTimer,
  applyClipChange: applyPresenterTimerClipChange,
  addFromPalette: addPresenterTimerFromPalette,
  removeClip: removePresenterTimerClip,
  applyHeader: applyPresenterTimerHeader,
};
