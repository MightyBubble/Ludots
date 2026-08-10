using System;

namespace Ludots.Core.Gameplay.AI.Fsm
{
    /// <summary>
    /// Dense SoA hierarchical FSM. Active configuration is a root→leaf stack per agent.
    /// </summary>
    public sealed class HfsmWorld
    {
        private readonly HfsmDefinition _hfsm;
        private readonly int[] _stack;
        private readonly byte[] _depth;
        private readonly byte[] _stimulus;
        private int _count;

        public HfsmWorld(HfsmDefinition hfsm, int capacity)
        {
            _hfsm = hfsm ?? throw new ArgumentNullException(nameof(hfsm));
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
            _stack = new int[capacity * HfsmLimits.MaxStackDepth];
            _depth = new byte[capacity];
            _stimulus = new byte[capacity];
        }

        public HfsmDefinition Definition => _hfsm;
        public int Capacity { get; }
        public int Count => _count;

        public int AddAgent()
        {
            if (_count >= Capacity) throw new InvalidOperationException("HfsmWorld at capacity.");
            int agent = _count++;
            EnterDefaultPath(agent, _hfsm.RootIndex);
            _stimulus[agent] = 0;
            return agent;
        }

        public void LatchStimulus(int agent)
        {
            if ((uint)agent >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(agent));
            _stimulus[agent] = 1;
        }

        public int GetLeafState(int agent)
        {
            if ((uint)agent >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(agent));
            int d = _depth[agent];
            if (d <= 0) throw new InvalidOperationException("HFSM agent has empty stack.");
            return _stack[agent * HfsmLimits.MaxStackDepth + d - 1];
        }

        public HfsmThinkStats TickAll()
        {
            int predicates = 0;
            int taken = 0;
            for (int agent = 0; agent < _count; agent++)
            {
                if (TryTransition(agent, ref predicates))
                {
                    taken++;
                }
            }

            return new HfsmThinkStats(_count, predicates, taken);
        }

        private bool TryTransition(int agent, ref int predicates)
        {
            int leaf = GetLeafState(agent);
            if (!TryPickTransition(agent, leaf, ref predicates, out HfsmTransition chosen))
            {
                // Also allow transitions authored on ancestors (outer HFSM edges).
                int parent = _hfsm.States[leaf].ParentIndex;
                while (parent >= 0)
                {
                    if (TryPickTransition(agent, parent, ref predicates, out chosen))
                    {
                        ApplyTransition(agent, chosen);
                        return true;
                    }

                    parent = _hfsm.States[parent].ParentIndex;
                }

                return false;
            }

            ApplyTransition(agent, chosen);
            return true;
        }

        private bool TryPickTransition(
            int agent,
            int fromState,
            ref int predicates,
            out HfsmTransition chosen)
        {
            ReadOnlySpan<HfsmTransition> span = _hfsm.GetTransitionsFromState(fromState);
            int bestPriority = int.MinValue;
            int bestIndex = -1;
            for (int i = 0; i < span.Length; i++)
            {
                predicates++;
                HfsmTransition tr = span[i];
                if (!Eval(agent, tr.Predicate))
                {
                    continue;
                }

                if (bestIndex < 0 || tr.Priority >= bestPriority)
                {
                    bestPriority = tr.Priority;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                chosen = default;
                return false;
            }

            chosen = span[bestIndex];
            return true;
        }

        private void ApplyTransition(int agent, in HfsmTransition transition)
        {
            if (transition.Predicate == HfsmTransitionPredicate.StimulusLatched)
            {
                _stimulus[agent] = 0;
            }

            int targetLeaf = _hfsm.ResolveDefaultLeaf(transition.ToState);
            int lca = FindLca(GetLeafState(agent), targetLeaf);
            ExitUpTo(agent, lca);
            EnterDownFrom(agent, lca, targetLeaf);
        }

        private void EnterDefaultPath(int agent, int stateIndex)
        {
            int leaf = _hfsm.ResolveDefaultLeaf(stateIndex);
            // Build stack root→leaf
            Span<int> path = stackalloc int[HfsmLimits.MaxStackDepth];
            int n = 0;
            int cur = leaf;
            while (cur >= 0)
            {
                path[n++] = cur;
                cur = _hfsm.States[cur].ParentIndex;
                if (n > HfsmLimits.MaxStackDepth)
                {
                    throw new InvalidOperationException("HFSM enter path exceeds max depth.");
                }
            }

            int baseIndex = agent * HfsmLimits.MaxStackDepth;
            _depth[agent] = (byte)n;
            for (int i = 0; i < n; i++)
            {
                _stack[baseIndex + i] = path[n - 1 - i];
            }
        }

        private void ExitUpTo(int agent, int lca)
        {
            int baseIndex = agent * HfsmLimits.MaxStackDepth;
            int depth = _depth[agent];
            while (depth > 0 && _stack[baseIndex + depth - 1] != lca)
            {
                depth--;
            }

            _depth[agent] = (byte)depth;
        }

        private void EnterDownFrom(int agent, int lca, int targetLeaf)
        {
            // path from targetLeaf up to but not including lca, then push reversed
            Span<int> path = stackalloc int[HfsmLimits.MaxStackDepth];
            int n = 0;
            int cur = targetLeaf;
            while (cur != lca && cur >= 0)
            {
                path[n++] = cur;
                cur = _hfsm.States[cur].ParentIndex;
            }

            int baseIndex = agent * HfsmLimits.MaxStackDepth;
            int depth = _depth[agent];
            for (int i = n - 1; i >= 0; i--)
            {
                if (depth >= HfsmLimits.MaxStackDepth)
                {
                    throw new InvalidOperationException("HFSM stack overflow on enter.");
                }

                _stack[baseIndex + depth] = path[i];
                depth++;
            }

            _depth[agent] = (byte)depth;
        }

        private int FindLca(int a, int b)
        {
            Span<byte> seen = stackalloc byte[HfsmLimits.MaxStates];
            seen.Clear();
            int cur = a;
            while (cur >= 0)
            {
                seen[cur] = 1;
                cur = _hfsm.States[cur].ParentIndex;
            }

            cur = b;
            while (cur >= 0)
            {
                if (seen[cur] != 0)
                {
                    return cur;
                }

                cur = _hfsm.States[cur].ParentIndex;
            }

            return _hfsm.RootIndex;
        }

        private bool Eval(int agent, HfsmTransitionPredicate predicate)
            => predicate switch
            {
                HfsmTransitionPredicate.Never => false,
                HfsmTransitionPredicate.Always => true,
                HfsmTransitionPredicate.StimulusLatched => _stimulus[agent] != 0,
                _ => throw new InvalidOperationException($"Unknown HFSM predicate {predicate}.")
            };
    }

    public readonly struct HfsmThinkStats
    {
        public HfsmThinkStats(int agents, int predicatesChecked, int transitionsTaken)
        {
            Agents = agents;
            PredicatesChecked = predicatesChecked;
            TransitionsTaken = transitionsTaken;
        }

        public int Agents { get; }
        public int PredicatesChecked { get; }
        public int TransitionsTaken { get; }
    }
}
