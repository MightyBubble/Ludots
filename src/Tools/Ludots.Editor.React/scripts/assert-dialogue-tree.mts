import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  applyFlowToDialogue,
  choiceHubId,
  dialogueToFlow,
  hubWidth,
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
const sayNodes = flow.nodes.filter((node) => node.type === 'dialogueSay');
const hubNodes = flow.nodes.filter((node) => node.type === 'dialogueChoice');
assert(sayNodes.length === 4, `gate tree has 4 say nodes, got ${sayNodes.length}`);
assert(hubNodes.length === 2, `open and recorded each get a choice hub, got ${hubNodes.length}`);
assert(sayNodes.some((node) => node.data.kind === 'say' && node.data.isEntry && node.id === 'open'), 'open is the START node');
assert(
  hubNodes.some((node) => node.id === choiceHubId('open') && node.data.kind === 'choiceHub' && node.data.choices.length === 3),
  'open hub carries three choices',
);

const choiceEdges = flow.edges.filter((edge) => parseChoiceHandle(edge.sourceHandle));
assert(choiceEdges.length === 4, `expected 4 choice edges, got ${choiceEdges.length}`);
assert(
  choiceEdges.every((edge) => edge.source === choiceHubId('open') || edge.source === choiceHubId('recorded')),
  'choice edges leave the hub, not the say node',
);
assert(
  flow.edges.filter((edge) => (edge.data as { kind?: string } | undefined)?.kind === 'hub').length === 2,
  'each choice-bearing say has one hub wire',
);
assert(
  flow.edges.every((edge) => edge.label === undefined),
  'flow/node canvas wires do not put labels on the path',
);
assert(
  flow.edges.every((edge) => edge.sourceHandle !== 'next' || (edge.data as { kind?: string }).kind === 'hub'),
  'AuthorKit gate uses choices, not linear next',
);

const pos = Object.fromEntries(flow.nodes.map((node) => [node.id, node.position]));
assert(pos.open.y < pos[choiceHubId('open')].y, 'choice hub sits under its say');
assert(pos[choiceHubId('open')].y < pos.recorded.y, 'choice targets sit below the hub');
assert(pos.recorded.x !== pos.allowed.x && pos.allowed.x !== pos.bye.x, 'choice targets fan out horizontally');
assert(Math.abs(pos.recorded.y - pos.allowed.y) < 1, 'sibling says share a row');
assert(Math.abs(pos.allowed.y - pos.bye.y) < 1, 'sibling says share a row');

const boxes = flow.nodes.map((node) => {
  const choiceCount = node.data.kind === 'choiceHub' ? node.data.choices.length : 0;
  const width = node.type === 'dialogueChoice' ? hubWidth(choiceCount) : 248;
  const height = node.type === 'dialogueChoice' ? 88 : 96;
  return { id: node.id, x: node.position.x, y: node.position.y, width, height };
});
for (let i = 0; i < boxes.length; i += 1) {
  for (let j = i + 1; j < boxes.length; j += 1) {
    const a = boxes[i];
    const b = boxes[j];
    const overlap =
      a.x < b.x + b.width && a.x + a.width > b.x && a.y < b.y + b.height && a.y + a.height > b.y;
    assert(!overlap, `nodes overlap: ${a.id} vs ${b.id}`);
  }
}

const roundTrip = applyFlowToDialogue(gate, flow.nodes, flow.edges);
assert(roundTrip.entryNode === 'open', 'entry survives round-trip');
assert(roundTrip.nodes.length === 4, 'hub nodes are not written into Dialogue JSON');
const open = roundTrip.nodes.find((node) => node.id === 'open');
assert(open?.choices?.find((choice) => choice.id === 'write_pass')?.nextNode === 'recorded', 'write_pass → recorded');
assert(open?.choices?.find((choice) => choice.id === 'ask_enter')?.conditionGraphId === 'Graph.AuthorKit.Condition.PassGranted', 'condition graph stays on the choice');
assert(open?.choices?.find((choice) => choice.id === 'ask_enter')?.nextNode === 'allowed', 'ask_enter → allowed');
assert(open?.choices?.find((choice) => choice.id === 'leave')?.nextNode === 'bye', 'leave → bye');
assert(open?.nextNode === undefined, 'hub wire does not become nextNode');

const problem = validateDialogueTree(roundTrip);
assert(problem === null, `valid tree must pass runtime-shaped checks, got: ${problem}`);

const broken = { ...gate, nodes: gate.nodes.map((node) => ({ ...node, lineId: node.id === 'open' ? '' : node.lineId })) };
assert(validateDialogueTree(broken) !== null, 'missing lineId must fail closed before save');

console.log('assert-dialogue-tree: ok');
