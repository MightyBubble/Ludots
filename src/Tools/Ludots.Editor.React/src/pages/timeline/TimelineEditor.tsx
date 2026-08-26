import React, { useMemo, useRef, useState } from 'react';
import { snapTime, type TimelineClip, type TimelineDocument, type TimeUnit } from './model.ts';

type DragState = {
  clipId: string;
  mode: 'move' | 'resize';
  originX: number;
  start: number;
  duration: number;
};

type Props = {
  document: TimelineDocument;
  selectedClipId: string | null;
  pixelsPerUnit?: number;
  playhead?: number | null;
  onSelectClip: (clipId: string) => void;
  onChangeClip: (clipId: string, patch: { start?: number; duration?: number }) => void;
};

function formatTick(value: number, unit: TimeUnit): string {
  return unit === 'ticks' ? `${value}` : `${value}`;
}

function clipIsActive(clip: TimelineClip, playhead: number | null | undefined): boolean {
  if (playhead === null || playhead === undefined) return false;
  if (clip.shape === 'point') return Math.abs(playhead - clip.start) <= Math.max(0.05, clip.duration * 0.15);
  return playhead >= clip.start && playhead < clip.start + clip.duration;
}

export const TimelineEditor: React.FC<Props> = ({
  document,
  selectedClipId,
  pixelsPerUnit,
  playhead,
  onSelectClip,
  onChangeClip,
}) => {
  const scrollerRef = useRef<HTMLDivElement>(null);
  const [drag, setDrag] = useState<DragState | null>(null);
  const scale = pixelsPerUnit ?? (document.timeUnit === 'ticks' ? 6 : 96);
  const laneById = useMemo(() => new Map(document.lanes.map((lane) => [lane.id, lane])), [document.lanes]);

  const totalUnits = useMemo(() => {
    const pad = document.timeUnit === 'ticks' ? 16 : 1;
    let end = document.timeUnit === 'ticks' ? 32 : 8;
    for (const clip of document.clips) {
      end = Math.max(end, clip.start + clip.duration + pad);
    }
    return Math.ceil(end);
  }, [document.clips, document.timeUnit]);

  const tickStep = document.timeUnit === 'ticks' ? (totalUnits > 160 ? 15 : totalUnits > 80 ? 10 : 5) : 1;
  const width = totalUnits * scale;
  const ticks = useMemo(() => {
    const values: number[] = [];
    for (let t = 0; t <= totalUnits; t += tickStep) values.push(t);
    return values;
  }, [totalUnits, tickStep]);

  const onPointerMove = (clientX: number) => {
    if (!drag) return;
    const clip = document.clips.find((item) => item.id === drag.clipId);
    if (!clip) return;
    const delta = (clientX - drag.originX) / scale;
    if (drag.mode === 'move' && clip.movable) {
      onChangeClip(clip.id, { start: snapTime(drag.start + delta, document.timeUnit) });
      return;
    }
    if (drag.mode === 'resize' && clip.resizable) {
      const minDuration = document.timeUnit === 'ticks' ? 1 : 0.05;
      onChangeClip(clip.id, { duration: Math.max(minDuration, snapTime(drag.duration + delta, document.timeUnit)) });
    }
  };

  return (
    <div className="rounded border border-zinc-700 bg-zinc-950/80 overflow-hidden">
      <div className="flex items-center justify-between px-3 py-2 border-b border-zinc-800">
        <div className="text-sm text-amber-200">{document.displayName || document.id || '时间轴'}</div>
        <div className="text-[11px] text-zinc-500">
          {document.clockLabel ? `${document.clockLabel} · ` : ''}
          拖块改开始；拖右边改时长 · {scale}px/{document.timeUnit === 'ticks' ? 'tick' : 's'}
        </div>
      </div>

      <div
        ref={scrollerRef}
        className="overflow-x-auto"
        onPointerMove={(e) => onPointerMove(e.clientX)}
        onPointerUp={() => setDrag(null)}
        onPointerLeave={() => setDrag(null)}
      >
        <div className="min-w-full" style={{ width: width + 160 }}>
          <div className="flex border-b border-zinc-800">
            <div className="w-[160px] shrink-0 px-2 py-1 text-[10px] text-zinc-500">轨道</div>
            <div className="relative h-7" style={{ width }}>
              {ticks.map((t) => (
                <div
                  key={t}
                  className="absolute top-0 bottom-0 border-l border-zinc-800/80"
                  style={{ left: t * scale }}
                >
                  <span className="absolute top-1 left-1 text-[10px] text-zinc-500">
                    {formatTick(t, document.timeUnit)}
                    {document.timeUnit === 'ticks' ? 't' : 's'}
                  </span>
                </div>
              ))}
              {playhead !== null && playhead !== undefined && (
                <div className="absolute top-0 bottom-[-999px] w-px bg-emerald-300 z-20" style={{ left: playhead * scale }} />
              )}
            </div>
          </div>

          {document.lanes.map((lane) => (
            <div key={lane.id} className="flex border-b border-zinc-900/80">
              <div className="w-[160px] shrink-0 px-2 py-3 text-xs text-zinc-300 bg-zinc-900/40">{lane.label}</div>
              <div
                className="relative h-14 bg-[linear-gradient(90deg,rgba(39,39,42,0.35)_1px,transparent_1px)]"
                style={{ width, backgroundSize: `${scale * (document.timeUnit === 'ticks' ? tickStep : 1)}px 100%` }}
              >
                {document.clips.map((clip) => {
                  if (clip.laneId !== lane.id) return null;
                  const left = clip.start * scale;
                  const w = Math.max(28, clip.duration * scale);
                  const selected = clip.id === selectedClipId;
                  const active = clipIsActive(clip, playhead);
                  const color = laneById.get(clip.laneId)?.colorClass ?? lane.colorClass;
                  return (
                    <div
                      key={clip.id}
                      className={`absolute top-2 h-10 rounded border px-2 text-[11px] text-zinc-950 font-medium flex items-center overflow-hidden ${color} ${
                        selected ? 'ring-2 ring-emerald-300 z-10' : 'z-[1]'
                      } ${active ? 'brightness-125' : ''} ${clip.movable ? 'cursor-grab active:cursor-grabbing' : 'cursor-pointer'}`}
                      style={{ left, width: w }}
                      onPointerDown={(e) => {
                        e.preventDefault();
                        onSelectClip(clip.id);
                        if (!clip.movable) return;
                        (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
                        setDrag({
                          clipId: clip.id,
                          mode: 'move',
                          originX: e.clientX,
                          start: clip.start,
                          duration: clip.duration,
                        });
                      }}
                      title={[clip.label, ...(clip.badges ?? [])].join(' · ')}
                    >
                      <span className="truncate pr-3">{clip.label}</span>
                      {clip.resizable && (
                        <span
                          className="absolute right-0 top-0 bottom-0 w-2 cursor-ew-resize bg-black/20"
                          onPointerDown={(e) => {
                            e.preventDefault();
                            e.stopPropagation();
                            onSelectClip(clip.id);
                            (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
                            setDrag({
                              clipId: clip.id,
                              mode: 'resize',
                              originX: e.clientX,
                              start: clip.start,
                              duration: clip.duration,
                            });
                          }}
                        />
                      )}
                    </div>
                  );
                })}
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
};

export default TimelineEditor;
