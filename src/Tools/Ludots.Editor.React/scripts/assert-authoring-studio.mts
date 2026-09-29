import { readFileSync, readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  AUTHORING_SECTIONS,
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
assert(
  AUTHORING_SECTIONS.map((section) => section.id).join(',') === 'author,world,run',
  `studio sections must be author, world, run (#1699); got ${AUTHORING_SECTIONS.map((section) => section.id).join(',')}`,
);
for (const section of AUTHORING_SECTIONS) {
  assert(AUTHORING_TOOLS.some((tool) => tool.section === section.id), `${section.id} section must have tools`);
}
for (const tool of AUTHORING_TOOLS) {
  assert(
    AUTHORING_SECTIONS.some((section) => section.id === tool.section),
    `${tool.id} must belong to a studio section`,
  );
}
assert(AUTHORING_TOOLS.length === 9, `studio must list exactly 9 tools, got ${AUTHORING_TOOLS.length}`);
assert(
  AUTHORING_TOOL_IDS.join(',') === 'blueprint,bt,fsm,dialogue,text,timeline,map,panels,run',
  `studio tool order must be blueprint, bt, fsm, dialogue, text, timeline, map, panels, run; got ${AUTHORING_TOOL_IDS.join(',')}`,
);

const forbidden = ['/data'];
for (const tool of AUTHORING_TOOLS) {
  assert(tool.path.startsWith('/'), `${tool.id} path must be a route`);
  assert(!forbidden.includes(tool.path), `${tool.id} must not point at data pages`);
  assert(tool.title.length > 0, `${tool.id} needs a title`);
  assert(tool.blurb.length > 0, `${tool.id} needs a player-facing blurb`);
}

assert(matchAuthoringTool('/gas-graphs')?.id === 'blueprint', '/gas-graphs must stay a blueprint alias');
assert(matchAuthoringTool('/story-authoring')?.id === 'dialogue', '/story-authoring must stay a dialogue alias');
assert(matchAuthoringTool('/blueprint')?.id === 'blueprint', '/blueprint is the studio card path');
assert(matchAuthoringTool('/dialogue')?.id === 'dialogue', '/dialogue is the studio card path');
assert(matchAuthoringTool('/timeline')?.id === 'timeline', '/timeline is a first-class studio room');
assert(matchAuthoringTool('/text-bank')?.id === 'text', '/text-bank is a first-class studio room');
assert(matchAuthoringTool('/map')?.id === 'map', '/map is a first-class world-section tool (#1699)');
assert(matchAuthoringTool('/ui-panel-authoring')?.id === 'panels', '/ui-panel-authoring must stay a panels alias');
assert(matchAuthoringTool('/panel-authoring')?.id === 'panels', '/panel-authoring is the panels card path');
assert(matchAuthoringTool('/run')?.id === 'run', '/run is the launch section (#1699)');
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
  'src/pages/text-bank/TextBankPage.tsx',
  'src/pages/text-bank/textBank.css',
  'src/pages/text-bank/textBankModel.ts',
  'src/pages/text-bank/RichTextArea.tsx',
  'src/pages/dialogue-tree-editor/StatementInspector.tsx',
  'src/pages/dialogue-tree-editor/inlineAuthoring.ts',
  'src/pages/run/RunPage.tsx',
  'src/components/ui/Button.tsx',
  'src/components/ui/Field.tsx',
  'src/components/ui/NavTab.tsx',
  'src/components/ui/Panel.tsx',
  'src/components/ui/Badge.tsx',
  'src/components/ui/Collapse.tsx',
  'src/components/ui/chrome.ts',
  'src/components/agent/AgentDock.tsx',
  'src/components/agent/Conversation.tsx',
  'src/components/agent/Message.tsx',
  'src/components/agent/PromptInput.tsx',
  'src/components/agent/ToolCard.tsx',
  'src/components/agent/llm.ts',
  'src/components/agent/tools.ts',
  'src/components/Editor/EditorLayout.tsx',
  'src/components/Editor/Toolbar.tsx',
  'src/components/Editor/HexRenderer.tsx',
  'src/components/Editor/Minimap.tsx',
  'src/pages/UiPanelAuthoringPage.tsx',
  'src/pages/ui-panel-authoring/authoring.css',
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
assert(dialoguePage.includes('onNodesDelete'), 'dialogue canvas delete must sync back to the tree');
const statementInspector = readFileSync(join(here, '../src/pages/dialogue-tree-editor/StatementInspector.tsx'), 'utf8');
assert(statementInspector.includes('删除此句'), 'dialogue inspector must offer delete-say');
assert(statementInspector.includes('删除此选项'), 'dialogue inspector must offer delete-choice');
assert(statementInspector.includes('新建说话人'), 'dialogue inspector must offer inline speaker quick-add');
assert(statementInspector.includes('RichTextArea'), 'dialogue inspector reuses the shared markup editor');
const storyPage = readFileSync(join(here, '../src/pages/StoryAuthoringPage.tsx'), 'utf8');
assert(storyPage.includes('新建'), 'story catalogs must offer create');
assert(storyPage.includes('删除此轨道') || storyPage.includes('删除'), 'story catalogs must offer delete');
const textBankPage = readFileSync(join(here, '../src/pages/text-bank/TextBankPage.tsx'), 'utf8');
assert(textBankPage.includes('缺这条翻译'), 'text bank must flag missing translations');
assert(textBankPage.includes('story/text/validate'), 'text bank save must gate on the engine validate endpoint');
const richTextArea = readFileSync(join(here, '../src/pages/text-bank/RichTextArea.tsx'), 'utf8');
assert(richTextArea.includes('wrapSelection'), 'inline markup toolbar lives in the shared RichTextArea');
for (const rel of [
  'src/pages/authoring-studio/authoringTheme.ts',
  'src/pages/StoryAuthoringPage.tsx',
  'src/pages/AiTopologyEditorPage.tsx',
  'src/pages/GasGraphEditorPage.tsx',
  'src/pages/dialogue-tree-editor/StatementInspector.tsx',
  'src/pages/dialogue-tree-editor/DialogueTreeCanvas.tsx',
  'src/pages/run/RunPage.tsx',
  'src/pages/authoring-studio/AuthoringShell.tsx',
]) {
  const text = readFileSync(join(here, '..', rel), 'utf8');
  assert(!text.includes('STUDIO_CHROME'), `${rel} must use components/ui primitives, not STUDIO_CHROME`);
}
const uiDir = join(here, '../src/components/ui');
for (const uiFile of readdirSync(uiDir)) {
  const text = readFileSync(join(uiDir, uiFile), 'utf8');
  assert(!/#[0-9a-fA-F]{3,8}/.test(text), `components/ui/${uiFile} must not hardcode hex colors`);
}
const shellPage = readFileSync(join(here, '../src/pages/authoring-studio/AuthoringShell.tsx'), 'utf8');
assert(shellPage.includes('AgentDock'), 'studio shell must host the agent copilot dock (#1699 S3)');
assert(shellPage.includes('data-agent-dock-toggle'), 'shell must offer the dock toggle affordance');
for (const rel of [
  'src/pages/run/RunPage.tsx',
  'src/pages/text-bank/TextBankPage.tsx',
  'src/pages/AiTopologyEditorPage.tsx',
  'src/pages/GasGraphEditorPage.tsx',
]) {
  const text = readFileSync(join(here, '..', rel), 'utf8');
  assert(text.includes('WorkspaceLayout'), `${rel} must follow the workspace layout contract (#1699 L1-L3)`);
  assert(!text.includes('window.alert'), `${rel} must surface errors in the status bar, not window.alert`);
}
const gasDialect = readFileSync(join(here, '../src/pages/gas-graph-editor/graphEditorDialect.ts'), 'utf8');
assert(!gasDialect.includes('Ludots Graph Editor'), 'dialect titles speak facts in Chinese, no product-name circulars');
assert(!gasDialect.includes('double-click to open'), 'dialect subtitles must be concrete Chinese, not English filler');
const gasPageCopy = readFileSync(join(here, '../src/pages/GasGraphEditorPage.tsx'), 'utf8');
assert(!gasPageCopy.includes('>Load<') && !gasPageCopy.includes('>Validate</') && !gasPageCopy.includes('>Auto Layout'), 'blueprint buttons use terse Chinese verbs');
assert(!gasPageCopy.includes('dialectNavClass'), 'room navigation lives in the shell nav only — no duplicate dialect-link clusters');
for (const banned of ['No runtime node matches.', 'Middle-drag to pan', 'Live Debug · ', 'No trace changes yet.', 'Add Event', '>To node<', '>To port<', '>Inspector<', '>Diagnostics<']) {
  assert(!gasPageCopy.includes(banned), `blueprint room chrome must be Chinese: banned "${banned}"`);
}
const gasComponents = readdirSync(join(here, '../src/pages/gas-graph-editor')).filter((f) => f.endsWith('.tsx'));
for (const comp of gasComponents) {
  const text = readFileSync(join(here, '../src/pages/gas-graph-editor', comp), 'utf8');
  for (const banned of ['Select mounted entry', 'Pick a registered event', 'Not eligible', 'Parity mismatch', '>Variables<', '>Placed instances<', '>Add<', '>Update<', '>Delete<']) {
    assert(!text.includes(banned), `gas-graph-editor/${comp} chrome must be Chinese: banned "${banned}"`);
  }
}
const topologyCopy = readFileSync(join(here, '../src/pages/AiTopologyEditorPage.tsx'), 'utf8');
assert(!topologyCopy.includes('to="/gas-graphs"'), 'topology room must not re-link sibling rooms — the shell nav is the only entrance');
const catalogTree = readFileSync(join(here, '../src/pages/gas-graph-editor/GraphCatalogTree.tsx'), 'utf8');
assert(catalogTree.includes('graphKindLabel'), 'catalog tree must localize graph-kind chips through graphKindLabel');
const llmLib = readFileSync(join(here, '../src/components/agent/llm.ts'), 'utf8');
assert(!llmLib.includes('sk-'), 'llm layer must not ship a default API key');
assert(llmLib.includes('localStorage'), 'llm config must stay in browser-local storage');
const runPage = readFileSync(join(here, '../src/pages/run/RunPage.tsx'), 'utf8');
assert(runPage.includes('/api/launch'), 'run page must launch through the bridge contract (#1699)');
assert(runPage.includes('/api/launcher/state'), 'run page must read launcher state through the bridge contract');
assert(runPage.includes('agent-bridge'), 'run page must surface the live-debug agent bridge channel');
assert(!/\bstatus=\{status\}/.test(runPage), 'run page footer must show a real fact — bare `status={status}` leaks the deprecated window.status global');

const storyContract = readFileSync(join(here, '../src/pages/StoryAuthoringPage.tsx'), 'utf8');
assert(storyContract.includes('WorkspaceLayout'), 'dialogue/timeline rooms must follow the workspace layout contract');
assert(storyContract.includes('DialogueTreeInspector'), 'dialogue inspector renders in the layout inspector rail, not inside the canvas');
assert(!storyContract.includes('pageClass'), 'story rooms must not keep the legacy pageClass shell');
assert(!storyContract.includes('目标 Mod'), 'mod picker label is Mod everywhere');
assert(storyContract.includes('writeStudioMod'), 'story rooms share the studio mod memory');
assert(topologyCopy.includes('writeStudioMod'), 'topology room shares the studio mod memory');
assert(!topologyCopy.includes('重新加载'), 'topology reload button is 重载 like every room');
assert(topologyCopy.includes('aria-label="连线模式"'), 'topology connect mode is a canvas tool, not a header control');
assert(gasPageCopy.includes('datalist id="studio-graph-mods"'), 'blueprint mod/graph inputs live in the rail with catalog suggestions');
assert(!gasPageCopy.includes("'Idle'"), 'blueprint status starts in Chinese');
const studioMod = readFileSync(join(here, '../src/pages/authoring-studio/useStudioMod.ts'), 'utf8');
assert(studioMod.includes('localStorage'), 'studio mod memory persists in browser-local storage');

const editorLayout = readFileSync(join(here, '../src/components/Editor/EditorLayout.tsx'), 'utf8');
assert(editorLayout.includes('WorkspaceLayout'), 'map room must follow the workspace layout contract');
assert(!editorLayout.includes('w-screen'), 'map room must not double-frame inside the studio shell');
const mapToolbar = readFileSync(join(here, '../src/components/Editor/Toolbar.tsx'), 'utf8');
assert(!mapToolbar.includes('alert('), 'map room errors go to the status bar via setError, never alert()');
assert(!mapToolbar.includes('>Ludots Editor<'), 'map room has no private brand header — the shell owns titles');
assert(!/(slate|emerald|amber|sky)-\d{2,3}/.test(mapToolbar), 'map toolbar must use studio tokens, not a private slate/emerald/amber palette');
const mapHex = readFileSync(join(here, '../src/components/Editor/HexRenderer.tsx'), 'utf8');
assert(!/(slate|emerald|amber)-\d{2,3}/.test(mapHex), 'map canvas chrome must use studio tokens');
const panelPage = readFileSync(join(here, '../src/pages/UiPanelAuthoringPage.tsx'), 'utf8');
assert(panelPage.includes('WorkspaceLayout'), 'panel room must follow the workspace layout contract');
assert(!panelPage.includes('window.alert'), 'panel room export errors go to the status bar');
assert(!panelPage.includes('和你原型的对应'), 'panel room must not ship conversation residue asides');
const panelCss = readFileSync(join(here, '../src/pages/ui-panel-authoring/authoring.css'), 'utf8');
assert(!panelCss.includes('--upa-'), 'panel room keeps no private upa-* theme variables — studio tokens only');

console.log('assert-authoring-studio: ok');
