import { abilityExecAdapter } from './abilityExec.ts';
import { presenterTimerAdapter } from './presenterTimer.ts';
import { sequencerAdapter } from './sequencer.ts';
import type { TimelineAdapter, TimelineContextId } from '../model.ts';

export { abilityExecAdapter } from './abilityExec.ts';
export { presenterTimerAdapter } from './presenterTimer.ts';
export { sequencerAdapter } from './sequencer.ts';

export const TIMELINE_ADAPTERS: Record<TimelineContextId, TimelineAdapter> = {
  sequencer: sequencerAdapter,
  'ability-exec': abilityExecAdapter,
  'presenter-timer': presenterTimerAdapter,
};

export const TIMELINE_CONTEXT_ORDER: TimelineContextId[] = ['sequencer', 'ability-exec', 'presenter-timer'];

export function adapterFor(contextId: TimelineContextId): TimelineAdapter {
  const adapter = TIMELINE_ADAPTERS[contextId];
  if (!adapter) throw new Error(`未知时间轴上下文 ${contextId}`);
  return adapter;
}
