import {
  AUTHORING_STUDIO_HOME,
  AUTHORING_TOOL_IDS,
  AUTHORING_TOOLS,
  matchAuthoringTool,
} from '../src/pages/authoring-studio/authoringTools.ts';

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message);
}

assert(AUTHORING_STUDIO_HOME === '/', 'studio home must be / so one-click opens the desk, not the map');
assert(AUTHORING_TOOLS.length === 5, `studio must list exactly 5 tools, got ${AUTHORING_TOOLS.length}`);
assert(
  AUTHORING_TOOL_IDS.join(',') === 'blueprint,bt,fsm,dialogue,timeline',
  `studio tool order must be blueprint, bt, fsm, dialogue, timeline; got ${AUTHORING_TOOL_IDS.join(',')}`,
);

const forbidden = ['/map', '/ui-panel-authoring', '/gas', '/data'];
for (const tool of AUTHORING_TOOLS) {
  assert(tool.path.startsWith('/'), `${tool.id} path must be a route`);
  assert(!forbidden.includes(tool.path), `${tool.id} must not point at map/panel/data`);
  assert(tool.title.length > 0, `${tool.id} needs a title`);
  assert(tool.blurb.length > 0, `${tool.id} needs a player-facing blurb`);
}

assert(matchAuthoringTool('/gas-graphs')?.id === 'blueprint', '/gas-graphs must stay a blueprint alias');
assert(matchAuthoringTool('/story-authoring')?.id === 'dialogue', '/story-authoring must stay a dialogue alias');
assert(matchAuthoringTool('/blueprint')?.id === 'blueprint', '/blueprint is the studio card path');
assert(matchAuthoringTool('/dialogue')?.id === 'dialogue', '/dialogue is the studio card path');
assert(matchAuthoringTool('/timeline')?.id === 'timeline', '/timeline is a first-class studio room');
assert(matchAuthoringTool('/map') === undefined, 'map editor must not be a studio tool');
assert(matchAuthoringTool('/ui-panel-authoring') === undefined, 'panel authoring must not be a studio tool');
assert(AUTHORING_TOOLS.find((tool) => tool.id === 'dialogue')?.blurb.includes('树'), 'dialogue card must say it is a tree');

console.log('assert-authoring-studio: ok');
