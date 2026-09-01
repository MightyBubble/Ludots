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
 * Catalog heuristics for editor dialects.
 * Leaves / state bodies live as Func Graphs (Graph.Func.* or Graph.BT.Leaf.*) — Func editor only.
 * BT shells: Graph.BT.Tree.* / Graph.BT.Root.*
 * FSM shells: Graph.FSM.<Name> (single segment after FSM — not Graph.FSM.X.Body).
 * BT/FSM are editor dialects + hosts, not GraphKind values.
 */
export function catalogGraphMatchesDialect(graphId: string, kind: string, dialect: GraphEditorDialect): boolean {
  const id = graphId;
  if (dialect === 'bt') {
    return /(^|\.)BT\.(Tree|Root)(\.|$)/i.test(id)
      || /Graph\.BT\.Tree\./i.test(id)
      || /BehaviorTree/i.test(id);
  }
  if (dialect === 'fsm') {
    return /^Graph\.FSM\.[^.]+$/i.test(id)
      || /^Graph\.HFSM\.[^.]+$/i.test(id)
      || /(^|\.)FSM$/i.test(id);
  }
  // Func editor: exclude outer BT trees and FSM shells; keep leaves / bodies.
  if (/(^|\.)BT\.(Tree|Root)(\.|$)/i.test(id) || /Graph\.BT\.Tree\./i.test(id)) return false;
  if (/^Graph\.FSM\.[^.]+$/i.test(id) || /^Graph\.HFSM\.[^.]+$/i.test(id)) return false;
  return true;
}

export function preferredDialectForGraphId(graphId: string): GraphEditorDialect {
  if (catalogGraphMatchesDialect(graphId, '', 'bt')) return 'bt';
  if (catalogGraphMatchesDialect(graphId, '', 'fsm')) return 'fsm';
  return 'func';
}
