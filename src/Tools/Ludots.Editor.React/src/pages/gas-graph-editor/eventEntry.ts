import { EVENT_ENTRY_PREFIX, isEventEntryNodeId } from './autoLayout';
import type { GasNodeViewEntry } from './GasNode';

export type EventEntryFilters = NonNullable<GasNodeViewEntry['filters']>;
export type EventEntryConfig = GasNodeViewEntry;

export const EVENT_REFIRE_IGNORE = 'ignore';
export const EVENT_REFIRE_RESTART = 'restart';
export const EVENT_DIRECTIONS = ['cross_above', 'cross_below'] as const;

/** What starts the chain. The runtime requires exactly one of these per entry. */
export type EntryTriggerKind = 'event' | 'action';

export function entryTriggerKind(entry: EventEntryConfig): EntryTriggerKind {
  return entry.action != null ? 'action' : 'event';
}

export function entryTriggerName(entry: EventEntryConfig): string {
  return (entryTriggerKind(entry) === 'action' ? entry.action : entry.event) ?? '';
}

/**
 * Single writer for the event / action choice. Keeping both fields out of reach of the
 * individual form controls is what stops the editor from authoring an entry that names
 * both or neither — shapes the runtime refuses to mount.
 */
export function setEntryTrigger(
  entry: EventEntryConfig,
  kind: EntryTriggerKind,
  name: string,
): EventEntryConfig {
  const { event: _event, action: _action, ...rest } = entry;
  return kind === 'action' ? { ...rest, action: name } : { ...rest, event: name };
}

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
  const kind = entryTriggerKind(entry);
  const name = entryTriggerName(entry).trim();
  return {
    label: entry.label.trim(),
    start: start.trim(),
    ...(kind === 'action' ? { action: name } : { event: name }),
    ...(entry.once ? { once: true } : {}),
    ...(refire && refire !== EVENT_REFIRE_IGNORE ? { refire } : {}),
    ...(filters ? { filters } : {}),
  };
}

/**
 * Why this entry cannot be saved, in the author's terms. Checked before the compiler so
 * a half-filled card names itself instead of surfacing as a graph-wide compile error.
 */
export function describeEntryProblem(entry: EventEntryConfig): string | null {
  const label = entry.label.trim() || '(unnamed entry)';
  if (label !== entry.label) {
    return `Entry '${label}' must not have leading or trailing spaces in its label.`;
  }
  if (entryTriggerName(entry).trim() === '') {
    return entryTriggerKind(entry) === 'action'
      ? `Entry '${label}' starts on an input action but no action id is picked.`
      : `Entry '${label}' starts on an event but no event name is set.`;
  }
  if (entryTriggerKind(entry) === 'action' && entry.filters?.action?.trim()) {
    return `Entry '${label}' already starts on an input action; clear the 'Action' payload filter.`;
  }
  return null;
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
      const entry = node.data.entry ?? createEmptyEventEntry(fallbackLabel);
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
