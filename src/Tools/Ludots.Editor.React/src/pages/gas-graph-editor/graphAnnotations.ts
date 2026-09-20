import type { LiveDebugEvent } from './liveVisualDebug';

/** A named span of nodes the author described in plain prose. */
export type GraphAnnotationGroup = {
  id: string;
  text: string;
  nodes: string[];
};

/** Per-entry headline the Live Debug dock shows above the walked groups. */
export type GraphAnnotationEntryStory = {
  title: string;
  summary: string;
};

export type GraphAnnotations = {
  groups: GraphAnnotationGroup[];
  entries: Record<string, GraphAnnotationEntryStory>;
};

export const EMPTY_GRAPH_ANNOTATIONS: GraphAnnotations = { groups: [], entries: {} };

function requireText(owner: Record<string, unknown>, field: string, path: string): string {
  const raw = owner[field];
  if (typeof raw !== 'string' || raw.trim() === '' || raw !== raw.trim()) {
    throw new Error(`${path}.${field} must be a trimmed non-empty string.`);
  }
  return raw;
}

/**
 * Narrow the sidecar payload, restating the Bridge contract so a malformed or stale
 * annotation surfaces as an error instead of a dock that quietly says nothing.
 */
export function parseGraphAnnotations(raw: unknown): GraphAnnotations {
  if (raw == null) return EMPTY_GRAPH_ANNOTATIONS;
  if (typeof raw !== 'object' || Array.isArray(raw)) {
    throw new Error('layout.annotations must be an object.');
  }

  const source = raw as Record<string, unknown>;
  const groups: GraphAnnotationGroup[] = [];
  const claimedNodes = new Map<string, string>();

  if (source.groups != null) {
    if (!Array.isArray(source.groups)) throw new Error('layout.annotations.groups must be an array.');
    source.groups.forEach((groupRaw, index) => {
      if (typeof groupRaw !== 'object' || groupRaw == null || Array.isArray(groupRaw)) {
        throw new Error(`layout.annotations.groups[${index}] must be an object.`);
      }
      const group = groupRaw as Record<string, unknown>;
      const id = requireText(group, 'id', `layout.annotations.groups[${index}]`);
      const text = requireText(group, 'text', `layout.annotations.groups[${index}]`);
      if (!Array.isArray(group.nodes) || group.nodes.length === 0) {
        throw new Error(`layout.annotations.groups['${id}'].nodes must be a non-empty array.`);
      }
      const nodes = group.nodes.map((nodeRaw, nodeIndex) => {
        if (typeof nodeRaw !== 'string' || nodeRaw.trim() === '' || nodeRaw !== nodeRaw.trim()) {
          throw new Error(`layout.annotations.groups['${id}'].nodes[${nodeIndex}] must be a trimmed non-empty string.`);
        }
        const owner = claimedNodes.get(nodeRaw);
        if (owner != null) {
          throw new Error(
            `layout.annotations.groups['${id}'] claims node '${nodeRaw}' already claimed by group '${owner}'; `
            + 'a node belongs to at most one group.',
          );
        }
        claimedNodes.set(nodeRaw, id);
        return nodeRaw;
      });
      if (groups.some((existing) => existing.id === id)) {
        throw new Error(`layout.annotations.groups[${index}] duplicates group id '${id}'.`);
      }
      groups.push({ id, text, nodes });
    });
  }

  const entries: Record<string, GraphAnnotationEntryStory> = {};
  if (source.entries != null) {
    if (typeof source.entries !== 'object' || Array.isArray(source.entries)) {
      throw new Error('layout.annotations.entries must be an object.');
    }
    for (const [label, storyRaw] of Object.entries(source.entries as Record<string, unknown>)) {
      if (typeof storyRaw !== 'object' || storyRaw == null || Array.isArray(storyRaw)) {
        throw new Error(`layout.annotations.entries['${label}'] must be an object.`);
      }
      const story = storyRaw as Record<string, unknown>;
      entries[label] = {
        title: requireText(story, 'title', `layout.annotations.entries['${label}']`),
        summary: requireText(story, 'summary', `layout.annotations.entries['${label}']`),
      };
    }
  }

  return { groups, entries };
}

export function lookupEntryStory(
  annotations: GraphAnnotations,
  entryLabel: string,
): GraphAnnotationEntryStory | null {
  return annotations.entries[entryLabel.trim()] ?? null;
}

/**
 * Groups walked in this run, in the order execution reached them.
 *
 * Callers pass the canvas heat TTL so the prose and the lit nodes always agree: a chain
 * that finishes inside one tick lands its whole group sequence at once, and both go quiet
 * together instead of the dock claiming a run is still in flight.
 */
export function resolveWalkedGroups(
  annotations: GraphAnnotations,
  events: LiveDebugEvent[],
  nowMs: number,
  ttlMs: number,
): GraphAnnotationGroup[] {
  if (annotations.groups.length === 0) return [];

  const groupByNode = new Map<string, GraphAnnotationGroup>();
  for (const group of annotations.groups) {
    for (const nodeId of group.nodes) groupByNode.set(nodeId, group);
  }

  const walked: GraphAnnotationGroup[] = [];
  const seen = new Set<string>();
  for (const event of events) {
    if (event.event !== 'NodeEnter' || !event.nodeId) continue;
    const at = typeof event.atMs === 'number' ? event.atMs : nowMs;
    if (nowMs - at >= ttlMs) continue;
    const group = groupByNode.get(event.nodeId);
    if (!group || seen.has(group.id)) continue;
    seen.add(group.id);
    walked.push(group);
  }

  return walked;
}
