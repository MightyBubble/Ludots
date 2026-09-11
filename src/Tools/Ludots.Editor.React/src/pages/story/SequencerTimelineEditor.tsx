import React, { useMemo, useRef, useState } from 'react';
import { STUDIO_THEME } from '../authoring-studio/authoringTheme';

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
const LANE_FILL: Record<string, string> = {
  Camera: STUDIO_THEME.blue,
  Subtitle: STUDIO_THEME.yellow,
  Signal: STUDIO_THEME.red,
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
    <div className="overflow-hidden rounded-lg border border-studio-elevated bg-studio-bg">
      <div className="flex items-center justify-between border-b border-studio-elevated px-3 py-2">
        <div className="text-sm text-studio-label">演出时间轴</div>
        <div className="text-[11px] text-studio-muted">拖块改开始；拖右边改时长 · {pixelsPerSecond}px/s</div>
      </div>

      <div
        ref={scrollerRef}
        className="overflow-x-auto"
        onPointerMove={(e) => onPointerMove(e.clientX)}
        onPointerUp={() => setDrag(null)}
        onPointerLeave={() => setDrag(null)}
      >
        <div className="min-w-full" style={{ width: width + 140 }}>
          <div className="flex border-b border-studio-elevated">
            <div className="w-[140px] shrink-0 px-2 py-1 text-[10px] text-studio-muted">轨道</div>
            <div className="relative h-7" style={{ width }}>
              {ticks.map((t) => (
                <div
                  key={t}
                  className="absolute top-0 bottom-0 border-l border-studio-elevated/80"
                  style={{ left: t * pixelsPerSecond }}
                >
                  <span className="absolute top-1 left-1 text-[10px] text-studio-muted">{t}s</span>
                </div>
              ))}
            </div>
          </div>

          {LANE_ORDER.map((lane) => (
            <div key={lane} className="flex border-b border-studio-elevated/80">
              <div className="w-[140px] shrink-0 bg-studio-surface px-2 py-3 text-xs text-studio-label">{LANE_LABEL[lane]}</div>
              <div className="relative h-14 bg-studio-bg bg-[length:96px_100%]" style={{ width, backgroundImage: 'linear-gradient(90deg, color-mix(in srgb, var(--studio-elevated) 55%, transparent) 1px, transparent 1px)' }}>
                {tracks.map((track, index) => {
                  if (track.type !== lane) return null;
                  const left = (Number(track.start) || 0) * pixelsPerSecond;
                  const w = trackDuration(track) * pixelsPerSecond;
                  const selected = index === selectedIndex;
                  return (
                    <div
                      key={`${lane}-${index}`}
                      className={`absolute top-2 z-[1] flex h-10 cursor-grab items-center overflow-hidden rounded border px-2 text-[11px] font-medium active:cursor-grabbing ${
                        selected ? 'z-10 ring-2 ring-studio-blue' : ''
                      }`}
                      style={{
                        left,
                        width: Math.max(28, w),
                        background: LANE_FILL[lane],
                        borderColor: LANE_FILL[lane],
                        color: lane === 'Subtitle' ? STUDIO_THEME.onYellow : STUDIO_THEME.label,
                      }}
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
