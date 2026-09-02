import { nightRaidFlowStories } from './liveDebugStories/nightRaidFlow.ts';

export type LiveDebugStoryBeat = {
  id: string;
  nodes: string[];
  text: string;
};

export type LiveDebugEntryStory = {
  title: string;
  summary: string;
  beats: LiveDebugStoryBeat[];
};

type StoryCatalog = {
  graphId: string;
  entries: Record<string, LiveDebugEntryStory>;
};

const catalogs: StoryCatalog[] = [nightRaidFlowStories];

export function lookupLiveDebugStory(
  graphId: string,
  entryLabel: string,
): LiveDebugEntryStory | null {
  const catalog = catalogs.find((row) => row.graphId === graphId);
  if (!catalog) return null;
  return catalog.entries[entryLabel.trim()] ?? null;
}

/** Latest NodeEnter id wins; map it onto the story beat that lists that node. */
export function resolveLiveDebugBeat(
  story: LiveDebugEntryStory | null,
  recentNodeIds: string[],
): LiveDebugStoryBeat | null {
  if (!story || recentNodeIds.length === 0) return null;
  for (let i = recentNodeIds.length - 1; i >= 0; i--) {
    const nodeId = recentNodeIds[i]!;
    const beat = story.beats.find((row) => row.nodes.includes(nodeId));
    if (beat) return beat;
  }
  return null;
}
