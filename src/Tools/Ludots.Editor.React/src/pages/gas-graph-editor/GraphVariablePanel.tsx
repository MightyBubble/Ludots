import React from 'react';

export const MAP_VAR_DRAG_MIME = 'text/plain';
export const MAP_VAR_DRAG_PREFIX = 'ludots-map-var';
export const PLACED_VAR_DRAG_PREFIX = 'ludots-placed-var';

export type MapVariableKind = 'int' | 'float' | 'array' | 'map';
export type MapVariableScalarType = 'int' | 'float';

export type GraphVariableRow = {
  name: string;
  type: MapVariableScalarType;
  initial: number;
  declared: boolean;
  reads: number;
  writes: number;
};

export type GraphPlacedInstance = {
  instanceId: string;
  template: string;
  ordinal: number;
};

export type MapVariableDraft = {
  name: string;
  kind: MapVariableKind;
  elementType: MapVariableScalarType;
  keyType: MapVariableScalarType;
  initial: string;
};

export const emptyVariableDraft = (): MapVariableDraft => ({
  name: '',
  kind: 'int',
  elementType: 'int',
  keyType: 'int',
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

export function encodePlacedVarDrag(instanceId: string): string {
  return `${PLACED_VAR_DRAG_PREFIX}\t${instanceId}`;
}

export function decodePlacedVarDrag(raw: string): { instanceId: string } | null {
  const parts = raw.split('\t');
  if (parts.length !== 2 || parts[0] !== PLACED_VAR_DRAG_PREFIX) return null;
  if (!parts[1]) return null;
  return { instanceId: parts[1] };
}

export function collectionTypeError(kind: MapVariableKind): string | null {
  if (kind === 'array' || kind === 'map') {
    return 'Map variables only store Integer or Float today. Array and Map stay in the type list so the choice is visible; collection variables wait for the #1108 follow-up slice.';
  }
  return null;
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
  const collectionError = collectionTypeError(draft.kind);
  const selected = selectedName != null && variables.some((variable) => variable.name === selectedName);
  const placedSorted = [...placedInstances].sort((a, b) => a.ordinal - b.ordinal);

  return (
    <div className="flex min-h-[240px] flex-col border-t border-slate-800 bg-slate-950/90">
      <div className="border-b border-slate-800 px-3 py-2">
        <div className="text-xs font-semibold uppercase tracking-wide text-amber-200">Variables</div>
        <div className="mt-0.5 text-[10px] text-slate-500">
          {mapId ? `Map ${mapId}` : 'No map hosts this graph'}
        </div>
      </div>
      <div className="min-h-0 flex-1 overflow-auto px-2 py-2">
        {variables.length === 0 && placedSorted.length === 0 ? (
          <div className="px-1 text-[11px] text-slate-500">
            {mapId
              ? 'This map has no variables yet. Add one below, then drag it onto the canvas.'
              : 'Map variables live on the map that mounts this graph.'}
          </div>
        ) : null}
        {placedSorted.length > 0 ? (
          <div className="mb-2">
            <div className="px-1 pb-1 text-[9px] font-semibold uppercase tracking-wide text-violet-300">
              Placed instances
            </div>
            {placedSorted.map((instance) => (
              <div
                key={instance.instanceId}
                draggable
                onDragStart={(event) => {
                  event.dataTransfer.setData(MAP_VAR_DRAG_MIME, encodePlacedVarDrag(instance.instanceId));
                  event.dataTransfer.effectAllowed = 'copy';
                }}
                className="mb-1 flex w-full cursor-grab items-center gap-2 rounded border border-violet-950 bg-violet-950/40 px-2 py-1.5 text-left active:cursor-grabbing"
                title={instance.template ? `Template ${instance.template}` : undefined}
              >
                <span className="w-8 shrink-0 font-mono text-[9px] uppercase text-violet-300">ent</span>
                <span className="min-w-0 flex-1 truncate font-mono text-[11px] text-violet-100">{instance.instanceId}</span>
                <span className="shrink-0 text-[9px] text-violet-400/80">#{instance.ordinal}</span>
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
                  active ? 'bg-amber-950 text-amber-50' : 'text-slate-200 hover:bg-slate-800'
                }`}
              >
                <span className="w-8 shrink-0 font-mono text-[9px] uppercase text-sky-300">{variable.type}</span>
                <span className="min-w-0 flex-1 truncate font-mono text-[11px]">{variable.name}</span>
                <span className="shrink-0 text-[9px] text-slate-500">
                  {variable.declared ? `${variable.initial}` : 'undeclared'}
                </span>
                <span className="shrink-0 text-[9px] text-slate-500">
                  {variable.reads} get · {variable.writes} set
                </span>
              </div>
            );
          })
          : null}
      </div>
      <div className="space-y-2 border-t border-slate-800 px-3 py-2">
        <label className="block">
          <div className="mb-1 text-[10px] text-slate-500">Name</div>
          <input
            value={draft.name}
            disabled={busy || !mapId}
            onChange={(event) => onDraftChange({ ...draft, name: event.target.value })}
            className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono text-[11px] text-slate-100"
          />
        </label>
        <div className="grid grid-cols-2 gap-2">
          <label className="block">
            <div className="mb-1 text-[10px] text-slate-500">Type</div>
            <select
              value={draft.kind}
              disabled={busy || !mapId}
              onChange={(event) => onDraftChange({ ...draft, kind: event.target.value as MapVariableKind })}
              className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono text-[11px] text-slate-100"
            >
              <option value="int">Integer</option>
              <option value="float">Float</option>
              <option value="array">Array</option>
              <option value="map">Map</option>
            </select>
          </label>
          {draft.kind === 'int' || draft.kind === 'float' ? (
            <label className="block">
              <div className="mb-1 text-[10px] text-slate-500">Default</div>
              <input
                value={draft.initial}
                disabled={busy || !mapId}
                onChange={(event) => onDraftChange({ ...draft, initial: event.target.value })}
                className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono text-[11px] text-slate-100"
              />
            </label>
          ) : draft.kind === 'array' ? (
            <label className="block">
              <div className="mb-1 text-[10px] text-slate-500">Element type</div>
              <select
                value={draft.elementType}
                disabled={busy || !mapId}
                onChange={(event) => onDraftChange({ ...draft, elementType: event.target.value as MapVariableScalarType })}
                className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono text-[11px] text-slate-100"
              >
                <option value="int">Integer</option>
                <option value="float">Float</option>
              </select>
            </label>
          ) : (
            <label className="block">
              <div className="mb-1 text-[10px] text-slate-500">Key / value</div>
              <div className="grid grid-cols-2 gap-1">
                <select
                  value={draft.keyType}
                  disabled={busy || !mapId}
                  onChange={(event) => onDraftChange({ ...draft, keyType: event.target.value as MapVariableScalarType })}
                  className="w-full rounded border border-slate-700 bg-slate-950 px-1 py-1 font-mono text-[11px] text-slate-100"
                >
                  <option value="int">Int key</option>
                  <option value="float">Float key</option>
                </select>
                <select
                  value={draft.elementType}
                  disabled={busy || !mapId}
                  onChange={(event) => onDraftChange({ ...draft, elementType: event.target.value as MapVariableScalarType })}
                  className="w-full rounded border border-slate-700 bg-slate-950 px-1 py-1 font-mono text-[11px] text-slate-100"
                >
                  <option value="int">Int val</option>
                  <option value="float">Float val</option>
                </select>
              </div>
            </label>
          )}
        </div>
        {collectionError ? <div className="text-[10px] text-amber-300">{collectionError}</div> : null}
        <div className="flex gap-1">
          <button
            type="button"
            disabled={busy || !mapId}
            onClick={onCreate}
            className="flex-1 rounded bg-emerald-800 px-2 py-1 text-[11px] font-semibold text-emerald-50 hover:bg-emerald-700 disabled:opacity-50"
          >
            Add
          </button>
          <button
            type="button"
            disabled={busy || !mapId || !selected}
            onClick={onUpdate}
            className="flex-1 rounded bg-sky-800 px-2 py-1 text-[11px] font-semibold text-sky-50 hover:bg-sky-700 disabled:opacity-50"
          >
            Update
          </button>
          <button
            type="button"
            disabled={busy || !mapId || !selected}
            onClick={onDelete}
            className="flex-1 rounded bg-rose-900 px-2 py-1 text-[11px] font-semibold text-rose-50 hover:bg-rose-800 disabled:opacity-50"
          >
            Delete
          </button>
        </div>
        <div className="text-[10px] text-slate-500">{status}</div>
        <div className="text-[10px] text-slate-600">
          Drag a declared variable onto the canvas, then choose Get or Set.
        </div>
      </div>
    </div>
  );
}
