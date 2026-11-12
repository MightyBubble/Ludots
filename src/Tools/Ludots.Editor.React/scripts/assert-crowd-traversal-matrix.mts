import {
  setEndpoint,
  setMaxSlope,
  toggleLink,
  togglePassable,
  type Traversal,
} from '../src/pages/crowd-config/traversalMatrix.ts';

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message);
}

const base: Traversal = {
  areas: { open: { ignoresSlope: false }, deep: { ignoresSlope: true } },
  terrainAreas: { land: 'open', water: 'deep' },
  linkKinds: { jump: { traversalSpeedRatio: 1.3 } },
  agentTypes: {
    leaper: {
      maxSlopeDeg: 30,
      links: { jump: { upCm: 2500, downCm: 20000, rangeNavCells: 7, costPerNavCell: 2 } },
      cells: {
        open: { cost: 1, speedDivisor: 1, depart: ['jump'], arrive: ['jump'] },
        deep: false,
      },
    },
  },
};
const frozen = JSON.stringify(base);

const noJump = toggleLink(base, base, 'leaper', 'jump', false);
assert(noJump.agentTypes.leaper.links.jump === false, 'turning a link off writes false');
const openCell = noJump.agentTypes.leaper.cells.open;
assert(openCell !== false && openCell.depart.length === 0 && openCell.arrive.length === 0, 'turning a link off clears its endpoints, so the loader does not see a dangling depart');
assert(JSON.stringify(base) === frozen, 'edits do not mutate the loaded traversal');

const jumpBack = toggleLink(noJump, base, 'leaper', 'jump', true);
const back = jumpBack.agentTypes.leaper.links.jump;
assert(back !== false && back.rangeNavCells === 7, 'turning a link back on takes the default row value');

const wet = togglePassable(noJump, base, 'leaper', 'deep', true);
const deep = wet.agentTypes.leaper.cells.deep;
assert(deep !== false && deep.cost === 1 && deep.speedDivisor === 1, 'a cell the defaults mark false opens with cost 1 and divisor 1');
assert(deep !== false && deep.depart.length === 0, 'a new cell starts without endpoints');

const reopened = togglePassable(noJump, base, 'leaper', 'open', true);
const reopenedCell = reopened.agentTypes.leaper.cells.open;
assert(reopenedCell !== false && reopenedCell.depart.length === 0, 'reopening a cell drops endpoints for links the row turned off');
assert(togglePassable(base, base, 'leaper', 'open', false).agentTypes.leaper.cells.open === false, 'closing a cell writes false');

const twice = setEndpoint(setEndpoint(base, 'leaper', 'open', 'depart', 'jump', true), 'leaper', 'open', 'depart', 'jump', true);
const twiceCell = twice.agentTypes.leaper.cells.open;
assert(twiceCell !== false && twiceCell.depart.length === 1, 'ticking an endpoint twice does not duplicate it');

assert(setMaxSlope(base, 'leaper', null).agentTypes.leaper.maxSlopeDeg === null, 'no slope limit is written as null');

console.log('crowd traversal matrix assert passed');
