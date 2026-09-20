import type { Edge, Node } from '@xyflow/react';

export const EVENT_ENTRY_PREFIX = '@entry:';

export function eventEntryNodeId(label: string): string {
  return `${EVENT_ENTRY_PREFIX}${label}`;
}

export function isEventEntryNodeId(id: string): boolean {
  return id.startsWith(EVENT_ENTRY_PREFIX);
}

type LayoutNodeData = {
  role?: 'op' | 'event-entry';
  controlOutputPorts?: string[];
  descriptor?: {
    linearInputPorts: string[];
    queryInputPorts: string[];
    scriptInputPorts: string[];
  };
  sugar?: { valueInputPorts: string[] };
};

type LayoutEdgeData = {
  kind?: 'control' | 'value';
};

const RANK_GAP_X = 300;
const NODE_GAP_Y = 36;
const ORIGIN_X = 48;
const ORIGIN_Y = 48;
const BASE_HEIGHT = 64;
const PIN_ROW_HEIGHT = 18;

export function estimateNodeHeight(node: Node<LayoutNodeData>): number {
  const inputs = new Set([
    ...(node.data.descriptor?.linearInputPorts ?? []),
    ...(node.data.descriptor?.queryInputPorts ?? []),
    ...(node.data.descriptor?.scriptInputPorts ?? []),
    ...(node.data.sugar?.valueInputPorts ?? []),
  ]).size;
  const outputs = Math.max(1, node.data.controlOutputPorts?.length ?? 1);
  const rows = Math.max(2, inputs + 1, outputs + 1);
  return BASE_HEIGHT + rows * PIN_ROW_HEIGHT;
}

export function computeAutoLayout(
  nodes: Node<LayoutNodeData>[],
  edges: Edge<LayoutEdgeData>[],
): Record<string, { x: number; y: number }> {
  const ids = nodes.map((node) => node.id);
  const incoming = new Map<string, string[]>();
  const outgoing = new Map<string, string[]>();
  for (const id of ids) {
    incoming.set(id, []);
    outgoing.set(id, []);
  }

  for (const edge of edges) {
    if (!incoming.has(edge.target) || !outgoing.has(edge.source)) continue;
    incoming.get(edge.target)!.push(edge.source);
    outgoing.get(edge.source)!.push(edge.target);
  }

  const rank = new Map<string, number>();
  const visit = (id: string, nextRank: number, stack: Set<string>) => {
    if (stack.has(id)) return;
    if ((rank.get(id) ?? -1) >= nextRank) return;
    rank.set(id, nextRank);
    stack.add(id);
    for (const child of outgoing.get(id) ?? []) {
      visit(child, nextRank + 1, stack);
    }
    stack.delete(id);
  };

  const roots = ids.filter((id) => {
    const node = nodes.find((entry) => entry.id === id);
    if (node?.data.role === 'event-entry') return true;
    return (incoming.get(id)?.length ?? 0) === 0;
  });
  for (const root of roots) visit(root, 0, new Set());
  for (const id of ids) {
    if (!rank.has(id)) rank.set(id, 0);
  }

  const layers = new Map<number, string[]>();
  for (const id of ids) {
    const layer = rank.get(id) ?? 0;
    const list = layers.get(layer) ?? [];
    list.push(id);
    layers.set(layer, list);
  }

  const sortedRanks = [...layers.keys()].sort((a, b) => a - b);
  for (const layer of sortedRanks) {
    const list = layers.get(layer)!;
    list.sort((left, right) => {
      const parentIndex = (id: string) => {
        const parents = incoming.get(id) ?? [];
        if (parents.length === 0) return 0;
        const sum = parents.reduce((total, parent) => {
          const parentLayer = layers.get(rank.get(parent) ?? 0) ?? [];
          return total + parentLayer.indexOf(parent);
        }, 0);
        return sum / parents.length;
      };
      const leftScore = parentIndex(left);
      const rightScore = parentIndex(right);
      if (leftScore !== rightScore) return leftScore - rightScore;
      const leftNode = nodes.find((node) => node.id === left);
      const rightNode = nodes.find((node) => node.id === right);
      if ((leftNode?.data.role === 'event-entry') !== (rightNode?.data.role === 'event-entry')) {
        return leftNode?.data.role === 'event-entry' ? -1 : 1;
      }
      return left.localeCompare(right);
    });
  }

  const positions: Record<string, { x: number; y: number }> = {};
  for (const layer of sortedRanks) {
    const list = layers.get(layer)!;
    let y = ORIGIN_Y;
    for (const id of list) {
      const node = nodes.find((entry) => entry.id === id);
      positions[id] = { x: ORIGIN_X + layer * RANK_GAP_X, y };
      y += (node ? estimateNodeHeight(node) : 96) + NODE_GAP_Y;
    }
  }
  return positions;
}
