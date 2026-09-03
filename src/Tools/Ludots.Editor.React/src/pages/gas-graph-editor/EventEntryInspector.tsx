import React from 'react';
import type { EventSchemaView } from './GasNode';
import {
  EVENT_DIRECTIONS,
  EVENT_REFIRE_IGNORE,
  EVENT_REFIRE_RESTART,
  entryTriggerKind,
  entryTriggerName,
  parseOptionalFloat,
  parseOptionalInt,
  setEntryTrigger,
  type EntryTriggerKind,
  type EventEntryConfig,
  type EventEntryFilters,
} from './eventEntry';

/** Payload schema an action-bound entry captures from, mirroring the runtime constant. */
export const INPUT_ACTION_SCHEMA_NAME = 'InputAction';

export type InputActionView = {
  id: string;
  type: string;
  source: string;
};

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <div className="mb-1 text-slate-500">{label}</div>
      {children}
    </label>
  );
}

function TextInput({
  value,
  onChange,
  placeholder,
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
}) {
  return (
    <input
      type="text"
      value={value}
      placeholder={placeholder}
      onChange={(event) => onChange(event.target.value)}
      className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
    />
  );
}

export function EventEntryInspector({
  entry,
  eventSchemas,
  inputActions,
  startOptions,
  instanceOptions,
  variableOptions,
  onChange,
  onAdd,
}: {
  entry: EventEntryConfig;
  eventSchemas: EventSchemaView[];
  inputActions: InputActionView[];
  startOptions: string[];
  instanceOptions: string[];
  variableOptions: string[];
  onChange: (next: EventEntryConfig) => void;
  onAdd?: () => void;
}) {
  const filters = entry.filters ?? {};
  const startChoices = entry.start && !startOptions.includes(entry.start)
    ? [entry.start, ...startOptions]
    : startOptions;
  const triggerKind = entryTriggerKind(entry);
  const triggerName = entryTriggerName(entry);
  const schemaNames = new Set(eventSchemas.map((schema) => schema.name));
  const actionIds = new Set(inputActions.map((action) => action.id));
  // Action-bound entries capture from the shared InputAction schema, so the payload pins
  // an author can drag are the same either way — only the lookup key differs.
  const selectedSchema = eventSchemas.find((schema) => schema.name === (
    triggerKind === 'action' ? INPUT_ACTION_SCHEMA_NAME : triggerName
  )) ?? null;
  const catalogValue = triggerKind === 'event' && selectedSchema ? triggerName : '';

  const patchFilters = (patch: EventEntryFilters) => {
    onChange({
      ...entry,
      filters: { ...filters, ...patch },
    });
  };

  const setTrigger = (kind: EntryTriggerKind, name: string) => {
    const autoLabel = name !== '' && (entry.label.trim() === '' || entry.label.startsWith('on_'))
      ? `on_${name.replace(/[^A-Za-z0-9_]+/g, '_')}`
      : entry.label;
    onChange({ ...setEntryTrigger(entry, kind, name), label: autoLabel });
  };

  return (
    <div className="space-y-2 rounded border border-rose-900 bg-rose-950/40 p-2">
      <div className="flex items-center justify-between">
        <div className="text-[10px] font-semibold uppercase tracking-wide text-rose-200">Event entry</div>
        {onAdd ? (
          <button
            type="button"
            onClick={onAdd}
            className="rounded border border-rose-800 px-2 py-0.5 text-[10px] text-rose-100 hover:bg-rose-900"
          >
            Add Event
          </button>
        ) : null}
      </div>
      <Field label="Starts on">
        <div className="flex gap-1">
          {(['event', 'action'] as const).map((kind) => (
            <button
              key={kind}
              type="button"
              onClick={() => triggerKind === kind || setTrigger(kind, '')}
              className={triggerKind === kind
                ? 'flex-1 rounded border border-rose-500 bg-rose-900/60 px-2 py-1 font-semibold text-rose-50'
                : 'flex-1 rounded border border-slate-700 bg-slate-950 px-2 py-1 text-slate-400 hover:bg-slate-900'}
            >
              {kind === 'event' ? 'a game event' : 'an input action'}
            </button>
          ))}
        </div>
      </Field>
      {triggerKind === 'event' ? (
        <>
          <Field label="Event schema">
            <select
              value={catalogValue}
              onChange={(event) => setTrigger('event', event.target.value)}
              className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
            >
              <option value="">{eventSchemas.length === 0 ? 'No schemas loaded' : 'Pick a registered event…'}</option>
              {eventSchemas.map((schema) => (
                <option key={schema.name} value={schema.name}>
                  {schema.name} · {schema.scope}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Event name">
            <TextInput
              value={triggerName}
              placeholder="EntityDied"
              onChange={(eventName) => setTrigger('event', eventName)}
            />
          </Field>
          {triggerName && !schemaNames.has(triggerName) ? (
            <p className="rounded border border-amber-900/60 bg-amber-950/40 p-2 text-[11px] leading-5 text-amber-100/90">
              This name is not in the schema catalog. Payload pins stay untyped until you pick a registered event.
            </p>
          ) : null}
        </>
      ) : (
        <>
          <Field label="Input action">
            <select
              value={actionIds.has(triggerName) ? triggerName : ''}
              onChange={(event) => setTrigger('action', event.target.value)}
              className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
            >
              <option value="">{inputActions.length === 0 ? 'No input actions loaded' : 'Pick a registered action…'}</option>
              {inputActions.map((action) => (
                <option key={action.id} value={action.id}>
                  {action.id} · {action.type}
                </option>
              ))}
            </select>
          </Field>
          {triggerName && !actionIds.has(triggerName) ? (
            <p className="rounded border border-rose-800 bg-rose-950/50 p-2 text-[11px] leading-5 text-rose-100">
              <span className="font-mono">{triggerName}</span> is not a registered input action. Pick one from the list
              or add it to a mod's <span className="font-mono">Input/default_input.json</span>.
            </p>
          ) : null}
          <p className="rounded border border-rose-950 bg-slate-950/70 p-2 text-[11px] leading-5 text-rose-100/80">
            This entry listens to the action itself, so it does not join the event bus. Leave the
            <span className="font-mono"> Action </span> payload filter below empty.
          </p>
        </>
      )}
      {selectedSchema ? (
        <div className="rounded border border-rose-950 bg-slate-950/70 p-2 text-[11px] leading-5 text-rose-100/80">
          <div className="mb-1 font-semibold text-rose-100">
            Payload pins
            {triggerKind === 'action'
              ? <span className="ml-1 font-mono font-normal text-slate-500">{INPUT_ACTION_SCHEMA_NAME}</span>
              : null}
          </div>
          {selectedSchema.parameters.length === 0 ? (
            <div>No parameters.</div>
          ) : (
            <ul className="space-y-0.5 font-mono text-[10px]">
              {selectedSchema.parameters.map((param) => (
                <li key={param.key}>
                  {param.name}
                  <span className="text-slate-500"> : {param.type}</span>
                  {param.optional ? <span className="text-slate-500"> (optional)</span> : null}
                  {param.type === 'String' ? <span className="text-amber-300"> — String pin not wired yet</span> : null}
                </li>
              ))}
            </ul>
          )}
        </div>
      ) : null}
      <Field label="Label">
        <TextInput
          value={entry.label}
          placeholder="on_raider_died"
          onChange={(label) => onChange({ ...entry, label })}
        />
      </Field>
      <Field label="Starts at">
        <select
          value={entry.start}
          onChange={(event) => onChange({ ...entry, start: event.target.value })}
          className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
        >
          <option value="">Wire Then, or pick a node</option>
          {startChoices.map((id) => (
            <option key={id} value={id}>{id}</option>
          ))}
        </select>
      </Field>
      <label className="flex items-center gap-2">
        <input
          type="checkbox"
          checked={Boolean(entry.once)}
          onChange={(event) => onChange({ ...entry, once: event.target.checked })}
        />
        <span className="text-slate-400">Once</span>
      </label>
      <Field label="If already running">
        <select
          value={entry.refire === EVENT_REFIRE_RESTART ? EVENT_REFIRE_RESTART : EVENT_REFIRE_IGNORE}
          onChange={(event) => onChange({ ...entry, refire: event.target.value })}
          className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
        >
          <option value={EVENT_REFIRE_IGNORE}>ignore — keep the current run</option>
          <option value={EVENT_REFIRE_RESTART}>restart — drop it and start over</option>
        </select>
      </Field>
      <div className="border-t border-rose-900 pt-2 text-[10px] font-semibold uppercase tracking-wide text-rose-200">
        Who can fire this
      </div>
      <Field label="Instance (exact placed unit)">
        <select
          value={filters.instanceId ?? ''}
          onChange={(event) => patchFilters({ instanceId: event.target.value || null })}
          className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
        >
          <option value="">any source</option>
          {instanceOptions.map((instanceId) => (
            <option key={instanceId} value={instanceId}>{instanceId}</option>
          ))}
        </select>
      </Field>
      <Field label="Variable (exact map variable)">
        <select
          value={filters.varName ?? ''}
          onChange={(event) => patchFilters({ varName: event.target.value || null })}
          className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
        >
          <option value="">any variable</option>
          {variableOptions.map((varName) => (
            <option key={varName} value={varName}>{varName}</option>
          ))}
        </select>
      </Field>
      <Field label="Region">
        <TextInput value={filters.region ?? ''} placeholder="raid_circle" onChange={(region) => patchFilters({ region })} />
      </Field>
      <Field label="Action carried by the event payload">
        {triggerKind === 'action' ? (
          <div className="rounded border border-slate-800 bg-slate-950/60 px-2 py-1 text-[11px] leading-5 text-slate-500">
            Not applicable — this entry already starts on an input action.
          </div>
        ) : (
          <TextInput
            value={filters.action ?? ''}
            placeholder="CommandSourceAcquire"
            onChange={(action) => patchFilters({ action })}
          />
        )}
      </Field>
      <Field label="Tag">
        <TextInput value={filters.tag ?? ''} onChange={(tag) => patchFilters({ tag })} />
      </Field>
      <Field label="Team">
        <input
          type="number"
          step="1"
          value={filters.team ?? ''}
          onChange={(event) => patchFilters({ team: parseOptionalInt(event.target.value) })}
          className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
        />
      </Field>
      <Field label="Count direction">
        <select
          value={filters.direction ?? ''}
          onChange={(event) => patchFilters({ direction: event.target.value || null })}
          className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
        >
          <option value="">none</option>
          {EVENT_DIRECTIONS.map((direction) => (
            <option key={direction} value={direction}>{direction}</option>
          ))}
        </select>
      </Field>
      <Field label="Count threshold">
        <input
          type="number"
          value={filters.threshold ?? ''}
          onChange={(event) => patchFilters({ threshold: parseOptionalFloat(event.target.value) })}
          className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono"
        />
      </Field>
      <p className="rounded border border-rose-950 bg-slate-950/70 p-2 text-[11px] leading-5 text-rose-100/80">
        This card only decides when the chain starts. The named pins above hand over what
        happened this time; drag one onto a value input to place the read node.
      </p>
    </div>
  );
}
