import React from 'react';
import {
  EVENT_DIRECTIONS,
  EVENT_REFIRE_IGNORE,
  EVENT_REFIRE_RESTART,
  parseOptionalFloat,
  parseOptionalInt,
  type EventEntryConfig,
  type EventEntryFilters,
} from './eventEntry';

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
  startOptions,
  onChange,
  onAdd,
}: {
  entry: EventEntryConfig;
  startOptions: string[];
  onChange: (next: EventEntryConfig) => void;
  onAdd?: () => void;
}) {
  const filters = entry.filters ?? {};
  const startChoices = entry.start && !startOptions.includes(entry.start)
    ? [entry.start, ...startOptions]
    : startOptions;

  const patchFilters = (patch: EventEntryFilters) => {
    onChange({
      ...entry,
      filters: { ...filters, ...patch },
    });
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
      <Field label="Event">
        <TextInput
          value={entry.event}
          placeholder="EntityDied"
          onChange={(eventName) => onChange({ ...entry, event: eventName })}
        />
      </Field>
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
      <Field label="Region">
        <TextInput value={filters.region ?? ''} placeholder="raid_circle" onChange={(region) => patchFilters({ region })} />
      </Field>
      <Field label="Action">
        <TextInput value={filters.action ?? ''} placeholder="CommandSourceAcquire" onChange={(action) => patchFilters({ action })} />
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
        This card only decides when the chain starts. Who died or who walked in is not a pin yet.
        Follow the Then wire with LoadCaster / LoadExplicitTarget / LoadEventPayloadInt.
      </p>
    </div>
  );
}
