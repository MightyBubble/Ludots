/**
 * Lightweight assertion harness for liveVisualDebug pure helpers.
 * Run: node --experimental-strip-types scripts/assert-live-visual-debug.mts
 */
import {
  applyLiveDebugToEdges,
  applyLiveDebugToNodes,
  applyWatchFocusToEdges,
  applyWatchFocusToNodes,
  computeLiveControlEdgeHeat,
  computeLiveNodeHeat,
  computeLivePinValues,
  computeLiveValueEdgeHeat,
  computeWatchedEntryFocus,
  formatLiveValue,
  LIVE_HEAT_TTL_MS,
  type LiveDebugEvent,
} from '../src/pages/gas-graph-editor/liveVisualDebug.ts';
import type { Edge, Node } from '@xyflow/react';

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message);
}

const now = 1_000_000;
const events: LiveDebugEvent[] = [
  { sequence: 1, event: 'NodeEnter', nodeId: 'a', controlPort: 'next', steps: 1, atMs: now - 80 },
  { sequence: 2, event: 'PinInt', nodeId: 'a', pinIndex: 0, value: 42, steps: 1, atMs: now - 70 },
  { sequence: 3, event: 'NodeEnter', nodeId: 'b', controlPort: 'true', steps: 2, atMs: now - 40 },
  { sequence: 4, event: 'PinFloat', nodeId: 'b', pinIndex: 1, value: 1.25, steps: 2, atMs: now - 30 },
  { sequence: 5, event: 'NodeEnter', nodeId: 'c', steps: 3, atMs: now - 10 },
];

const heat = computeLiveNodeHeat(events, now);
assert(heat.get('c')?.current === true, 'latest NodeEnter should be current');
assert((heat.get('a')?.intensity ?? 0) < (heat.get('c')?.intensity ?? 0), 'trail intensity rises toward latest');

const cooled = computeLiveNodeHeat(events, now + LIVE_HEAT_TTL_MS + 50);
assert(cooled.size === 0, 'heat must fully cool after TTL');

const edges: Edge[] = [
  { id: 'e1', source: 'a', target: 'b', sourceHandle: 'next', data: { kind: 'control' } },
  { id: 'e2', source: 'b', target: 'c', sourceHandle: 'true', data: { kind: 'control' } },
  { id: 'e3', source: 'a', target: 'c', sourceHandle: 'next', data: { kind: 'control' } },
  { id: 'v1', source: 'a', target: 'b', sourceHandle: 'value', data: { kind: 'value' } },
];
const controlHeat = computeLiveControlEdgeHeat(events, edges, now);
assert(controlHeat.has('e1'), 'a→b control edge should light');
assert(controlHeat.has('e2'), 'b→c control edge should light');
assert(!controlHeat.has('e3'), 'unrelated a→c should stay cold');
assert(controlHeat.get('e1')?.kind === 'control', 'control heat kind');

const valueHeat = computeLiveValueEdgeHeat(events, edges, now);
assert(valueHeat.has('v1'), 'value edge from pin producer should light');
assert(valueHeat.get('v1')?.kind === 'value', 'value heat kind');

const pins = computeLivePinValues(events, now);
assert(pins.get('a')?.[0]?.value === '42', 'pin int formats');
assert(pins.get('b')?.[0]?.value === '1.25', 'pin float formats');
assert(formatLiveValue(true) === 'true', 'bool true');
assert(computeLivePinValues(events, now + LIVE_HEAT_TTL_MS + 50).size === 0, 'pins cool with TTL');

const nodes: Node<Record<string, unknown>>[] = [
  { id: 'a', position: { x: 0, y: 0 }, data: { op: 'ConstInt' } },
  { id: 'c', position: { x: 0, y: 0 }, data: { op: 'BranchBool' } },
];
const lit = applyLiveDebugToNodes(nodes, heat, pins);
assert(lit.find((n) => n.id === 'c')?.className === 'gas-live-current', 'current class');
assert(Array.isArray((lit.find((n) => n.id === 'a')?.data as { liveDebug?: { pins: unknown[] } }).liveDebug?.pins), 'pins attached');

const litEdges = applyLiveDebugToEdges(edges, controlHeat, valueHeat);
assert(litEdges.find((e) => e.id === 'e1')?.className === 'gas-live-edge-control', 'hot control class');
assert(litEdges.find((e) => e.id === 'v1')?.className === 'gas-live-edge-value', 'hot value class');
assert(litEdges.find((e) => e.id === 'e3')?.className !== 'gas-live-edge-control', 'cold edge not control-lit');

const focusNodes: Node<Record<string, unknown>>[] = [
  { id: '@entry:on_teleport', position: { x: 0, y: 0 }, data: { entry: { start: 'tp_px' } } },
  { id: 'tp_px', position: { x: 0, y: 0 }, data: { op: 'LoadEntryPayloadFloat' } },
  { id: 'tp_ground', position: { x: 0, y: 0 }, data: { op: 'ScreenPointToGround' } },
  { id: 'noise', position: { x: 0, y: 0 }, data: { op: 'ConstInt' } },
];
const focusEdges: Edge[] = [
  { id: 'c1', source: '@entry:on_teleport', target: 'tp_px', data: { kind: 'control' } },
  { id: 'c2', source: 'tp_px', target: 'tp_ground', data: { kind: 'control' } },
  { id: 'vFocus', source: 'tp_px', target: 'tp_ground', data: { kind: 'value' } },
  { id: 'c3', source: 'noise', target: 'tp_px', data: { kind: 'control' } },
];
const focus = computeWatchedEntryFocus(focusNodes, focusEdges, 'on_teleport', (label) => `@entry:${label}`);
assert(focus.nodeIds.has('tp_px') && focus.nodeIds.has('tp_ground'), 'focus walks entry start chain');
assert(!focus.nodeIds.has('noise'), 'unrelated nodes stay out of focus');
assert(focus.edgeIds.has('vFocus'), 'value edges between focus nodes stay framed');
const framed = applyWatchFocusToNodes(focusNodes, focus.nodeIds);
assert(framed.find((n) => n.id === 'tp_ground')?.hidden !== true, 'focus node visible');
assert(framed.find((n) => n.id === 'noise')?.hidden === true, 'noise node hidden while watching');
const framedEdges = applyWatchFocusToEdges(focusEdges, focus.edgeIds, new Set());
assert(framedEdges.find((e) => e.id === 'c2')?.hidden !== true, 'focus control edge visible');
assert(framedEdges.find((e) => e.id === 'vFocus')?.hidden !== true, 'focus value edge visible');
assert(framedEdges.find((e) => e.id === 'c3')?.hidden === true, 'noise edge hidden');

console.log('assert-live-visual-debug: ok');
