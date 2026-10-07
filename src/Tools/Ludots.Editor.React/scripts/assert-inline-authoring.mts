import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { validateDialogueTree, type DialogueTree } from '../src/pages/dialogue-tree-editor/dialogueTreeModel.ts';
import {
  draftKeyForChoice,
  draftKeyForNode,
  planInlineSync,
  slug,
  type InlineSyncInput,
  type LineRow,
  type SpeakerRow,
} from '../src/pages/dialogue-tree-editor/inlineAuthoring.ts';
import type { LocaleRoot, TextTokenRow } from '../src/pages/text-bank/textBankModel.ts';

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message);
}

const here = dirname(fileURLToPath(import.meta.url));
const base = join(here, '../../../../mods/showcases/dialogue_author_kit/DialogueAuthorKitShowcaseMod/assets');
const read = (rel: string) => JSON.parse(readFileSync(join(base, rel), 'utf8'));

const dialogues = read('Dialogue/dialogues.json') as DialogueTree[];
const lines = read('Story/lines.json') as LineRow[];
const speakers = read('Story/speakers.json') as SpeakerRow[];
const tokens = read('Presentation/text_tokens.json') as TextTokenRow[];
const localeRoot = read('Presentation/text_locales.json') as LocaleRoot;

const gate = dialogues.find((row) => row.id === 'Dialogue.AuthorKit.Gate')!;
assert(gate, 'AuthorKit gate dialogue must exist');

function input(overrides: Partial<InlineSyncInput> = {}): InlineSyncInput {
  return {
    dialogues: structuredClone(dialogues),
    lines: structuredClone(lines),
    speakers: structuredClone(speakers),
    tokens: structuredClone(tokens),
    localeRoot: structuredClone(localeRoot),
    drafts: {},
    newSpeakers: [],
    ...overrides,
  };
}

// 1) No drafts → nothing moves; the planner is a no-op on untouched data.
const idle = planInlineSync(input());
assert(idle.ok, 'no-draft plan must succeed');
if (idle.ok) {
  assert(JSON.stringify(idle.plan.dialogues) === JSON.stringify(dialogues), 'no-draft plan must not touch dialogues');
  assert(JSON.stringify(idle.plan.lines) === JSON.stringify(lines), 'no-draft plan must not touch lines');
  assert(JSON.stringify(idle.plan.tokens) === JSON.stringify(tokens), 'no-draft plan must not touch tokens');
  assert(JSON.stringify(idle.plan.localeRoot) === JSON.stringify(localeRoot), 'no-draft plan must not touch locales');
  assert(idle.plan.createdLineIds.length === 0 && idle.plan.createdTokenIds.length === 0, 'no-draft plan creates nothing');
  assert(idle.plan.orphanLineIds.length === 0, 'every AuthorKit line is referenced by the tree');
}

// 2) Editing a bound line rewrites only the default-locale template.
const boundEdit = planInlineSync(
  input({ drafts: { 'line.author_kit.guard.open': { text: '新正文，<b>重点</b>在这里。' } } }),
);
assert(boundEdit.ok, 'bound edit plan must succeed');
if (boundEdit.ok) {
  assert(
    boundEdit.plan.localeRoot.locales['zh-CN']!['story.author_kit.guard.open'] === '新正文，<b>重点</b>在这里。',
    'bound edit writes the default-locale template',
  );
  assert(
    boundEdit.plan.localeRoot.locales['en-US']!['story.author_kit.guard.open'] === localeRoot.locales['en-US']!['story.author_kit.guard.open'],
    'bound edit must not touch other locales',
  );
  assert(boundEdit.plan.createdLineIds.length === 0, 'bound edit creates no new line');
  assert(boundEdit.plan.lines.every((line) => line.id !== 'line.author_kit.guard.open' || line.speakerId === 'speaker.guard'), 'bound edit keeps the line row');
}

// 3) Typing text on an unbound say node derives line + token + template.
const unboundTree = structuredClone(dialogues);
const unboundGate = unboundTree.find((row) => row.id === 'Dialogue.AuthorKit.Gate')!;
const openNode = unboundGate.nodes.find((node) => node.id === 'open')!;
openNode.lineId = '';
const created = planInlineSync(
  input({
    dialogues: unboundTree,
    drafts: { [draftKeyForNode(unboundGate, 'open')]: { speakerId: 'speaker.guard', text: '站住！<color=#FFCC5500>口令</color>。' } },
  }),
);
assert(created.ok, 'unbound draft plan must succeed');
if (created.ok) {
  assert(slug('Dialogue.AuthorKit.Gate') === 'dialogue_author_kit_gate', 'slug matches the house line-id style');
  const openAfter = created.plan.dialogues
    .find((row) => row.id === 'Dialogue.AuthorKit.Gate')!
    .nodes.find((node) => node.id === 'open')!;
  assert(openAfter.lineId === 'line.dialogue_author_kit_gate.open', `derived lineId, got ${openAfter.lineId}`);
  const newLine = created.plan.lines.find((line) => line.id === 'line.dialogue_author_kit_gate.open');
  assert(newLine?.speakerId === 'speaker.guard' && newLine.textToken === 'story.line.dialogue_author_kit_gate.open', 'derived line binds speaker and token');
  assert(created.plan.tokens.some((token) => token.id === 'story.line.dialogue_author_kit_gate.open' && token.argCount === 0), 'token row appended');
  assert(created.plan.localeRoot.locales['zh-CN']!['story.line.dialogue_author_kit_gate.open'] === '站住！<color=#FFCC5500>口令</color>。', 'template written into default locale');
  assert(created.plan.orphanLineIds.includes('line.author_kit.guard.open'), 'the old unreferenced line stays in the book as an orphan, not deleted');
  assert(validateDialogueTree(created.plan.dialogues.find((row) => row.id === 'Dialogue.AuthorKit.Gate')!) === null, 'plan output still passes tree validation');
}

// 4) Derived ids never collide: an existing occupant pushes the suffix.
const occupied = structuredClone(lines);
occupied.push({ id: 'line.dialogue_author_kit_gate.open', speakerId: 'speaker.guard', textToken: 'x', tags: [] });
const collided = planInlineSync(
  input({
    dialogues: unboundTree,
    lines: occupied,
    drafts: { [draftKeyForNode(unboundGate, 'open')]: { speakerId: 'speaker.guard', text: '占位冲突' } },
  }),
);
assert(collided.ok, 'collision plan must succeed');
if (collided.ok) {
  assert(collided.plan.createdLineIds.includes('line.dialogue_author_kit_gate.open_2'), 'collision appends _2 instead of overwriting');
}

// 5) Unbound choice text derives a choice-scoped line id.
const choiceTree = structuredClone(dialogues);
const choiceGate = choiceTree.find((row) => row.id === 'Dialogue.AuthorKit.Gate')!;
const choiceNode = choiceGate.nodes.find((node) => node.id === 'open')!;
const writePass = choiceNode.choices!.find((choice) => choice.id === 'write_pass')!;
writePass.lineId = '';
const choicePlan = planInlineSync(
  input({
    dialogues: choiceTree,
    drafts: { [draftKeyForChoice(choiceGate, 'open', 'write_pass')]: { speakerId: 'speaker.traveler', text: '我这就写。' } },
  }),
);
assert(choicePlan.ok, 'choice draft plan must succeed');
if (choicePlan.ok) {
  const bound = choicePlan.plan.dialogues
    .find((row) => row.id === 'Dialogue.AuthorKit.Gate')!
    .nodes.find((node) => node.id === 'open')!
    .choices!.find((choice) => choice.id === 'write_pass')!;
  assert(bound.lineId === 'line.dialogue_author_kit_gate.open__write_pass', `choice line id, got ${bound.lineId}`);
}

// 6) Quick-add speaker lands with a derived display-name token.
const withSpeaker = planInlineSync(
  input({ newSpeakers: [{ id: 'speaker.scout', displayName: '斥候', portraitImageId: 'portrait.speaker.scout' }] }),
);
assert(withSpeaker.ok, 'quick-add speaker plan must succeed');
if (withSpeaker.ok) {
  const scout = withSpeaker.plan.speakers.find((row) => row.id === 'speaker.scout');
  assert(scout?.displayNameToken === 'story.speaker.scout.name', 'speaker display token derived');
  assert(withSpeaker.plan.localeRoot.locales['zh-CN']!['story.speaker.scout.name'] === '斥候', 'speaker name written to default locale');
  assert(withSpeaker.plan.tokens.some((token) => token.id === 'story.speaker.scout.name'), 'speaker token appended');
  const dupe = planInlineSync(
    input({
      speakers: withSpeaker.plan.speakers,
      newSpeakers: [{ id: 'speaker.scout', displayName: '又一个' }],
    }),
  );
  assert(!dupe.ok, 'duplicate quick-add must fail closed');
}

// 7) A node bound to a missing line fails closed instead of silently dropping.
const brokenTree = structuredClone(dialogues);
brokenTree.find((row) => row.id === 'Dialogue.AuthorKit.Gate')!.nodes.find((node) => node.id === 'open')!.lineId = 'line.nope';
const broken = planInlineSync(input({ dialogues: brokenTree }));
assert(!broken.ok, 'missing bound line must fail closed');

// 8) First line in a mod without any bank bootstraps zh-CN.
const freshTree: DialogueTree = {
  id: 'Dialogue.Fresh',
  displayName: '新对话',
  entryNode: 'open',
  nodes: [{ id: 'open', lineId: '', presentationProfile: 'story.dialogue_overlay', choices: [] }],
};
const fresh = planInlineSync({
  dialogues: [freshTree],
  lines: [],
  speakers: [],
  tokens: [],
  localeRoot: null,
  drafts: { [draftKeyForNode(freshTree, 'open')]: { speakerId: '', text: '第一句' } },
  newSpeakers: [],
});
assert(fresh.ok, 'fresh-bank plan must succeed');
if (fresh.ok) {
  assert(fresh.plan.localeRoot.defaultLocale === 'zh-CN', 'fresh bank defaults to zh-CN');
  assert(Object.keys(fresh.plan.localeRoot.locales).includes('zh-CN'), 'fresh bank has the zh-CN table');
}

console.log('assert-inline-authoring: ok');
