import type { Edge, Node } from '@xyflow/react';

export type LiveDebugEvent = {
  sequence: number;
  event: string;
  nodeId?: string | null;
  op?: string | null;
  controlPort?: string | null;
  pinIndex?: number;
  value?: number | boolean;
  steps: number;
  /** Client receive time — used for heat / edge decay. */
  atMs?: number;
};

export type LiveNodeHeat = {
  /** 0..1 — 1 is hottest / most recent */
  intensity: number;
  /** true if this node is the latest NodeEnter still within the hot window */
  current: boolean;
};

export type LivePinValue = {
  pinIndex: number;
  value: string;
};

export type LiveEdgeHeat = {
  intensity: number;
  kind: 'control' | 'value';
};

/** How long a NodeEnter / pin keep visual heat after they arrive. */
export const LIVE_HEAT_TTL_MS = 2200;
/** Only the freshest enter stays "current" within this window. */
export const LIVE_CURRENT_MS = 280;

const PIN_WINDOW = 120;

function isNodeEnter(event: string): boolean {
  return event === 'NodeEnter';
}

function isPinEvent(event: string): boolean {
  return event === 'PinInt'
    || event === 'PinFloat'
    || event === 'PinBool'
    || event === 'PinEntity';
}

function eventTime(event: LiveDebugEvent, fallbackMs: number): number {
  return typeof event.atMs === 'number' ? event.atMs : fallbackMs;
}

function decayIntensity(ageMs: number, ttlMs: number): number {
  if (ageMs < 0) return 1;
  if (ageMs >= ttlMs) return 0;
  return 1 - ageMs / ttlMs;
}

/**
 * Build per-node heat from recent NodeEnter events.
 * Intensity decays with wall-clock age so a finished run cools off —
 * nodes must not stay lit forever after one fire.
 */
export function computeLiveNodeHeat(
  events: LiveDebugEvent[],
  nowMs: number = Date.now(),
  ttlMs: number = LIVE_HEAT_TTL_MS,
): Map<string, LiveNodeHeat> {
  const enters = events.filter((e) => isNodeEnter(e.event) && e.nodeId);
  const heat = new Map<string, LiveNodeHeat>();
  if (enters.length === 0) return heat;

  let latestId: string | null = null;
  let latestAt = -Infinity;
  for (const event of enters) {
    const id = event.nodeId!;
    const at = eventTime(event, nowMs);
    const intensity = decayIntensity(nowMs - at, ttlMs);
    if (intensity <= 0) continue;
    const previous = heat.get(id);
    if (!previous || intensity >= previous.intensity) {
      heat.set(id, { intensity, current: false });
    }
    if (at >= latestAt) {
      latestAt = at;
      latestId = id;
    }
  }

  if (latestId && nowMs - latestAt <= LIVE_CURRENT_MS) {
    const row = heat.get(latestId);
    if (row) heat.set(latestId, { ...row, current: true });
  }
  return heat;
}

/**
 * Control-flow edges taken by consecutive NodeEnter pairs (exec trail).
 * Decays with the later enter's age.
 */
export function computeLiveControlEdgeHeat(
  events: LiveDebugEvent[],
  edges: Edge[],
  nowMs: number = Date.now(),
  ttlMs: number = LIVE_HEAT_TTL_MS,
): Map<string, LiveEdgeHeat> {
  const enters = events.filter((e) => isNodeEnter(e.event) && e.nodeId);
  const hot = new Map<string, LiveEdgeHeat>();
  for (let i = 1; i < enters.length; i++) {
    const from = enters[i - 1]!;
    const to = enters[i]!;
    if (!from.nodeId || !to.nodeId || from.nodeId === to.nodeId) continue;
    const at = Math.max(eventTime(from, nowMs), eventTime(to, nowMs));
    const intensity = decayIntensity(nowMs - at, ttlMs);
    if (intensity <= 0) continue;
    const port = from.controlPort?.trim() || null;
    for (const edge of edges) {
      if (edge.data?.kind !== 'control') continue;
      if (edge.source !== from.nodeId || edge.target !== to.nodeId) continue;
      if (port && !controlPortMatchesHandle(port, edge.sourceHandle)) continue;
      const previous = hot.get(edge.id);
      if (!previous || intensity >= previous.intensity) {
        hot.set(edge.id, { intensity, kind: 'control' });
      }
    }
  }
  return hot;
}

/**
 * Value / data edges light when a producer node emits Pin* (or was just entered
 * with pins). Distinct from control exec trail — quiet solid wire + live label.
 */
export function computeLiveValueEdgeHeat(
  events: LiveDebugEvent[],
  edges: Edge[],
  nowMs: number = Date.now(),
  ttlMs: number = LIVE_HEAT_TTL_MS,
): Map<string, LiveEdgeHeat> {
  const hot = new Map<string, LiveEdgeHeat>();
  const producerAt = new Map<string, number>();
  for (const event of events) {
    if (!event.nodeId) continue;
    if (!isPinEvent(event.event) && !isNodeEnter(event.event)) continue;
    const at = eventTime(event, nowMs);
    const previous = producerAt.get(event.nodeId) ?? -Infinity;
    if (at > previous) producerAt.set(event.nodeId, at);
  }

  for (const edge of edges) {
    if (edge.data?.kind !== 'value') continue;
    const at = producerAt.get(edge.source);
    if (at == null) continue;
    const intensity = decayIntensity(nowMs - at, ttlMs);
    if (intensity <= 0) continue;
    hot.set(edge.id, { intensity, kind: 'value' });
  }
  return hot;
}

/** @deprecated Prefer computeLiveControlEdgeHeat — kept for call-site migration. */
export function computeLiveEdgeIds(
  events: LiveDebugEvent[],
  edges: Edge[],
  nowMs: number = Date.now(),
): Set<string> {
  return new Set(computeLiveControlEdgeHeat(events, edges, nowMs).keys());
}

function controlPortMatchesHandle(port: string, sourceHandle: string | null | undefined): boolean {
  const handle = (sourceHandle ?? '').trim();
  if (!handle) return true;
  if (handle === port) return true;
  if ((port === 'next' || port === 'Enter' || port === 'exec')
    && (handle === 'next' || handle === 'exec')) {
    return true;
  }
  return false;
}

/** Latest pin values per node from recent Pin* events (still within TTL). */
export function computeLivePinValues(
  events: LiveDebugEvent[],
  nowMs: number = Date.now(),
  ttlMs: number = LIVE_HEAT_TTL_MS,
): Map<string, LivePinValue[]> {
  const pins = events
    .filter((e) => isPinEvent(e.event) && e.nodeId && e.pinIndex !== undefined)
    .slice(-PIN_WINDOW);
  const byNode = new Map<string, Map<number, { value: string; at: number }>>();
  for (const event of pins) {
    const id = event.nodeId!;
    const index = event.pinIndex!;
    const at = eventTime(event, nowMs);
    if (decayIntensity(nowMs - at, ttlMs) <= 0) continue;
    const map = byNode.get(id) ?? new Map();
    const previous = map.get(index);
    if (!previous || at >= previous.at) {
      map.set(index, { value: formatLiveValue(event.value), at });
    }
    byNode.set(id, map);
  }
  const result = new Map<string, LivePinValue[]>();
  for (const [id, map] of byNode) {
    result.set(
      id,
      [...map.entries()]
        .sort((a, b) => a[0] - b[0])
        .map(([pinIndex, row]) => ({ pinIndex, value: row.value })),
    );
  }
  return result;
}

export function formatLiveValue(value: number | boolean | undefined): string {
  if (typeof value === 'boolean') return value ? 'true' : 'false';
  if (typeof value === 'number') {
    if (Number.isInteger(value)) return String(value);
    return value.toFixed(3).replace(/\.?0+$/, '');
  }
  return '—';
}

/** Prefer a single readable value for an outgoing value port / wire. */
export function pickLiveValueLabel(
  pins: LivePinValue[] | undefined,
  sourceHandle?: string | null,
): string | null {
  if (!pins || pins.length === 0) return null;
  if (pins.length === 1) return pins[0]!.value;
  const handle = (sourceHandle ?? '').trim();
  if (handle === 'list') {
    return pins.map((pin) => pin.value).join(', ');
  }
  // Value ports / unnamed: show lowest pin index (usually the primary result).
  return [...pins].sort((a, b) => a.pinIndex - b.pinIndex)[0]!.value;
}

/** Apply live heat / pin chips onto React Flow node data without mutating originals. */
export function applyLiveDebugToNodes<T extends Record<string, unknown>>(
  nodes: Node<T>[],
  heat: Map<string, LiveNodeHeat>,
  pins: Map<string, LivePinValue[]>,
): Node<T>[] {
  return nodes.map((node) => {
    const nodeHeat = heat.get(node.id);
    const nodePins = pins.get(node.id);
    if (!nodeHeat && (!nodePins || nodePins.length === 0)) {
      if ((node.data as { liveDebug?: unknown }).liveDebug == null) return node;
      const { liveDebug: _drop, ...rest } = node.data as T & { liveDebug?: unknown };
      return { ...node, data: rest as T, className: undefined };
    }
    const liveClass = nodeHeat?.current
      ? 'gas-live-current'
      : nodeHeat && nodeHeat.intensity > 0.66
        ? 'gas-live-hot'
        : nodeHeat
          ? 'gas-live-trail'
          : undefined;
    return {
      ...node,
      className: liveClass,
      data: {
        ...node.data,
        liveDebug: {
          intensity: nodeHeat?.intensity ?? 0,
          current: nodeHeat?.current ?? false,
          pins: nodePins ?? [],
        },
      },
    };
  });
}

export function applyLiveDebugToEdges(
  edges: Edge[],
  controlHeat: Map<string, LiveEdgeHeat> | Set<string>,
  valueHeat: Map<string, LiveEdgeHeat> = new Map(),
  pinValues: Map<string, LivePinValue[]> = new Map(),
): Edge[] {
  const control = controlHeat instanceof Set
    ? new Map([...controlHeat].map((id) => [id, { intensity: 1, kind: 'control' as const }]))
    : controlHeat;

  return edges.map((edge) => {
    const kind = edge.data?.kind === 'value' ? 'value' : 'control';
    const type = kind === 'value' ? 'gasValue' : 'gasControl';
    const ctl = control.get(edge.id);
    const val = valueHeat.get(edge.id);
    const sourcePins = pinValues.get(edge.source);
    const liveValue = kind === 'value'
      ? pickLiveValueLabel(sourcePins, edge.sourceHandle)
      : null;

    if (ctl) {
      return {
        ...edge,
        type,
        animated: false,
        hidden: false,
        className: 'gas-live-edge-control',
        label: undefined,
        data: {
          ...edge.data,
          kind: 'control',
          live: true,
          intensity: ctl.intensity,
          liveValue: null,
        },
        style: {
          ...edge.style,
          strokeDasharray: undefined,
        },
      };
    }
    if (val || liveValue) {
      return {
        ...edge,
        type,
        animated: false,
        hidden: false,
        className: val ? 'gas-live-edge-value' : edge.className,
        label: liveValue ?? undefined,
        data: {
          ...edge.data,
          kind: 'value',
          live: Boolean(val),
          intensity: val?.intensity ?? 0,
          liveValue,
        },
        style: {
          ...edge.style,
          strokeDasharray: undefined,
        },
      };
    }

    const hadLive = edge.className === 'gas-live-edge-control'
      || edge.className === 'gas-live-edge-value'
      || edge.className === 'gas-live-edge'
      || Boolean((edge.data as { live?: boolean } | undefined)?.live);

    if (!hadLive && edge.type === type) return edge;

    return {
      ...edge,
      type,
      animated: false,
      className: undefined,
      label: kind === 'value' ? edge.label : undefined,
      data: {
        ...edge.data,
        kind,
        live: false,
        intensity: 0,
        liveValue: null,
      },
      style: {
        ...edge.style,
        strokeDasharray: undefined,
      },
    };
  });
}

/**
 * Walk control edges from a watched event entry (and its start node) so the
 * canvas can hide everything else. Value edges between focused nodes stay too.
 */
export function computeWatchedEntryFocus(
  nodes: Node[],
  edges: Edge[],
  entryLabel: string,
  eventNodeIdForLabel: (label: string) => string,
): { nodeIds: Set<string>; edgeIds: Set<string> } {
  const label = entryLabel.trim();
  const nodeIds = new Set<string>();
  const edgeIds = new Set<string>();
  if (!label) return { nodeIds, edgeIds };

  const eventId = eventNodeIdForLabel(label);
  const eventNode = nodes.find((node) => node.id === eventId);
  if (eventNode) nodeIds.add(eventNode.id);

  const startFromEntry = (eventNode?.data as { entry?: { start?: string } } | undefined)?.entry?.start?.trim();
  const queue: string[] = [];
  if (startFromEntry) queue.push(startFromEntry);
  for (const edge of edges) {
    if (edge.source !== eventId) continue;
    if (edge.data?.kind && edge.data.kind !== 'control') continue;
    edgeIds.add(edge.id);
    if (edge.target) queue.push(edge.target);
  }

  const control = edges.filter((edge) => edge.data?.kind === 'control' || edge.data?.kind == null);
  const outgoing = new Map<string, Edge[]>();
  for (const edge of control) {
    const list = outgoing.get(edge.source) ?? [];
    list.push(edge);
    outgoing.set(edge.source, list);
  }

  while (queue.length > 0) {
    const id = queue.shift()!;
    if (nodeIds.has(id)) continue;
    nodeIds.add(id);
    for (const edge of outgoing.get(id) ?? []) {
      edgeIds.add(edge.id);
      if (edge.target && !nodeIds.has(edge.target)) queue.push(edge.target);
    }
  }

  for (const edge of edges) {
    if (edge.data?.kind !== 'value') continue;
    if (!nodeIds.has(edge.source) || !nodeIds.has(edge.target)) continue;
    edgeIds.add(edge.id);
  }

  return { nodeIds, edgeIds };
}

export function applyWatchFocusToNodes<T extends Record<string, unknown>>(
  nodes: Node<T>[],
  focusNodeIds: Set<string>,
): Node<T>[] {
  if (focusNodeIds.size === 0) return nodes;
  return nodes.map((node) => {
    if (focusNodeIds.has(node.id)) {
      return {
        ...node,
        hidden: false,
        className: [node.className, 'gas-watch-focus'].filter(Boolean).join(' '),
      };
    }
    return {
      ...node,
      hidden: true,
      className: [node.className, 'gas-watch-dim'].filter(Boolean).join(' '),
    };
  });
}

export function applyWatchFocusToEdges(
  edges: Edge[],
  focusEdgeIds: Set<string>,
  hotEdgeIds: Set<string>,
): Edge[] {
  if (focusEdgeIds.size === 0) return edges;
  return edges.map((edge) => {
    if (hotEdgeIds.has(edge.id)) {
      return { ...edge, hidden: false };
    }
    if (focusEdgeIds.has(edge.id)) {
      const isValue = edge.data?.kind === 'value';
      const live = Boolean((edge.data as { live?: boolean } | undefined)?.live);
      return {
        ...edge,
        type: isValue ? 'gasValue' : 'gasControl',
        hidden: false,
        style: {
          ...edge.style,
          stroke: live
            ? (isValue ? '#94a3b8' : '#fbbf24')
            : (isValue ? '#71717a' : '#52525b'),
          strokeWidth: isValue ? 1.5 : (live ? 3.2 : 2),
          opacity: isValue ? 0.7 : 1,
          strokeDasharray: undefined,
        },
      };
    }
    return {
      ...edge,
      hidden: true,
      animated: false,
    };
  });
}
