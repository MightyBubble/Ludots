import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  addAbilityExecFromPalette,
  applyAbilityExecClipChange,
  clipIdForItem,
  projectAbilityExec,
} from '../src/pages/timeline/contexts/abilityExec.ts';
import {
  addPresenterTimerFromPalette,
  applyPresenterTimerClipChange,
  clipIdForRule,
  PRESENTER_DURATION_TIMER_NAME,
  projectPresenterTimer,
} from '../src/pages/timeline/contexts/presenterTimer.ts';
import {
  applySequencerClipChange,
  clipIdForTrack,
  projectSequencer,
} from '../src/pages/timeline/contexts/sequencer.ts';

const root = join(dirname(fileURLToPath(import.meta.url)), '../../../..');
const failures: string[] = [];

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) failures.push(message);
}

function readJson(rel: string): unknown {
  return JSON.parse(readFileSync(join(root, rel), 'utf8'));
}

function first<T>(value: unknown): T {
  if (!Array.isArray(value) || value.length === 0) throw new Error('expected non-empty array');
  return value[0] as T;
}

const sequences = readJson('mods/showcases/narrative/NarrativeShowcaseMod/assets/Sequencer/sequences.json');
const trial = (sequences as Array<{ id: string }>).find((row) => row.id === 'Sequence.Narrative.TrialReveal');
assert(trial, 'TrialReveal sequence must exist');
const seqDoc = projectSequencer(trial);
assert(seqDoc.clips.length === 3, `TrialReveal should project 3 clips, got ${seqDoc.clips.length}`);
assert(seqDoc.clips.some((clip) => clip.laneId === 'Camera'), 'TrialReveal should have a Camera clip');
assert(seqDoc.clips.some((clip) => clip.laneId === 'Signal' && clip.shape === 'point'), 'TrialReveal should have a Signal point');
const moved = applySequencerClipChange(trial, clipIdForTrack(0), { start: 0.4 });
assert(moved.ok, 'moving camera track should succeed');
if (moved.ok) {
  const next = moved.source as { tracks: Array<{ start: number }> };
  assert(next.tracks[0].start === 0.4, `camera start should write back 0.4, got ${next.tracks[0].start}`);
}

const abilities = readJson('mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/GAS/abilities.json');
const build = (abilities as Array<{ id: string }>).find((row) => row.id === 'Ability.Rts.RedAlert.BuildPowerPlant');
assert(build, 'BuildPowerPlant ability must exist');
const execDoc = projectAbilityExec(build);
assert(execDoc.occupancy?.max === 16, 'ability occupancy max must be 16');
assert(execDoc.clips.length === 11, `BuildPowerPlant should project 11 items, got ${execDoc.clips.length}`);
assert(execDoc.clips[0]?.laneId === 'clip', 'first item should sit on clip lane');
assert(execDoc.clips.some((clip) => clip.laneId === 'end'), 'BuildPowerPlant should show End');
const shifted = applyAbilityExecClipChange(build, clipIdForItem(1), { start: 8 });
assert(shifted.ok, 'moving EffectSignal should succeed');
if (shifted.ok) {
  const next = shifted.source as { exec: { items: Array<{ tick: number; kind: string }> } };
  assert(next.exec.items[1].tick === 8, `item[1].tick should write back 8, got ${next.exec.items[1].tick}`);
  assert(next.exec.items[1].kind === 'EffectSignal', 'item[1] kind must stay EffectSignal');
  assert(next.exec.items[0].tick === 0, 'item[0] must keep tick 0 — array order is not rewritten');
}
const resized = applyAbilityExecClipChange(build, clipIdForItem(0), { duration: 90 });
assert(resized.ok, 'resizing TagClip should succeed');
if (resized.ok) {
  const next = resized.source as { exec: { items: Array<{ duration?: number }> } };
  assert(next.exec.items[0].duration === 90, `TagClip duration should write back 90, got ${next.exec.items[0].duration}`);
}
const ezreal = first<{ exec?: { items?: unknown[] } }>(
  readJson('mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/abilities.json'),
);
const filled = { id: 'Ability.Overflow', exec: { clockId: 'FixedFrame', items: Array.from({ length: 16 }, () => ({ kind: 'End', tick: 0 })) } };
const overflow = addAbilityExecFromPalette(filled, 'EffectSignal', 0);
assert(!overflow.ok, 'adding a 17th exec item must fail closed');

const presenters = readJson(
  'mods/showcases/capability_standard/CapabilityStandardPresenterCommandShowcaseMod/assets/Presentation/presenters/capability_standard.presenter_command.flash_plaza.json',
);
const flash = first<{ id: string; rules: unknown[] }>(presenters);
const presenterDoc = projectPresenterTimer(flash);
const timerClip = presenterDoc.clips.find((clip) => clip.payload.role === 'timer-set');
assert(timerClip, 'flash presenter should project a TimerSet clip');
assert(timerClip && Math.abs(timerClip.duration - 0.6) < 1e-6, `flash timer duration should be 0.6, got ${timerClip?.duration}`);
assert(
  presenterDoc.clips.some((clip) => clip.payload.role === 'reaction'),
  'flash presenter should project the TimerExpired SetParam reaction',
);
assert(
  presenterDoc.clips.some((clip) => clip.payload.role === 'kill'),
  'flash presenter should project TimerKill',
);
const longer = applyPresenterTimerClipChange(flash, clipIdForRule(1), { duration: 1.2 });
assert(longer.ok, 'resizing TimerSet should succeed');
if (longer.ok) {
  const next = longer.source as { rules: Array<{ command?: { durationSeconds?: number } }> };
  assert(next.rules[1].command?.durationSeconds === 1.2, `TimerSet duration should write back 1.2, got ${next.rules[1].command?.durationSeconds}`);
}
const reserved = applyPresenterTimerClipChange(flash, clipIdForRule(1), {
  payload: { timerName: PRESENTER_DURATION_TIMER_NAME, commandKind: 'TimerSet', durationSeconds: 0.6, eventKind: 'GameplayEvent' },
});
assert(!reserved.ok, 'authoring presenter.duration must fail closed');
const wildcardSet = applyPresenterTimerClipChange(flash, clipIdForRule(1), {
  payload: { timerName: '*', commandKind: 'TimerSet', durationSeconds: 0.6, eventKind: 'GameplayEvent' },
});
assert(!wildcardSet.ok, 'TimerSet named * must fail closed');
const added = addPresenterTimerFromPalette(flash, 'TimerSet', 0);
assert(added.ok, 'adding TimerSet from palette should succeed');

const fixture = first<{ id: string }>(
  readJson('mods/fixtures/presenter_timer/PresenterTimerTestMod/assets/Presentation/presenters.json'),
);
const fixtureDoc = projectPresenterTimer(fixture);
assert(
  fixtureDoc.clips.some((clip) => clip.payload.role === 'timer-set' && clip.payload.timerName === 'pt.flash'),
  'fixture presenter should project pt.flash',
);

if (failures.length > 0) {
  for (const failure of failures) console.error(`FAIL ${failure}`);
  process.exit(1);
}

console.log('timeline adapters: ok');
