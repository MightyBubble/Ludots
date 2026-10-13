export type AuthoringSectionId = 'author' | 'world' | 'run';

export type AuthoringToolId =
  | 'blueprint'
  | 'bt'
  | 'fsm'
  | 'dialogue'
  | 'text'
  | 'timeline'
  | 'map'
  | 'panels'
  | 'run';

export type AuthoringSection = {
  id: AuthoringSectionId;
  title: string;
  blurb: string;
};

export type AuthoringTool = {
  id: AuthoringToolId;
  section: AuthoringSectionId;
  path: string;
  aliases: readonly string[];
  title: string;
  blurb: string;
  hint: string;
};

/**
 * LudotsEditor 正门分三区：作者（内容六间房）、世界（地图与面板）、运行（选 Mod 开局）。
 * 路径别名留给旧书签：/gas-graphs、/story-authoring、/ui-panel-authoring。
 */
export const AUTHORING_SECTIONS: readonly AuthoringSection[] = [
  {
    id: 'author',
    title: '作者',
    blurb: '蓝图、行为树、状态机、对话、文本、时间轴——写内容的地方。',
  },
  {
    id: 'world',
    title: '世界',
    blurb: '地图地形、导航烘焙、面板模板——捏世界皮肤的地方。',
  },
  {
    id: 'run',
    title: '运行',
    blurb: '选 Mod、选平台、一键开局；live debug 通道随局点亮。',
  },
];

export const AUTHORING_TOOLS: readonly AuthoringTool[] = [
  {
    id: 'blueprint',
    section: 'author',
    path: '/blueprint',
    aliases: ['/gas-graphs'],
    title: '蓝图',
    blurb: '关卡事件、查询、函数图。画节点、连线、保存。',
    hint: 'TriggerGraph · Script · Query',
  },
  {
    id: 'bt',
    section: 'author',
    path: '/bt-editor',
    aliases: [],
    title: '行为树',
    blurb: '巡逻、追击、出手。改树的结构，叶子进蓝图。',
    hint: 'AI/behavior_trees.json',
  },
  {
    id: 'fsm',
    section: 'author',
    path: '/fsm-editor',
    aliases: [],
    title: '状态机',
    blurb: '哨兵那种状态切换。双击生命周期进蓝图。',
    hint: 'AI/hfsm.json',
  },
  {
    id: 'dialogue',
    section: 'author',
    path: '/dialogue',
    aliases: ['/story-authoring'],
    title: '对话',
    blurb: '说话节点连成树。黄线是选项，蓝线接下句。条件和副作用进蓝图。',
    hint: 'Dialogue/ · Story/lines.json',
  },
  {
    id: 'text',
    section: 'author',
    path: '/text-bank',
    aliases: [],
    title: '文本',
    blurb: '一张表改所有文案。行是词条，列是语言；就地加粗、上色，缺翻译标红。',
    hint: 'Presentation/text_tokens.json · text_locales.json',
  },
  {
    id: 'timeline',
    section: 'author',
    path: '/timeline',
    aliases: [],
    title: '时间轴',
    blurb: '镜头、字幕、过场。拖轨道改时长。',
    hint: 'Sequencer/sequences.json',
  },
  {
    id: 'map',
    section: 'world',
    path: '/map',
    aliases: [],
    title: '地图',
    blurb: '六边形地形、十类笔刷、摆实体、导航烘焙。三个窗口来回切。',
    hint: 'Terrain/ · Boards/ · Nav 烘焙',
  },
  {
    id: 'panels',
    section: 'world',
    path: '/panel-authoring',
    aliases: ['/ui-panel-authoring'],
    title: '面板',
    blurb: '面板模板的 authoring-form 参考实现：布局、变量绑定、四皮预览。',
    hint: 'UI/panel_templates',
  },
  {
    id: 'run',
    section: 'run',
    path: '/run',
    aliases: [],
    title: '开局',
    blurb: '勾 Mod、选 preset 与平台，一键开局；进程与 live debug 通道状态在页。',
    hint: 'POST /api/launch · /api/launcher/state',
  },
];

export const AUTHORING_STUDIO_HOME = '/';

export const AUTHORING_TOOL_IDS: readonly AuthoringToolId[] = AUTHORING_TOOLS.map((tool) => tool.id);

export function matchAuthoringTool(pathname: string): AuthoringTool | undefined {
  return AUTHORING_TOOLS.find((tool) => tool.path === pathname || tool.aliases.includes(pathname));
}
