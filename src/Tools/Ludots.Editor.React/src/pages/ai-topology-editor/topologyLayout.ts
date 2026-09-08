import type { Edge, Node } from '@xyflow/react';

const GAP_X = 220;
const GAP_Y = 120;
const ORIGIN_X = 64;
const ORIGIN_Y = 48;

/** Depth-first column layout for parent→child topology graphs. */
export function computeTopologyTreeLayout(
  nodes: Node[],
  edges: Edge[],
  rootId: string,
): Record<string, { x: number; y: number }> {
  const children = new Map<string, string[]>();
  for (const edge of edges) {
    if (edge.data?.kind === 'transition') continue;
    const list = children.get(edge.source) ?? [];
    list.push(edge.target);
    children.set(edge.source, list);
  }

  const positions: Record<string, { x: number; y: number }> = {};
  let leafCursor = 0;

  const place = (id: string, depth: number): number => {
    const kids = children.get(id) ?? [];
    if (kids.length === 0) {
      const x = ORIGIN_X + leafCursor * GAP_X;
      positions[id] = { x, y: ORIGIN_Y + depth * GAP_Y };
      leafCursor += 1;
      return x;
    }

    const childXs = kids.map((child) => place(child, depth + 1));
    const x = (Math.min(...childXs) + Math.max(...childXs)) / 2;
    positions[id] = { x, y: ORIGIN_Y + depth * GAP_Y };
    return x;
  };

  if (nodes.some((n) => n.id === rootId)) {
    place(rootId, 0);
  }

  // Orphans (not reachable from root) get a side column.
  for (const node of nodes) {
    if (positions[node.id]) continue;
    positions[node.id] = {
      x: ORIGIN_X + leafCursor * GAP_X,
      y: ORIGIN_Y,
    };
    leafCursor += 1;
  }

  return positions;
}
