export type GraphEditorDialect = 'func' | 'bt' | 'fsm';

export const GRAPH_KIND_LABELS: Record<string, string> = {
  TriggerGraph: '事件',
  Effect: '效果',
  Query: '查询',
  Score: '评分',
  Validation: '校验',
  Derived: '派生',
  Script: '脚本',
};

export const graphKindLabel = (kind: string): string => GRAPH_KIND_LABELS[kind] ?? kind;

const BT_SUGARS = new Set([
  'BtSequence',
  'BtSelector',
  'BtDecorator',
  'BtLeaf',
  'BtAction',
  'BtCondition',
]);

const FSM_SUGARS = new Set([
  'FsmState',
  'FsmAction',
]);

const BT_FSM_SUGARS = new Set([...BT_SUGARS, ...FSM_SUGARS]);

export function dialectTitle(dialect: GraphEditorDialect): { title: string; subtitle: string } {
  switch (dialect) {
    case 'bt':
      return {
        title: '行为树图',
        subtitle: '拓扑叶子指向的函数图视图，双击拓扑叶子进来。',
      };
    case 'fsm':
      return {
        title: '状态机图',
        subtitle: '状态动作指向的函数图视图，双击拓扑叶子进来。',
      };
    default:
      return {
        title: '蓝图',
        subtitle: '画函数 / 事件 / 效果 / 查询图，保存进 GAS/graphs.json。',
      };
  }
}

export function dialectPath(dialect: GraphEditorDialect): string {
  switch (dialect) {
    case 'bt':
      return '/bt-editor';
    case 'fsm':
      return '/fsm-editor';
    default:
      return '/gas-graphs';
  }
}

/** Palette filter: which ops/sugars appear in this editor. */
export function isOpAllowedInDialect(op: string, dialect: GraphEditorDialect): boolean {
  if (dialect === 'bt') return BT_SUGARS.has(op);
  if (dialect === 'fsm') return FSM_SUGARS.has(op);
  // Func graph editor: hide BT/FSM composition — those belong in their own editors.
  return !BT_FSM_SUGARS.has(op);
}

export function isFunctionGraphPortalOp(op: string): boolean {
  return op === 'BtLeaf'
    || op === 'BtAction'
    || op === 'BtCondition'
    || op === 'FsmAction'
    || op === 'InlineGraph'
    || op === 'InvokeScript'
    || op === 'InvokeGraph';
}

/**
 * Catalog heuristics for leftover dialect filters on GasGraphEditorPage (func editor).
 * BT/FSM author shells live in AI/*.json now — /bt-editor and /fsm-editor no longer use this.
 * Leaves / state bodies stay as Func Graphs (Graph.BT.Leaf.* / Graph.HFSM.*).
 */
export function catalogGraphMatchesDialect(graphId: string, kind: string, dialect: GraphEditorDialect): boolean {
  const id = graphId;
  if (dialect === 'bt' || dialect === 'fsm') {
    // Dialect routes retired for Script shells; keep heuristic for fail-closed redirects only.
    return false;
  }
  // Func editor: exclude any leftover outer BT/FSM Script shell ids if they reappear.
  if (/(^|\.)BT\.(Tree|Root)(\.|$)/i.test(id) || /Graph\.BT\.Tree\./i.test(id)) return false;
  if (/^Graph\.FSM\.[^.]+$/i.test(id)) return false;
  return true;
}

export function preferredDialectForGraphId(graphId: string): GraphEditorDialect {
  if (catalogGraphMatchesDialect(graphId, '', 'bt')) return 'bt';
  if (catalogGraphMatchesDialect(graphId, '', 'fsm')) return 'fsm';
  return 'func';
}
