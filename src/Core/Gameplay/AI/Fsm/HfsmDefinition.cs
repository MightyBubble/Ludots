using System;

namespace Ludots.Core.Gameplay.AI.Fsm
{
    public sealed class HfsmDefinition
    {
        private readonly int[] _transitionStarts;
        private readonly int[] _transitionCounts;
        private readonly HfsmTransition[] _transitionsByFrom;

        public HfsmDefinition(string id, HfsmState[] states, int rootIndex, HfsmTransition[] transitions)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("HFSM id required.", nameof(id));
            if (states == null || states.Length == 0) throw new ArgumentException("HFSM requires states.", nameof(states));
            if (states.Length > HfsmLimits.MaxStates) throw new ArgumentException("Too many HFSM states.", nameof(states));
            if ((uint)rootIndex >= (uint)states.Length) throw new ArgumentOutOfRangeException(nameof(rootIndex));
            transitions ??= Array.Empty<HfsmTransition>();
            if (transitions.Length > HfsmLimits.MaxTransitions)
            {
                throw new ArgumentException("Too many HFSM transitions.", nameof(transitions));
            }

            Id = id;
            States = states;
            RootIndex = rootIndex;
            ValidateHierarchy(id, states, rootIndex);
            ValidateTransitions(id, states.Length, transitions);

            // Animator-like: build per-from-state transition spans (Presentation-independent copy of the indexing idea).
            _transitionsByFrom = (HfsmTransition[])transitions.Clone();
            Array.Sort(_transitionsByFrom, (a, b) =>
            {
                int c = a.FromState.CompareTo(b.FromState);
                return c != 0 ? c : b.Priority.CompareTo(a.Priority);
            });
            _transitionStarts = new int[states.Length];
            _transitionCounts = new int[states.Length];
            Array.Fill(_transitionStarts, 0);
            Array.Fill(_transitionCounts, 0);
            for (int i = 0; i < _transitionsByFrom.Length; i++)
            {
                int from = _transitionsByFrom[i].FromState;
                if (_transitionCounts[from] == 0)
                {
                    _transitionStarts[from] = i;
                }

                _transitionCounts[from]++;
            }
        }

        public string Id { get; }
        public HfsmState[] States { get; }
        public int RootIndex { get; }
        public int StateCount => States.Length;

        public ReadOnlySpan<HfsmTransition> GetTransitionsFromState(int stateIndex)
        {
            if ((uint)stateIndex >= (uint)_transitionCounts.Length)
            {
                return ReadOnlySpan<HfsmTransition>.Empty;
            }

            int count = _transitionCounts[stateIndex];
            return count == 0
                ? ReadOnlySpan<HfsmTransition>.Empty
                : new ReadOnlySpan<HfsmTransition>(_transitionsByFrom, _transitionStarts[stateIndex], count);
        }

        public int ResolveDefaultLeaf(int stateIndex)
        {
            int guard = 0;
            int current = stateIndex;
            while (States[current].Kind == HfsmStateKind.Compound)
            {
                if (++guard > HfsmLimits.MaxStackDepth)
                {
                    throw new InvalidOperationException($"HFSM '{Id}' default-child drill exceeded max depth.");
                }

                current = States[current].DefaultChildIndex;
            }

            return current;
        }

        private static void ValidateHierarchy(string id, HfsmState[] states, int rootIndex)
        {
            if (states[rootIndex].ParentIndex != -1)
            {
                throw new InvalidOperationException($"HFSM '{id}' root must have ParentIndex=-1.");
            }

            for (int i = 0; i < states.Length; i++)
            {
                HfsmState s = states[i];
                if (s.Kind == HfsmStateKind.Compound)
                {
                    if (s.ChildCount <= 0)
                    {
                        throw new InvalidOperationException($"HFSM '{id}' compound state[{i}] needs children.");
                    }

                    if ((uint)s.DefaultChildIndex < (uint)s.ChildStart ||
                        s.DefaultChildIndex >= s.ChildStart + s.ChildCount)
                    {
                        throw new InvalidOperationException($"HFSM '{id}' compound state[{i}] defaultChild out of range.");
                    }

                    for (int c = 0; c < s.ChildCount; c++)
                    {
                        int child = s.ChildStart + c;
                        if ((uint)child >= (uint)states.Length)
                        {
                            throw new InvalidOperationException($"HFSM '{id}' compound state[{i}] child out of range.");
                        }

                        if (states[child].ParentIndex != i)
                        {
                            throw new InvalidOperationException($"HFSM '{id}' state[{child}] parent mismatch.");
                        }
                    }
                }
                else if (s.ChildCount != 0)
                {
                    throw new InvalidOperationException($"HFSM '{id}' leaf state[{i}] cannot have children.");
                }
            }
        }

        private static void ValidateTransitions(string id, int stateCount, HfsmTransition[] transitions)
        {
            for (int i = 0; i < transitions.Length; i++)
            {
                HfsmTransition t = transitions[i];
                if ((uint)t.FromState >= (uint)stateCount || (uint)t.ToState >= (uint)stateCount)
                {
                    throw new InvalidOperationException($"HFSM '{id}' transition[{i}] references invalid state.");
                }
            }
        }
    }

    public static class HfsmFactory
    {
        /// <summary>
        /// Root(Compound) → Idle | Alerting(Compound → Alert/Combat/Retreat).
        /// Idle --stimulus--> Alert; Alert→Combat→Retreat→Idle.
        /// </summary>
        public static HfsmDefinition CreateSentryHierarchy(string id)
        {
            // 0 Root compound children 1..2 default Idle
            // 1 Idle leaf
            // 2 Alerting compound children 3..5 default Alert
            // 3 Alert leaf
            // 4 Combat leaf
            // 5 Retreat leaf
            var states = new[]
            {
                new HfsmState(HfsmStateKind.Compound, parentIndex: -1, childStart: 1, childCount: 2, defaultChildIndex: 1),
                new HfsmState(HfsmStateKind.Leaf, parentIndex: 0, childStart: 0, childCount: 0, defaultChildIndex: 0),
                new HfsmState(HfsmStateKind.Compound, parentIndex: 0, childStart: 3, childCount: 3, defaultChildIndex: 3),
                new HfsmState(HfsmStateKind.Leaf, parentIndex: 2, childStart: 0, childCount: 0, defaultChildIndex: 0),
                new HfsmState(HfsmStateKind.Leaf, parentIndex: 2, childStart: 0, childCount: 0, defaultChildIndex: 0),
                new HfsmState(HfsmStateKind.Leaf, parentIndex: 2, childStart: 0, childCount: 0, defaultChildIndex: 0),
            };
            var transitions = new[]
            {
                new HfsmTransition(fromState: 1, toState: 3, HfsmTransitionPredicate.StimulusLatched, priority: 0),
                new HfsmTransition(fromState: 3, toState: 4, HfsmTransitionPredicate.Always, priority: 0),
                new HfsmTransition(fromState: 4, toState: 5, HfsmTransitionPredicate.Always, priority: 0),
                new HfsmTransition(fromState: 5, toState: 1, HfsmTransitionPredicate.Always, priority: 0),
            };
            return new HfsmDefinition(id, states, rootIndex: 0, transitions);
        }
    }
}
