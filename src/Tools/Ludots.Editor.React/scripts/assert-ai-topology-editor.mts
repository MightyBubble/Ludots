import {
  hfsmTransitionFromEdge,
  hfsmTransitionToEdgeData,
  type HfsmTransition,
} from '../src/pages/ai-topology-editor/hfsmTransitions.ts';

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message);
}

const transitions: HfsmTransition[] = [
  { from: 'idle', to: 'combat', predicate: 'Always', priority: 1 },
  { from: 'idle', to: 'retreat', predicate: 'Always', condition: 'hfsm.shouldRetreat', priority: 10 },
];

const roundTripped = transitions.map((transition) =>
  hfsmTransitionFromEdge(
    transition.from,
    transition.to,
    hfsmTransitionToEdgeData(transition),
  ));

assert(roundTripped[0]?.priority === 1, 'HFSM transition priority 1 must survive editor round-trip');
assert(roundTripped[1]?.priority === 10, 'HFSM transition priority 10 must survive editor round-trip');
assert(roundTripped[1]?.condition === 'hfsm.shouldRetreat', 'HFSM transition condition must survive editor round-trip');

console.log('assert-ai-topology-editor: ok');
