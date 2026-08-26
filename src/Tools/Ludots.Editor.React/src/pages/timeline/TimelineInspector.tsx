import React from 'react';
import { readString, type TimelineClip, type TimelineField } from './model.ts';

const fieldClass = 'mt-1 w-full bg-zinc-900 border border-zinc-700 rounded px-2 py-1.5 text-sm text-zinc-100';
const labelClass = 'block text-xs text-zinc-400';

type Props = {
  title: string;
  fields: TimelineField[];
  values: Record<string, unknown>;
  onChange: (key: string, value: unknown) => void;
  onRemove?: () => void;
  extra?: React.ReactNode;
};

function FieldControl({
  field,
  value,
  onChange,
}: {
  field: TimelineField;
  value: unknown;
  onChange: (value: unknown) => void;
}) {
  if (field.type === 'checkbox') {
    return (
      <label className="flex items-center gap-2 text-xs text-zinc-400 pt-5">
        <input type="checkbox" checked={!!value} onChange={(e) => onChange(e.target.checked)} />
        {field.label}
      </label>
    );
  }
  if (field.type === 'select') {
    return (
      <label className={labelClass}>
        {field.label}
        <select className={fieldClass} value={readString(value)} onChange={(e) => onChange(e.target.value)}>
          {(field.options ?? []).map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </label>
    );
  }
  if (field.type === 'number') {
    return (
      <label className={labelClass}>
        {field.label}
        <input
          className={fieldClass}
          type="number"
          step={field.step}
          min={field.min}
          value={value === undefined || value === null ? '' : String(value)}
          onChange={(e) => onChange(e.target.value === '' ? 0 : Number(e.target.value))}
        />
      </label>
    );
  }
  return (
    <label className={labelClass}>
      {field.label}
      <input className={fieldClass} value={readString(value)} onChange={(e) => onChange(e.target.value)} />
    </label>
  );
}

export function clipValues(clip: TimelineClip): Record<string, unknown> {
  return {
    ...clip.payload,
    start: clip.start,
    duration: clip.duration,
  };
}

export const TimelineInspector: React.FC<Props> = ({ title, fields, values, onChange, onRemove, extra }) => {
  const visible = fields.filter((field) => !field.visibleWhen || field.visibleWhen(values));
  return (
    <div className="rounded border border-zinc-800 bg-zinc-950/60 p-3 space-y-2">
      <div className="flex items-center justify-between">
        <h3 className="text-sm text-amber-200">{title}</h3>
        {onRemove && (
          <button type="button" className="text-xs px-2 py-1 rounded border border-rose-900 text-rose-300" onClick={onRemove}>
            删除
          </button>
        )}
      </div>
      <div className="grid grid-cols-2 gap-2">
        {visible.map((field) => (
          <FieldControl key={field.key} field={field} value={values[field.key]} onChange={(value) => onChange(field.key, value)} />
        ))}
      </div>
      {extra}
    </div>
  );
};
