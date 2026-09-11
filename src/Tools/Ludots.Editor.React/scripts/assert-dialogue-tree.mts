import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  applyFlowToDialogue,
  dialogueToFlow,
  parseChoiceHandle,
  validateDialogueTree,
  type DialogueTree,
} from '../src/pages/dialogue-tree-editor/dialogueTreeModel.ts';

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message);
}

const here = dirname(fileURLToPath(import.meta.url));
const gatePath = join(
  here,
  '../../../../mods/showcases/dialogue_author_kit/DialogueAuthorKitShowcaseMod/assets/Dialogue/dialogues.json',
);
const catalog = JSON.parse(readFileSync(gatePath, 'utf8')) as DialogueTree[];
const gate = catalog.find((row) => row.id === 'Dialogue.AuthorKit.Gate');
assert(gate, 'AuthorKit gate dialogue must exist for canvas round-trip');

const flow = dialogueToFlow(gate);
assert(flow.nodes.length === 4, 'gate tree has 4 statement nodes');
assert(flow.nodes.some((node) => node.data.isEntry && node.id === 'open'), 'open is the START node');

const choiceEdges = flow.edges.filter((edge) => parseChoiceHandle(edge.sourceHandle));
assert(choiceEdges.length === 4, `expected 4 choice edges, got ${choiceEdges.length}`);
assert(
  flow.edges.every((edge) => edge.sourceHandle !== 'next'),
  'AuthorKit gate uses choices, not linear next',
);

const roundTrip = applyFlowToDialogue(gate, flow.nodes, flow.edges);
assert(roundTrip.entryNode === 'open', 'entry survives round-trip');
const open = roundTrip.nodes.find((node) => node.id === 'open');
assert(open?.choices?.find((choice) => choice.id === 'write_pass')?.nextNode === 'recorded', 'write_pass → recorded');
assert(open?.choices?.find((choice) => choice.id === 'ask_enter')?.conditionGraphId === 'Graph.AuthorKit.Condition.PassGranted', 'condition graph stays on the choice');
assert(open?.choices?.find((choice) => choice.id === 'ask_enter')?.nextNode === 'allowed', 'ask_enter → allowed');
assert(open?.choices?.find((choice) => choice.id === 'leave')?.nextNode === 'bye', 'leave → bye');

const problem = validateDialogueTree(roundTrip);
assert(problem === null, `valid tree must pass runtime-shaped checks, got: ${problem}`);

const broken = { ...gate, nodes: gate.nodes.map((node) => ({ ...node, lineId: node.id === 'open' ? '' : node.lineId })) };
assert(validateDialogueTree(broken) !== null, 'missing lineId must fail closed before save');

console.log('assert-dialogue-tree: ok');
