export type AuthoringToolId = 'blueprint' | 'bt' | 'fsm' | 'dialogue' | 'timeline';

export type AuthoringTool = {
  id: AuthoringToolId;
  path: string;
  aliases: readonly string[];
  title: string;
  blurb: string;
  hint: string;
};

/**
 * 作者工作室正门只列这五件。地图、面板、场编辑、技能数值不进这张表。
 * 路径别名留给旧书签：/gas-graphs、/story-authoring。
 */
export const AUTHORING_TOOLS: readonly AuthoringTool[] = [
  {
    id: 'blueprint',
    path: '/blueprint',
    aliases: ['/gas-graphs'],
    title: '蓝图',
    blurb: '关卡事件、查询、函数图。画节点、连线、保存。',
    hint: 'TriggerGraph · Script · Query',
  },
  {
    id: 'bt',
    path: '/bt-editor',
    aliases: [],
    title: '行为树',
    blurb: '巡逻、追击、出手。改树的结构，叶子进蓝图。',
    hint: 'AI/behavior_trees.json',
  },
  {
    id: 'fsm',
    path: '/fsm-editor',
    aliases: [],
    title: '状态机',
    blurb: '哨兵那种状态切换。双击生命周期进蓝图。',
    hint: 'AI/hfsm.json',
  },
  {
    id: 'dialogue',
    path: '/dialogue',
    aliases: ['/story-authoring'],
    title: '对话',
    blurb: '说话节点连成树。黄线是选项，蓝线接下句。条件和副作用进蓝图。',
    hint: 'Dialogue/ · Story/lines.json',
  },
  {
    id: 'timeline',
    path: '/timeline',
    aliases: [],
    title: '时间轴',
    blurb: '镜头、字幕、过场。拖轨道改时长。',
    hint: 'Sequencer/sequences.json',
  },
];

export const AUTHORING_STUDIO_HOME = '/';

export const AUTHORING_TOOL_IDS: readonly AuthoringToolId[] = AUTHORING_TOOLS.map((tool) => tool.id);

export function matchAuthoringTool(pathname: string): AuthoringTool | undefined {
  return AUTHORING_TOOLS.find((tool) => tool.path === pathname || tool.aliases.includes(pathname));
}
