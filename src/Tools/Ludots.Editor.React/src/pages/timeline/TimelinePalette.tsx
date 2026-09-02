import React from 'react';
import type { TimelineAdapter } from './model.ts';

type Props = {
  adapter: TimelineAdapter;
  disabled?: boolean;
  disabledReason?: string;
  onAdd: (paletteId: string) => void;
};

export const TimelinePalette: React.FC<Props> = ({ adapter, disabled, disabledReason, onAdd }) => {
  const groups = new Map<string, typeof adapter.palette>();
  for (const item of adapter.palette) {
    const list = groups.get(item.group) ?? [];
    list.push(item);
    groups.set(item.group, list);
  }

  return (
    <div className="rounded border border-zinc-800 bg-zinc-950/60 p-3 space-y-2">
      <div className="flex items-center justify-between">
        <div className="text-sm text-amber-200">调色板</div>
        {disabled && <div className="text-[11px] text-rose-300">{disabledReason}</div>}
      </div>
      {[...groups.entries()].map(([group, items]) => (
        <div key={group} className="space-y-1">
          <div className="text-[11px] text-zinc-500">{group}</div>
          <div className="flex flex-wrap gap-1.5">
            {items.map((item) => (
              <button
                key={item.id}
                type="button"
                disabled={disabled}
                onClick={() => onAdd(item.id)}
                className="text-[11px] px-2 py-1 rounded border border-zinc-700 text-zinc-200 hover:bg-zinc-800 disabled:opacity-40 disabled:cursor-not-allowed"
              >
                + {item.label}
              </button>
            ))}
          </div>
        </div>
      ))}
    </div>
  );
};
