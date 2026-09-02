import React from 'react';
import { lookupEntryStory, type GraphAnnotations } from './graphAnnotations';

export type LiveDebugMountOption = {
  graphName: string;
  entryLabel: string;
  event: string;
};

/**
 * The one entry picker. The dock and the sidebar render this so both always offer the
 * same choices and the same labelling; the author's title is extra context next to the
 * event key, never a replacement for it.
 */
export function LiveDebugEntryPicker({
  mounts,
  annotations,
  value,
  onChange,
  className,
}: {
  mounts: LiveDebugMountOption[];
  annotations: GraphAnnotations;
  value: string;
  onChange: (entryLabel: string) => void;
  className?: string;
}) {
  return (
    <select
      value={value}
      onChange={(event) => onChange(event.target.value)}
      className={className ?? 'min-w-0 flex-1 rounded border border-slate-700 bg-slate-950 px-2 py-1 font-mono text-[11px]'}
    >
      <option value="">Select mounted entry</option>
      {mounts.map((mount) => {
        const story = lookupEntryStory(annotations, mount.entryLabel);
        return (
          <option key={`${mount.graphName}:${mount.entryLabel}`} value={mount.entryLabel}>
            {mount.entryLabel} · {mount.event}{story ? ` · ${story.title}` : ''}
          </option>
        );
      })}
    </select>
  );
}
