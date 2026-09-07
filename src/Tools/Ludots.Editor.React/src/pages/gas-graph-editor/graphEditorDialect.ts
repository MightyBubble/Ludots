export type GraphEditorDialect = 'func' | 'bt' | 'fsm';

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
        title: 'Ludots Behavior Tree Editor',
        subtitle: 'Tree topology · Action/Condition → Func Graph · double-click to open',
      };
    case 'fsm':
      return {
        title: 'Ludots FSM Editor',
        subtitle: 'State topology · FsmAction → Func Graph · double-click to open',
      };
    default:
      return {
        title: 'Ludots Graph Editor',
        subtitle: 'Func / Event / Effect / Query · compiler diagnostics · live execution',
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
