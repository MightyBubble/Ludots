/**
 * Checks every mod's graph_editor.json annotations against the graphs.json they describe,
 * then pins the dock behaviour that made the old build unreadable: groups arrive in walk
 * order and go quiet on the same TTL as the canvas heat.
 *
 * Run: node --experimental-strip-types scripts/assert-graph-annotations.mts
 */
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
import {
  lookupEntryStory,
  parseGraphAnnotations,
  resolveWalkedGroups,
  type GraphAnnotations,
} from '../src/pages/gas-graph-editor/graphAnnotations.ts';
import { LIVE_HEAT_TTL_MS, type LiveDebugEvent } from '../src/pages/gas-graph-editor/liveVisualDebug.ts';

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message);
}

const repoRoot = join(import.meta.dirname, '..', '..', '..', '..');

function findSidecars(dir: string, found: string[] = []): string[] {
  for (const name of readdirSync(dir)) {
    if (name === 'node_modules' || name === 'bin' || name === 'obj' || name.startsWith('.')) continue;
    const path = join(dir, name);
    if (statSync(path).isDirectory()) findSidecars(path, found);
    else if (name === 'graph_editor.json') found.push(path);
  }
  return found;
}

let checkedGraphs = 0;
let checkedGroups = 0;

for (const sidecarPath of findSidecars(join(repoRoot, 'mods'))) {
  const root = JSON.parse(readFileSync(sidecarPath, 'utf8')) as Record<string, unknown>;
  const graphsPath = join(sidecarPath, '..', 'graphs.json');
  const graphs = JSON.parse(readFileSync(graphsPath, 'utf8')) as {
    id?: string;
    nodes?: { id?: string }[];
    entries?: { label?: string }[];
  }[];

  const layouts = root.graphs;
  assert(
    layouts != null && typeof layouts === 'object' && !Array.isArray(layouts),
    `${sidecarPath} must contain an object property 'graphs'.`,
  );

  for (const [graphId, layoutRaw] of Object.entries(layouts as Record<string, unknown>)) {
    const layout = layoutRaw as { annotations?: unknown };
    let annotations: GraphAnnotations;
    try {
      annotations = parseGraphAnnotations(layout.annotations);
    } catch (err) {
      throw new Error(`${sidecarPath} graph '${graphId}': ${err instanceof Error ? err.message : String(err)}`);
    }
    if (annotations.groups.length === 0 && Object.keys(annotations.entries).length === 0) continue;

    const graph = graphs.find((row) => row.id === graphId);
    assert(graph != null, `${sidecarPath} annotates graph '${graphId}', absent from ${graphsPath}.`);

    const nodeIds = new Set((graph.nodes ?? []).map((node) => node.id));
    for (const group of annotations.groups) {
      for (const nodeId of group.nodes) {
        assert(
          nodeIds.has(nodeId),
          `${sidecarPath} group '${group.id}' names node '${nodeId}', absent from graph '${graphId}'.`,
        );
      }
      checkedGroups += 1;
    }

    const entryLabels = new Set((graph.entries ?? []).map((entry) => entry.label));
    for (const label of Object.keys(annotations.entries)) {
      assert(
        entryLabels.has(label),
        `${sidecarPath} annotates entry '${label}', absent from graph '${graphId}'.`,
      );
      assert(lookupEntryStory(annotations, label) != null, `entry story lookup failed for '${label}'.`);
    }

    checkedGraphs += 1;
    console.log(
      `  ${graphId}: ${annotations.groups.length} groups, `
      + `${Object.keys(annotations.entries).length} entry stories — all targets exist`,
    );
  }
}

assert(checkedGraphs > 0, 'no annotated graphs found; the walk-order checks below would be vacuous.');

// One drain batch stamps every record with the same receive time, which is what a
// single-tick chain looks like. All of its groups must read out in walk order.
const annotations = parseGraphAnnotations({
  groups: [
    { id: 'first', text: 'first', nodes: ['a1', 'a2'] },
    { id: 'second', text: 'second', nodes: ['b1'] },
    { id: 'third', text: 'third', nodes: ['c1'] },
  ],
});
const drainAt = 500_000;
const batch: LiveDebugEvent[] = ['a1', 'a2', 'b1', 'c1'].map((nodeId, index) => ({
  sequence: index + 1,
  event: 'NodeEnter',
  nodeId,
  steps: index + 1,
  atMs: drainAt,
}));

const walked = resolveWalkedGroups(annotations, batch, drainAt, LIVE_HEAT_TTL_MS);
assert(
  walked.map((group) => group.id).join(',') === 'first,second,third',
  `whole run must read out in walk order, got '${walked.map((group) => group.id).join(',')}'`,
);

const midRun = resolveWalkedGroups(annotations, batch, drainAt + LIVE_HEAT_TTL_MS - 1, LIVE_HEAT_TTL_MS);
assert(midRun.length === 3, 'groups stay readable for the whole heat window');

const cooled = resolveWalkedGroups(annotations, batch, drainAt + LIVE_HEAT_TTL_MS, LIVE_HEAT_TTL_MS);
assert(cooled.length === 0, 'groups must go quiet exactly when the canvas heat does');

const longCooled = resolveWalkedGroups(annotations, batch, drainAt + 60_000, LIVE_HEAT_TTL_MS);
assert(longCooled.length === 0, 'a finished run must not keep narrating itself');

// A node claimed by two groups is an authoring mistake, not something to resolve silently.
let rejectedDoubleClaim = false;
try {
  parseGraphAnnotations({
    groups: [
      { id: 'one', text: 'one', nodes: ['shared'] },
      { id: 'two', text: 'two', nodes: ['shared'] },
    ],
  });
} catch {
  rejectedDoubleClaim = true;
}
assert(rejectedDoubleClaim, 'a node claimed by two groups must be rejected');

let rejectedBlankText = false;
try {
  parseGraphAnnotations({ groups: [{ id: 'one', text: '  ', nodes: ['a'] }] });
} catch {
  rejectedBlankText = true;
}
assert(rejectedBlankText, 'a group without prose must be rejected');

console.log(`assert-graph-annotations: ok (${checkedGraphs} graphs, ${checkedGroups} groups)`);
