/**
 * Assert liveDebugStory helpers.
 * Run: node --experimental-strip-types scripts/assert-live-debug-story.mts
 */
import {
  lookupLiveDebugStory,
  resolveLiveDebugBeat,
} from '../src/pages/gas-graph-editor/liveDebugStory.ts';

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message);
}

const story = lookupLiveDebugStory('Graph.NightRaid.Flow', 'on_raider_died');
assert(story?.title === '杀敌刷 Boss', 'night raid kill story title');
assert(story?.summary.includes('击杀'), 'summary mentions kill count');

const beatCount = resolveLiveDebugBeat(story, ['rd_scope', 'rd_write']);
assert(beatCount?.id === 'count', 'rd_write maps to count beat');

const beatBoss = resolveLiveDebugBeat(story, ['rd_write', 'spawn_boss']);
assert(beatBoss?.id === 'boss', 'latest spawn_boss maps to boss beat');

assert(lookupLiveDebugStory('Graph.NightRaid.Flow', 'missing') == null, 'unknown entry is null');
assert(lookupLiveDebugStory('Other.Graph', 'on_raider_died') == null, 'unknown graph is null');

console.log('assert-live-debug-story: ok');
