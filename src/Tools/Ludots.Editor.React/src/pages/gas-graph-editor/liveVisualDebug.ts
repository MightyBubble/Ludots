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
};

export type LiveNodeHeat = {
  /** 0..1 — 1 is the hottest / most recent */
  intensity: number;
  /** true if this node was the latest NodeEnter */
  current: boolean;
};

export type LivePinValue = {
  pinIndex: number;
  value: string;
};

const HEAT_WINDOW = 48;
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

/** Build per-node heat from recent NodeEnter events (Flow Canvas style trail). */
export function computeLiveNodeHeat(events: LiveDebugEvent[]): Map<string, LiveNodeHeat> {
  const enters = events.filter((e) => isNodeEnter(e.event) && e.nodeId);
  const window = enters.slice(-HEAT_WINDOW);
  const heat = new Map<string, LiveNodeHeat>();
  if (window.length === 0) return heat;

  const latestId = window[window.length - 1]!.nodeId!;
  for (let i = 0; i < window.length; i++) {
    const id = window[i]!.nodeId!;
    const intensity = (i + 1) / window.length;
    const previous = heat.get(id);
    if (!previous || intensity >= previous.intensity) {
      heat.set(id, { intensity, current: id === latestId });
    } else if (id === latestId) {
      heat.set(id, { intensity: previous.intensity, current: true });
    }
  }
  return heat;
}

/**
 * Highlight control edges taken during the recent trail:
 * consecutive NodeEnter pairs, optionally matched by controlPort → sourceHandle.
 */
export function computeLiveEdgeIds(
  events: LiveDebugEvent[],
  edges: Edge[],
): Set<string> {
  const enters = events.filter((e) => isNodeEnter(e.event) && e.nodeId).slice(-HEAT_WINDOW);
  const hot = new Set<string>();
  for (let i = 1; i < enters.length; i++) {
    const from = enters[i - 1]!;
    const to = enters[i]!;
    if (!from.nodeId || !to.nodeId || from.nodeId === to.nodeId) continue;
    const port = from.controlPort?.trim() || null;
    for (const edge of edges) {
      if (edge.data?.kind !== 'control') continue;
      if (edge.source !== from.nodeId || edge.target !== to.nodeId) continue;
      if (port && !controlPortMatchesHandle(port, edge.sourceHandle)) continue;
      hot.add(edge.id);
    }
  }
  return hot;
}

function controlPortMatchesHandle(port: string, sourceHandle: string | null | undefined): boolean {
  const handle = (sourceHandle ?? '').trim();
  if (!handle) return true;
  if (handle === port) return true;
  // Event Then / generic next often author as exec while source map says next/Enter.
  if ((port === 'next' || port === 'Enter' || port === 'exec')
    && (handle === 'next' || handle === 'exec')) {
    return true;
  }
  return false;
}

/** Latest pin values per node from recent Pin* events. */
export function computeLivePinValues(events: LiveDebugEvent[]): Map<string, LivePinValue[]> {
  const pins = events.filter((e) => isPinEvent(e.event) && e.nodeId && e.pinIndex !== undefined).slice(-PIN_WINDOW);
  const byNode = new Map<string, Map<number, string>>();
  for (const event of pins) {
    const id = event.nodeId!;
    const index = event.pinIndex!;
    const map = byNode.get(id) ?? new Map<number, string>();
    map.set(index, formatLiveValue(event.value));
    byNode.set(id, map);
  }
  const result = new Map<string, LivePinValue[]>();
  for (const [id, map] of byNode) {
    result.set(
      id,
      [...map.entries()]
        .sort((a, b) => a[0] - b[0])
        .map(([pinIndex, value]) => ({ pinIndex, value })),
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

export function heatBorderColor(intensity: number, current: boolean): string {
  if (current) return '#4ade80';
  if (intensity > 0.66) return '#22d3ee';
  if (intensity > 0.33) return '#38bdf8';
  return '#64748b';
}

export function heatGlow(intensity: number, current: boolean): string {
  if (current) return '0 0 22px rgba(74,222,128,.65)';
  const alpha = 0.25 + intensity * 0.45;
  return `0 0 ${12 + intensity * 14}px rgba(34,211,238,${alpha.toFixed(2)})`;
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
    return {
      ...node,
      className: nodeHeat?.current
        ? 'gas-live-current'
        : nodeHeat
          ? 'gas-live-hot'
          : node.className,
      data: {
        ...node.data,
        liveDebug: {
          intensity: nodeHeat?.intensity ?? 0,
          current: nodeHeat?.current ?? false,
          pins: nodePins ?? [],
        },
      },
      style: {
        ...node.style,
        ...(nodeHeat
          ? {
              border: `2px solid ${heatBorderColor(nodeHeat.intensity, nodeHeat.current)}`,
              boxShadow: heatGlow(nodeHeat.intensity, nodeHeat.current),
            }
          : {}),
      },
    };
  });
}

export function applyLiveDebugToEdges(
  edges: Edge[],
  hotEdgeIds: Set<string>,
): Edge[] {
  if (hotEdgeIds.size === 0) return edges;
  return edges.map((edge) => {
    if (!hotEdgeIds.has(edge.id)) {
      if (edge.className !== 'gas-live-edge' && !edge.animated) return edge;
      return {
        ...edge,
        animated: false,
        className: undefined,
      };
    }
    return {
      ...edge,
      animated: true,
      style: {
        ...edge.style,
        stroke: '#4ade80',
        strokeWidth: 3,
      },
      className: 'gas-live-edge',
    };
  });
}

/**
 * Walk control edges from a watched event entry (and its start node) so the
 * canvas can dim everything else and frame only the story being watched.
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
  // Also accept Then edges leaving the event card.
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
    // Hide the rest so Watch is a short story, not a dimmed blob.
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
      return {
        ...edge,
        hidden: false,
        style: {
          ...edge.style,
          stroke: '#67e8f9',
          strokeWidth: 2,
          opacity: 1,
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
