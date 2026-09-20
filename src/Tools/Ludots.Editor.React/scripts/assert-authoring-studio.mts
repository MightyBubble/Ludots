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
assert(css.includes('scrollbar-color: var(--studio-fill)'), 'scrollbar must use studio tokens');
assert(css.includes('::-webkit-scrollbar-thumb'), 'webkit scrollbar must be themed');
assert(css.includes('--xy-controls-button-background-color: var(--studio-surface)'), 'zoom controls must not stay xyflow white');

const studioSurfaces = [
  'src/pages/GasGraphEditorPage.tsx',
  'src/pages/AiTopologyEditorPage.tsx',
  'src/pages/gas-graph-editor/GraphVariablePanel.tsx',
  'src/pages/gas-graph-editor/GraphCatalogTree.tsx',
  'src/pages/gas-graph-editor/GraphCodegenPanel.tsx',
  'src/pages/gas-graph-editor/EventEntryInspector.tsx',
  'src/pages/gas-graph-editor/LiveDebugEntryPicker.tsx',
  'src/pages/authoring-studio/AuthoringShell.tsx',
  'src/pages/authoring-studio/AuthoringStudioHome.tsx',
  'src/pages/dialogue-tree-editor/DialogueTreeCanvas.tsx',
  'src/pages/dialogue-tree-editor/dialogueTree.css',
  'src/pages/dialogue-tree-editor/dialogueTreeModel.ts',
  'src/pages/story/SequencerTimelineEditor.tsx',
  'src/pages/StoryAuthoringPage.tsx',
  'src/pages/gas-graph-editor/editor.css',
  'src/pages/gas-graph-editor/gasGraphTheme.ts',
  'src/pages/gas-graph-editor/GasEdges.tsx',
  'src/pages/authoring-studio/authoringTheme.ts',
];
const bannedPalette = /violet-|indigo-|fuchsia-|purple-|cyan-|sky-|#a78bfa|#e879f9|#c084fc|#a855f7|#7c3aed|#8b5cf6|#22d3ee|#67e8f9|#a78bfa/;
for (const rel of studioSurfaces) {
  const text = readFileSync(join(here, '..', rel), 'utf8');
  assert(!bannedPalette.test(text), `${rel} still contains electric purple / indigo leftovers`);
}

const gasPage = readFileSync(join(here, '../src/pages/GasGraphEditorPage.tsx'), 'utf8');
assert(gasPage.includes('删除此节点'), 'blueprint inspector must offer node delete');
assert(gasPage.includes('删除此连线'), 'blueprint inspector must offer edge delete');
const topologyPage = readFileSync(join(here, '../src/pages/AiTopologyEditorPage.tsx'), 'utf8');
assert(topologyPage.includes('删除此节点'), 'topology inspector must offer node delete');
assert(topologyPage.includes('删除此转移'), 'state machine inspector must offer transition delete');
assert(topologyPage.includes('添加节点'), 'topology canvas must offer add-node');
assert(topologyPage.includes('新建拓扑'), 'topology list must offer create');
assert(topologyPage.includes('删除当前拓扑'), 'topology list must offer delete');
assert(topologyPage.includes('尚未写出'), 'mod sources without files must stay selectable');
assert(topologyPage.includes('/api/ai/action-lib?host='), 'topology action picker must query ActionLib');
assert(topologyPage.includes('&source=${encodeURIComponent(source)}'), 'topology action picker must follow the selected mod');
assert(!topologyPage.includes('mod=core&graph='), 'leaf jump must not hardcode Core as the graph owner');
const dialoguePage = readFileSync(join(here, '../src/pages/dialogue-tree-editor/DialogueTreeCanvas.tsx'), 'utf8');
assert(dialoguePage.includes('加一句'), 'dialogue canvas must offer add-say');
assert(dialoguePage.includes('删除此句'), 'dialogue inspector must offer delete-say');
assert(dialoguePage.includes('删除此选项'), 'dialogue inspector must offer delete-choice');
assert(dialoguePage.includes('onNodesDelete'), 'dialogue canvas delete must sync back to the tree');
const storyPage = readFileSync(join(here, '../src/pages/StoryAuthoringPage.tsx'), 'utf8');
assert(storyPage.includes('新建'), 'story catalogs must offer create');
assert(storyPage.includes('删除此轨道') || storyPage.includes('删除'), 'story catalogs must offer delete');

console.log('assert-authoring-studio: ok');
