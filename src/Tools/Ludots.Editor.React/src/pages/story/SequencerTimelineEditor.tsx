import React, { useMemo } from 'react';
import { TimelineEditor } from '../timeline/TimelineEditor.tsx';
import {
  applySequencerClipChange,
  clipIdForTrack,
  projectSequencer,
  trackIndexFromClipId,
  type SequencerTrackRow,
} from '../timeline/contexts/sequencer.ts';

export type { SequencerTrackRow };

type Props = {
  tracks: SequencerTrackRow[];
  selectedIndex: number;
  onSelect: (index: number) => void;
  onChangeTrack: (index: number, next: SequencerTrackRow) => void;
  pixelsPerSecond?: number;
};

export const SequencerTimelineEditor: React.FC<Props> = ({
  tracks,
  selectedIndex,
  onSelect,
  onChangeTrack,
  pixelsPerSecond = 96,
}) => {
  const source = useMemo(() => ({ id: 'draft', displayName: '演出时间轴', tracks }), [tracks]);
  const document = useMemo(() => projectSequencer(source), [source]);

  return (
    <TimelineEditor
      document={document}
      selectedClipId={tracks.length ? clipIdForTrack(Math.min(Math.max(0, selectedIndex), tracks.length - 1)) : null}
      pixelsPerUnit={pixelsPerSecond}
      onSelectClip={(clipId) => {
        const index = trackIndexFromClipId(clipId);
        if (index !== null) onSelect(index);
      }}
      onChangeClip={(clipId, patch) => {
        const index = trackIndexFromClipId(clipId);
        if (index === null) return;
        const mutation = applySequencerClipChange(source, clipId, patch);
        if (!mutation.ok) return;
        const nextTracks = (mutation.source as { tracks: SequencerTrackRow[] }).tracks;
        if (nextTracks[index]) onChangeTrack(index, nextTracks[index]);
      }}
    />
  );
};

export default SequencerTimelineEditor;
