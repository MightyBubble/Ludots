import {
  authoredValue,
  describeSource,
  draftAgent,
  draftProfile,
  issueForField,
  issuesFor,
  setAgentField,
  setProfileField,
  type Issue,
} from '../src/pages/crowd-config/unitTemplates.ts';

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message);
}

const component = 'CrowdSimulationAgent';
const draft = JSON.stringify(
  {
    unitTypes: [
      { id: 'crowd_minimal.team', components: { Name: { Value: 'crowd_minimal.team' } } },
      {
        id: 'crowd_minimal.unit',
        components: {
          Name: { Value: 'crowd_minimal.unit' },
          CrowdSimulationAgent: { profileId: 'crowd_minimal_unit', agentTypeId: 'foot', speedCmPerSecond: 1400, special: false },
        },
      },
      { id: 'derived.unit', extends: 'crowd_minimal.unit' },
    ],
    agentProfiles: [{ id: 'crowd_minimal_unit', radiusCm: 150, heightCm: 180 }],
    agentTypes: { foot: {} },
  },
  null,
  2,
);

assert(authoredValue('number', ' 1650 ') === 1650, 'number fields store finite numbers');
assert(authoredValue('number', '-5') === -5, 'negative numbers go to the loader as numbers so it can reject them');
assert(authoredValue('number', 'fast') === 'fast', 'non-numeric text is passed through for the loader to reject');
assert(authoredValue('number', '') === '', 'an empty number field is passed through, not turned into 0');
assert(authoredValue('bool', true) === true && authoredValue('bool', false) === false, 'bool fields store booleans');
assert(authoredValue('string', 'hover') === 'hover', 'string fields store text as typed');

const faster = setAgentField(draft, component, 'crowd_minimal.unit', 'speedCmPerSecond', 1650);
assert(draftAgent(faster, component, 'crowd_minimal.unit')?.speedCmPerSecond === 1650, 'speed edit lands in the draft');
assert(draftAgent(faster, component, 'crowd_minimal.unit')?.agentTypeId === 'foot', 'other fields stay');
assert(JSON.parse(faster).agentTypes.foot !== undefined, 'other draft sections stay');
assert(faster.includes('\n  "unitTypes": [\n'), 'draft keeps two-space indentation');

const derived = setAgentField(draft, component, 'derived.unit', 'speedCmPerSecond', 900);
const derivedEntry = JSON.parse(derived).unitTypes[2];
assert(derivedEntry.extends === 'crowd_minimal.unit', 'inherited template keeps extends');
assert(
  JSON.stringify(derivedEntry.components) === '{"CrowdSimulationAgent":{"speedCmPerSecond":900}}',
  'inherited template only gets the edited field on its own entry',
);

const wider = setProfileField(draft, 'crowd_minimal_unit', 'radiusCm', 200);
assert(draftProfile(wider, 'crowd_minimal_unit')?.radiusCm === 200, 'radius edit lands on the agent profile');
assert(draftProfile(wider, 'crowd_minimal_unit')?.heightCm === 180, 'other profile fields stay');

let missing = '';
try {
  setAgentField(draft, component, 'nope', 'speedCmPerSecond', 1);
} catch (err) {
  missing = (err as Error).message;
}
assert(missing.includes('"nope"'), 'editing an unknown template reports it instead of inventing an entry');
assert(draftAgent('{not json', component, 'crowd_minimal.unit') === undefined, 'broken draft text reads as no value');

const issues: Issue[] = [
  {
    path: 'CrowdSimulationAgent.speedCmPerSecond',
    message: 'Entities/templates.json: 模板 "crowd_minimal.unit" 的 CrowdSimulationAgent.speedCmPerSecond 需为 > 0 的数值。',
    sourceMod: 'CrowdSimulationMinimalMod',
    sourceFile: 'CrowdSimulationMinimalMod:Entities/templates.json',
  },
  {
    path: 'agents.avoidanceRadiusScale',
    message: 'Entities/templates.json: 模板 "crowd_minimal.unit" 的 personalRadiusCm 与半径 × agents.avoidanceRadiusScale 不一致。',
  },
  { path: '', message: "AgentProfile 'crowd_minimal_unit' requires RadiusCm > 0." },
  { path: 'unitTypes.other.components.CrowdSimulationAgent.pushPriority', message: '下层已经给出这个键。当前 Mod 的覆盖删不掉它。' },
  { path: 'sim.maxUnits', message: 'CrowdSimulationConfig.json: sim.maxUnits 需为整数' },
];

const mine = issuesFor(issues, 'crowd_minimal.unit', 'crowd_minimal_unit');
assert(mine.length === 3, `issues naming the template or its profile belong to it, got ${mine.length}`);
assert(issuesFor(issues, 'other').length === 1, 'omission issues are matched by their unitTypes path');
assert(issueForField(mine, 'speedCmPerSecond') === issues[0], 'speed issue is attached to the speed field');
assert(issueForField(mine, 'personalRadiusCm') === issues[1], 'mismatch issue is attached to personalRadiusCm');
assert(issueForField(mine, 'radiusCm') === issues[2], 'profile radius issue is attached to radiusCm');
assert(issueForField(mine, 'agentTypeId') === undefined, 'fields without an issue get no hint');

const source = { sourceMod: 'CrowdSimulationDemoContentMod', sourceFile: 'CrowdSimulationDemoContentMod:Entities/templates.json' };
assert(describeSource(source) === 'CrowdSimulationDemoContentMod · Entities/templates.json', 'source shows mod and file');
assert(
  describeSource({ ...source, template: 'base' }, 'derived.unit').endsWith('继承自 base'),
  'inherited fields name the template that wrote them',
);
assert(describeSource(undefined) === '没有片段写过', 'missing source is said plainly');

console.log('crowd unit templates assert passed');
