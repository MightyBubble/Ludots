import { EVENT_ENTRY_PREFIX, eventEntryNodeId, isEventEntryNodeId } from './autoLayout';
import type { GasNodeViewEntry } from './GasNode';

export type EventEntryFilters = NonNullable<GasNodeViewEntry['filters']>;
export type EventEntryConfig = GasNodeViewEntry;

export const EVENT_REFIRE_IGNORE = 'ignore';
export const EVENT_REFIRE_RESTART = 'restart';
export const EVENT_DIRECTIONS = ['cross_above', 'cross_below'] as const;

export function uniqueEventLabel(used: Iterable<string>, base = 'on_event'): string {
  const taken = new Set(used);
  if (!taken.has(base)) return base;
  let suffix = 1;
  let label = `${base}_${suffix}`;
  while (taken.has(label)) {
    suffix += 1;
    label = `${base}_${suffix}`;
  }
  return label;
}

export function entryLabelsFromNodes(nodes: { data: { role?: string; entry?: EventEntryConfig } }[]): string[] {
  return nodes
    .filter((node) => node.data.role === 'event-entry' && node.data.entry?.label)
    .map((node) => node.data.entry!.label);
}

export function sanitizeEventFilters(filters?: EventEntryFilters | null): EventEntryFilters | undefined {
  if (!filters) return undefined;
  const next: EventEntryFilters = {};
  const region = filters.region?.trim();
  const tag = filters.tag?.trim();
  const action = filters.action?.trim();
  const direction = filters.direction?.trim();
  const instanceId = filters.instanceId?.trim();
  const varName = filters.varName?.trim();
  if (region) next.region = region;
  if (tag) next.tag = tag;
  if (action) next.action = action;
  if (instanceId) next.instanceId = instanceId;
  if (varName) next.varName = varName;
  if (filters.team != null && Number.isInteger(filters.team)) next.team = filters.team;
  if (filters.threshold != null && Number.isFinite(filters.threshold)) next.threshold = filters.threshold;
  if (direction) next.direction = direction;
  return Object.keys(next).length > 0 ? next : undefined;
}

export function toWireEventEntry(entry: EventEntryConfig, start: string): EventEntryConfig {
  const filters = sanitizeEventFilters(entry.filters);
  const refire = entry.refire?.trim();
  const event = entry.event?.trim() || undefined;
  const action = entry.action?.trim() || undefined;
  return {
    label: (entry.label ?? '').trim(),
    start: start.trim(),
    ...(event ? { event } : {}),
    ...(action ? { action } : {}),
    ...(entry.once ? { once: true } : {}),
    ...(refire && refire !== EVENT_REFIRE_IGNORE ? { refire } : {}),
    ...(filters ? { filters } : {}),
  };
}

export function eventStartFromEdges(
  nodeId: string,
  edges: { source: string; sourceHandle?: string | null; target: string }[],
): string {
  const wired = edges.find((edge) => edge.source === nodeId && (edge.sourceHandle === 'exec' || edge.sourceHandle == null));
  return wired?.target ?? '';
}

export function collectEventEntries(
  nodes: { id: string; data: { role?: string; entry?: EventEntryConfig } }[],
  edges: { source: string; sourceHandle?: string | null; target: string }[],
): EventEntryConfig[] {
  return nodes
    .filter((node) => node.data.role === 'event-entry')
    .map((node) => {
      const fallbackLabel = isEventEntryNodeId(node.id) ? node.id.slice(EVENT_ENTRY_PREFIX.length) : node.id;
      const entry = node.data.entry ?? { label: fallbackLabel, event: '', start: '' };
      return toWireEventEntry(entry, eventStartFromEdges(node.id, edges) || entry.start);
    });
}

export function createEmptyEventEntry(label: string): EventEntryConfig {
  return { label, event: '', start: '' };
}

export function eventThenEdgeId(source: string, target: string): string {
  return `c:${source}:exec:${target}`;
}

export function parseOptionalInt(value: string): number | null {
  const trimmed = value.trim();
  if (trimmed === '') return null;
  const parsed = Number.parseInt(trimmed, 10);
  return Number.isInteger(parsed) ? parsed : null;
}

export function parseOptionalFloat(value: string): number | null {
  const trimmed = value.trim();
  if (trimmed === '') return null;
  const parsed = Number.parseFloat(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}
