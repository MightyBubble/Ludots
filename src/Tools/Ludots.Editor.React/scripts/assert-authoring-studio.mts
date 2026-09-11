import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  AUTHORING_STUDIO_HOME,
  AUTHORING_TOOL_IDS,
  AUTHORING_TOOLS,
  matchAuthoringTool,
} from '../src/pages/authoring-studio/authoringTools.ts';
import { STUDIO_THEME } from '../src/pages/authoring-studio/authoringTheme.ts';

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

assert(STUDIO_THEME.bg === 'var(--studio-bg)', 'STUDIO_THEME.bg must alias CSS, not copy hex');
assert(STUDIO_THEME.surface === 'var(--studio-surface)', 'STUDIO_THEME.surface must alias CSS');
assert(STUDIO_THEME.label === 'var(--studio-label)', 'STUDIO_THEME.label must alias CSS');
assert(STUDIO_THEME.blue === 'var(--studio-blue)', 'STUDIO_THEME.blue must alias CSS');
assert(STUDIO_THEME.yellow === 'var(--studio-yellow)', 'STUDIO_THEME.yellow must alias CSS');
assert(STUDIO_THEME.red === 'var(--studio-red)', 'STUDIO_THEME.red must alias CSS');
assert(!('canvas' in STUDIO_THEME), 'abandoned --studio-canvas must not re-enter STUDIO_THEME');
assert(!('wireCyan' in STUDIO_THEME), 'abandoned cyan wire must not re-enter STUDIO_THEME');

const here = dirname(fileURLToPath(import.meta.url));
const css = readFileSync(join(here, '../src/index.css'), 'utf8');
assert(/--studio-bg:\s*#09090b;/.test(css), 'canvas must be zinc-950 from the shadcn zinc table');
assert(/--studio-surface:\s*#18181b;/.test(css), 'node fill must be zinc-900');
assert(/--studio-label:\s*#fafafa;/.test(css), 'label token must exist so body color is not empty');
assert(/--studio-blue:\s*hsl\(220 70% 50%\);/.test(css), 'structure color must be shadcn chart-1');
assert(/--studio-yellow:\s*hsl\(30 80% 55%\);/.test(css), 'control color must be shadcn chart-3');
assert(/--studio-red:\s*hsl\(340 75% 55%\);/.test(css), 'event color must be shadcn chart-5');
assert(!css.includes('--studio-canvas'), 'abandoned canvas token must stay gone');
assert(!css.includes('--studio-wire-cyan'), 'abandoned cyan wire must stay gone');
assert(!/--studio-bg:\s*#1c1c1e/.test(css), 'HIG systemGray must not remain as the canvas');
assert(!/--studio-blue:\s*#0a84ff/.test(css), 'HIG systemBlue must not remain as structure color');

const tailwind = readFileSync(join(here, '../tailwind.config.js'), 'utf8');
assert(tailwind.includes("bg: 'var(--studio-bg)'"), 'tailwind studio.bg must alias CSS vars');
assert(tailwind.includes("blue: 'var(--studio-blue)'"), 'tailwind studio.blue must alias CSS vars');
assert(!tailwind.includes('#1c1c1e'), 'tailwind must not keep a second HIG hex table');

const dialogueCss = readFileSync(join(here, '../src/pages/dialogue-tree-editor/dialogueTree.css'), 'utf8');
assert(dialogueCss.includes('var(--studio-blue)'), 'dialogue nodes must use studio tokens');
assert(!dialogueCss.includes('#0a84ff'), 'dialogue CSS must not keep HIG blue hex');
assert(!dialogueCss.includes('#ffd60a'), 'dialogue CSS must not keep HIG yellow hex');

const editorCss = readFileSync(join(here, '../src/pages/gas-graph-editor/editor.css'), 'utf8');
assert(editorCss.includes('--gas-canvas-bg: var(--studio-bg)'), 'blueprint canvas must alias studio tokens');
assert(!editorCss.includes('#8b1a16'), 'blueprint must not keep the invented event maroon');
assert(!editorCss.includes('#003a73'), 'blueprint must not keep the invented value navy');
assert(!editorCss.includes('#0a84ff'), 'blueprint CSS must not keep HIG blue hex');

console.log('assert-authoring-studio: ok');
