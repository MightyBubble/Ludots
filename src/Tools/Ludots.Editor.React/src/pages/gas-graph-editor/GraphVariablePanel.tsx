import React from 'react';

export const MAP_VAR_DRAG_MIME = 'text/plain';
export const MAP_VAR_DRAG_PREFIX = 'ludots-map-var';
export const PLACED_VAR_DRAG_PREFIX = 'ludots-placed-var';

export type MapVariableKind = 'int' | 'float';
export type MapVariableScalarType = 'int' | 'float';

export type GraphVariableRow = {
  name: string;
  type: MapVariableScalarType;
  initial: number;
  declared: boolean;
  reads: number;
  writes: number;
};

export type GraphPlacedKind = 'entity' | 'anchor' | 'region';

export type GraphPlacedInstance = {
  instanceId: string;
  template: string;
  kind: GraphPlacedKind;
  ordinal: number;
};

export type MapVariableDraft = {
  name: string;
  kind: MapVariableKind;
  initial: string;
};

export const emptyVariableDraft = (): MapVariableDraft => ({
  name: '',
  kind: 'int',
  initial: '0',
});

export function encodeMapVarDrag(name: string, type: MapVariableScalarType): string {
  return `${MAP_VAR_DRAG_PREFIX}\t${name}\t${type}`;
}

export function decodeMapVarDrag(raw: string): { name: string; type: MapVariableScalarType } | null {
  const parts = raw.split('\t');
  if (parts.length !== 3 || parts[0] !== MAP_VAR_DRAG_PREFIX) return null;
  const type = parts[2];
  if (type !== 'int' && type !== 'float') return null;
  if (!parts[1]) return null;
  return { name: parts[1], type };
}

export function encodePlacedVarDrag(instanceId: string, kind: GraphPlacedKind): string {
  return `${PLACED_VAR_DRAG_PREFIX}\t${instanceId}\t${kind}`;
}

export function decodePlacedVarDrag(raw: string): { instanceId: string; kind: GraphPlacedKind } | null {
  const parts = raw.split('\t');
  if (parts.length !== 3 || parts[0] !== PLACED_VAR_DRAG_PREFIX) return null;
  if (!parts[1]) return null;
  const kind = parts[2];
  if (kind !== 'entity' && kind !== 'anchor' && kind !== 'region') return null;
  return { instanceId: parts[1], kind };
}

export function GraphVariablePanel({
  variables,
  placedInstances,
  selectedName,
  mapId,
  status,
  draft,
  busy,
  onSelect,
  onDraftChange,
  onCreate,
  onUpdate,
  onDelete,
}: {
  variables: GraphVariableRow[];
  placedInstances: GraphPlacedInstance[];
  selectedName: string | null;
  mapId: string | null;
  status: string;
  draft: MapVariableDraft;
  busy: boolean;
  onSelect: (name: string) => void;
  onDraftChange: (draft: MapVariableDraft) => void;
  onCreate: () => void;
  onUpdate: () => void;
  onDelete: () => void;
}) {
  const selected = selectedName != null && variables.some((variable) => variable.name === selectedName);
  const placedSorted = [...placedInstances].sort((a, b) => a.ordinal - b.ordinal);

  return (
    <div className="flex min-h-[240px] flex-col border-t border-studio-elevated bg-studio-bg/90">
      <div className="border-b border-studio-elevated px-3 py-2">
        <div className="text-xs font-semibold uppercase tracking-wide text-studio-yellow">Variables</div>
        <div className="mt-0.5 text-[10px] text-studio-muted">
          {mapId ? `Map ${mapId}` : 'No map hosts this graph'}
        </div>
      </div>
      <div className="min-h-0 flex-1 overflow-auto px-2 py-2">
        {variables.length === 0 && placedSorted.length === 0 ? (
          <div className="px-1 text-[11px] text-studio-muted">
            {mapId
              ? 'This map has no variables yet. Add one below, then drag it onto the canvas.'
              : 'Map variables live on the map that mounts this graph.'}
          </div>
        ) : null}
        {placedSorted.length > 0 ? (
          <div className="mb-2">
            <div className="px-1 pb-1 text-[9px] font-semibold uppercase tracking-wide text-studio-red">
              Placed instances
            </div>
            {placedSorted.map((instance) => (
              <div
                key={instance.instanceId}
                draggable
                onDragStart={(event) => {
                  event.dataTransfer.setData(MAP_VAR_DRAG_MIME, encodePlacedVarDrag(instance.instanceId, instance.kind));
                  event.dataTransfer.effectAllowed = 'copy';
                }}
                className="mb-1 flex w-full cursor-grab items-center gap-2 rounded border border-studio-red/40 bg-studio-red/15 px-2 py-1.5 text-left active:cursor-grabbing"
                title={instance.template ? `Template ${instance.template}` : instance.kind === 'region' ? 'Map region' : undefined}
              >
                <span className="w-8 shrink-0 font-mono text-[9px] uppercase text-studio-red">
                  {instance.kind === 'region' ? 'reg' : instance.kind === 'anchor' ? 'anc' : 'ent'}
                </span>
                <span className="min-w-0 flex-1 truncate font-mono text-[11px] text-studio-label">{instance.instanceId}</span>
                <span className="shrink-0 text-[9px] text-studio-muted">#{instance.ordinal}</span>
              </div>
            ))}
          </div>
        ) : null}
        {variables.length > 0
          ? variables.map((variable) => {
            const active = variable.name === selectedName;
            return (
              <div
                key={variable.name}
                role="button"
                tabIndex={0}
                draggable={variable.declared}
                onClick={() => onSelect(variable.name)}
                onKeyDown={(event) => {
                  if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    onSelect(variable.name);
                  }
                }}
                onDragStart={(event) => {
                  if (!variable.declared) {
                    event.preventDefault();
                    return;
                  }
                  event.dataTransfer.setData(MAP_VAR_DRAG_MIME, encodeMapVarDrag(variable.name, variable.type));
                  event.dataTransfer.effectAllowed = 'copy';
                }}
                className={`mb-1 flex w-full cursor-grab items-center gap-2 rounded px-2 py-1.5 text-left active:cursor-grabbing ${
                  active ? 'bg-studio-yellow/15 text-studio-label ring-1 ring-studio-yellow/40' : 'text-studio-label hover:bg-studio-elevated'
                }`}
              >
                <span className="w-8 shrink-0 font-mono text-[9px] uppercase text-studio-blue">{variable.type}</span>
                <span className="min-w-0 flex-1 truncate font-mono text-[11px]">{variable.name}</span>
                <span className="shrink-0 text-[9px] text-studio-muted">
                  {variable.declared ? `${variable.initial}` : 'undeclared'}
                </span>
                <span className="shrink-0 text-[9px] text-studio-muted">
                  {variable.reads} get · {variable.writes} set
                </span>
              </div>
            );
          })
          : null}
      </div>
      <div className="space-y-2 border-t border-studio-elevated px-3 py-2">
        <label className="block">
          <div className="mb-1 text-[10px] text-studio-muted">Name</div>
          <input
            value={draft.name}
            disabled={busy || !mapId}
            onChange={(event) => onDraftChange({ ...draft, name: event.target.value })}
            className="w-full rounded border border-studio-fill bg-studio-bg px-2 py-1 font-mono text-[11px] text-studio-label"
          />
        </label>
        <div className="grid grid-cols-2 gap-2">
          <label className="block">
            <div className="mb-1 text-[10px] text-studio-muted">Type</div>
            <select
              value={draft.kind}
              disabled={busy || !mapId}
              onChange={(event) => onDraftChange({ ...draft, kind: event.target.value as MapVariableKind })}
              className="w-full rounded border border-studio-fill bg-studio-bg px-2 py-1 font-mono text-[11px] text-studio-label"
            >
              <option value="int">Integer</option>
              <option value="float">Float</option>
            </select>
          </label>
          <label className="block">
            <div className="mb-1 text-[10px] text-studio-muted">Default</div>
            <input
              value={draft.initial}
              disabled={busy || !mapId}
              onChange={(event) => onDraftChange({ ...draft, initial: event.target.value })}
              className="w-full rounded border border-studio-fill bg-studio-bg px-2 py-1 font-mono text-[11px] text-studio-label"
            />
          </label>
        </div>
        <div className="text-[10px] text-studio-muted">
          Map variables store Integer or Float only. Collections are not authorable here.
        </div>
        <div className="flex gap-1">
          <button
            type="button"
            disabled={busy || !mapId}
            onClick={onCreate}
            className="flex-1 rounded bg-studio-blue px-2 py-1 text-[11px] font-semibold text-studio-label hover:brightness-110 disabled:opacity-50"
          >
            Add
          </button>
          <button
            type="button"
            disabled={busy || !mapId || !selected}
            onClick={onUpdate}
            className="flex-1 rounded bg-studio-blue px-2 py-1 text-[11px] font-semibold text-studio-label hover:bg-studio-blue disabled:opacity-50"
          >
            Update
          </button>
          <button
            type="button"
            disabled={busy || !mapId || !selected}
            onClick={onDelete}
            className="flex-1 rounded bg-studio-red px-2 py-1 text-[11px] font-semibold text-studio-label hover:brightness-110 disabled:opacity-50"
          >
            Delete
          </button>
        </div>
        <div className="text-[10px] text-studio-muted">{status}</div>
        <div className="text-[10px] text-studio-muted">
          Drag a declared variable onto the canvas, then choose Get or Set.
        </div>
      </div>
    </div>
  );
}
