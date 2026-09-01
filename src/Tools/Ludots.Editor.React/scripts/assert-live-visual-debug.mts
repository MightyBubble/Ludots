/**
 * Lightweight assertion harness for liveVisualDebug pure helpers.
 * Run: node --experimental-strip-types scripts/assert-live-visual-debug.mts
 */
import {
  applyLiveDebugToEdges,
  applyLiveDebugToNodes,
  computeLiveEdgeIds,
  computeLiveNodeHeat,
  computeLivePinValues,
  formatLiveValue,
  type LiveDebugEvent,
} from '../src/pages/gas-graph-editor/liveVisualDebug.ts';
import type { Edge, Node } from '@xyflow/react';

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message);
}

const events: LiveDebugEvent[] = [
  { sequence: 1, event: 'NodeEnter', nodeId: 'a', controlPort: 'next', steps: 1 },
  { sequence: 2, event: 'PinInt', nodeId: 'a', pinIndex: 0, value: 42, steps: 1 },
  { sequence: 3, event: 'NodeEnter', nodeId: 'b', controlPort: 'true', steps: 2 },
  { sequence: 4, event: 'PinFloat', nodeId: 'b', pinIndex: 1, value: 1.25, steps: 2 },
  { sequence: 5, event: 'NodeEnter', nodeId: 'c', steps: 3 },
];

const heat = computeLiveNodeHeat(events);
assert(heat.get('c')?.current === true, 'latest NodeEnter should be current');
assert((heat.get('a')?.intensity ?? 0) < (heat.get('c')?.intensity ?? 0), 'trail intensity rises toward latest');

const edges: Edge[] = [
  { id: 'e1', source: 'a', target: 'b', sourceHandle: 'next', data: { kind: 'control' } },
  { id: 'e2', source: 'b', target: 'c', sourceHandle: 'true', data: { kind: 'control' } },
  { id: 'e3', source: 'a', target: 'c', sourceHandle: 'next', data: { kind: 'control' } },
];
const hot = computeLiveEdgeIds(events, edges);
assert(hot.has('e1'), 'a→b control edge should light');
assert(hot.has('e2'), 'b→c control edge should light');
assert(!hot.has('e3'), 'unrelated a→c should stay cold');

const pins = computeLivePinValues(events);
assert(pins.get('a')?.[0]?.value === '42', 'pin int formats');
assert(pins.get('b')?.[0]?.value === '1.25', 'pin float formats');
assert(formatLiveValue(true) === 'true', 'bool true');

const nodes: Node<Record<string, unknown>>[] = [
  { id: 'a', position: { x: 0, y: 0 }, data: { op: 'ConstInt' } },
  { id: 'c', position: { x: 0, y: 0 }, data: { op: 'BranchBool' } },
];
const lit = applyLiveDebugToNodes(nodes, heat, pins);
assert(lit.find((n) => n.id === 'c')?.className === 'gas-live-current', 'current class');
assert(Array.isArray((lit.find((n) => n.id === 'a')?.data as { liveDebug?: { pins: unknown[] } }).liveDebug?.pins), 'pins attached');

const litEdges = applyLiveDebugToEdges(edges, hot);
assert(litEdges.find((e) => e.id === 'e1')?.animated === true, 'hot edge animated');
assert(litEdges.find((e) => e.id === 'e3')?.animated !== true, 'cold edge not animated');

console.log('assert-live-visual-debug: ok');
