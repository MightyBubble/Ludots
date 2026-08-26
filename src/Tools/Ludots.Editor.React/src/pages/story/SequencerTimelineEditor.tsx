import React, { useMemo, useRef, useState } from 'react';

export type SequencerTrackRow = {
  type: string;
  profile?: string;
  lineId?: string;
  presentationProfile?: string;
  eventId?: string;
  actionGraphId?: string;
  start: number;
  duration?: number;
};

type Props = {
  tracks: SequencerTrackRow[];
  selectedIndex: number;
  onSelect: (index: number) => void;
  onChangeTrack: (index: number, next: SequencerTrackRow) => void;
  pixelsPerSecond?: number;
};

const LANE_ORDER = ['Camera', 'Subtitle', 'Signal'] as const;
const LANE_LABEL: Record<string, string> = {
  Camera: '镜头 Camera',
  Subtitle: '字幕 Subtitle',
  Signal: '信号 Signal',
};
const LANE_COLOR: Record<string, string> = {
  Camera: 'bg-sky-500/80 border-sky-300',
  Subtitle: 'bg-amber-500/80 border-amber-200',
  Signal: 'bg-rose-500/70 border-rose-200',
};

function trackLabel(track: SequencerTrackRow): string {
  if (track.type === 'Camera') return track.profile || 'Camera';
  if (track.type === 'Subtitle') return track.lineId || 'Subtitle';
  return track.eventId || track.actionGraphId || 'Signal';
}

function trackDuration(track: SequencerTrackRow): number {
  if (track.type === 'Signal') return Math.max(0.25, 0.35);
  return Math.max(0.2, Number(track.duration) || 0.2);
}

export const SequencerTimelineEditor: React.FC<Props> = ({
  tracks,
  selectedIndex,
  onSelect,
  onChangeTrack,
  pixelsPerSecond = 96,
}) => {
  const scrollerRef = useRef<HTMLDivElement>(null);
  const [drag, setDrag] = useState<{ index: number; mode: 'move' | 'resize'; originX: number; start: number; duration: number } | null>(
    null,
  );

  const totalSeconds = useMemo(() => {
    let end = 8;
    for (const track of tracks) {
      end = Math.max(end, (Number(track.start) || 0) + trackDuration(track) + 1);
    }
    return Math.ceil(end);
  }, [tracks]);

  const width = totalSeconds * pixelsPerSecond;
  const ticks = useMemo(() => Array.from({ length: totalSeconds + 1 }, (_, i) => i), [totalSeconds]);

  const onPointerMove = (clientX: number) => {
    if (!drag || !scrollerRef.current) return;
    const dx = clientX - drag.originX;
    const deltaSec = dx / pixelsPerSecond;
    const track = tracks[drag.index];
    if (!track) return;
    if (drag.mode === 'move') {
      const nextStart = Math.max(0, Math.round((drag.start + deltaSec) * 10) / 10);
      onChangeTrack(drag.index, { ...track, start: nextStart });
    } else if (track.type !== 'Signal') {
      const nextDur = Math.max(0.2, Math.round((drag.duration + deltaSec) * 10) / 10);
      onChangeTrack(drag.index, { ...track, duration: nextDur });
    }
  };

  return (
    <div className="rounded border border-zinc-700 bg-zinc-950/80 overflow-hidden">
      <div className="flex items-center justify-between px-3 py-2 border-b border-zinc-800">
        <div className="text-sm text-amber-200">演出时间轴</div>
        <div className="text-[11px] text-zinc-500">拖块改开始；拖右边改时长 · {pixelsPerSecond}px/s</div>
      </div>

      <div
        ref={scrollerRef}
        className="overflow-x-auto"
        onPointerMove={(e) => onPointerMove(e.clientX)}
        onPointerUp={() => setDrag(null)}
        onPointerLeave={() => setDrag(null)}
      >
        <div className="min-w-full" style={{ width: width + 140 }}>
          <div className="flex border-b border-zinc-800">
            <div className="w-[140px] shrink-0 px-2 py-1 text-[10px] text-zinc-500">轨道</div>
            <div className="relative h-7" style={{ width }}>
              {ticks.map((t) => (
                <div
                  key={t}
                  className="absolute top-0 bottom-0 border-l border-zinc-800/80"
                  style={{ left: t * pixelsPerSecond }}
                >
                  <span className="absolute top-1 left-1 text-[10px] text-zinc-500">{t}s</span>
                </div>
              ))}
            </div>
          </div>

          {LANE_ORDER.map((lane) => (
            <div key={lane} className="flex border-b border-zinc-900/80">
              <div className="w-[140px] shrink-0 px-2 py-3 text-xs text-zinc-300 bg-zinc-900/40">{LANE_LABEL[lane]}</div>
              <div className="relative h-14 bg-[linear-gradient(90deg,rgba(39,39,42,0.35)_1px,transparent_1px)] bg-[length:96px_100%]" style={{ width }}>
                {tracks.map((track, index) => {
                  if (track.type !== lane) return null;
                  const left = (Number(track.start) || 0) * pixelsPerSecond;
                  const w = trackDuration(track) * pixelsPerSecond;
                  const selected = index === selectedIndex;
                  return (
                    <div
                      key={`${lane}-${index}`}
                      className={`absolute top-2 h-10 rounded border px-2 text-[11px] text-zinc-950 font-medium cursor-grab active:cursor-grabbing flex items-center overflow-hidden ${LANE_COLOR[lane]} ${
                        selected ? 'ring-2 ring-emerald-300 z-10' : 'z-[1]'
                      }`}
                      style={{ left, width: Math.max(28, w) }}
                      onPointerDown={(e) => {
                        e.preventDefault();
                        onSelect(index);
                        (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
                        setDrag({
                          index,
                          mode: 'move',
                          originX: e.clientX,
                          start: Number(track.start) || 0,
                          duration: trackDuration(track),
                        });
                      }}
                      title={trackLabel(track)}
                    >
                      <span className="truncate pr-3">{trackLabel(track)}</span>
                      {track.type !== 'Signal' && (
                        <span
                          className="absolute right-0 top-0 bottom-0 w-2 cursor-ew-resize bg-black/20"
                          onPointerDown={(e) => {
                            e.preventDefault();
                            e.stopPropagation();
                            onSelect(index);
                            setDrag({
                              index,
                              mode: 'resize',
                              originX: e.clientX,
                              start: Number(track.start) || 0,
                              duration: trackDuration(track),
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

export default SequencerTimelineEditor;
