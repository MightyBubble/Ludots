export type HfsmTransition = {
  from: string;
  to: string;
  predicate: string;
  condition?: string;
  priority?: number;
};

export type HfsmTransitionEdgeData = {
  kind: 'transition';
  predicate?: string;
  condition?: string;
  priority?: number;
};

export function hfsmTransitionToEdgeData(transition: HfsmTransition): HfsmTransitionEdgeData {
  return {
    kind: 'transition',
    predicate: transition.predicate,
    condition: transition.condition,
    priority: transition.priority,
  };
}

export function hfsmTransitionFromEdge(
  source: string,
  target: string,
  data: HfsmTransitionEdgeData,
): HfsmTransition {
  return {
    from: source,
    to: target,
    predicate: data.predicate || 'Always',
    condition: data.condition || undefined,
    priority: data.priority,
  };
}
