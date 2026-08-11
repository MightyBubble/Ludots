using System;

namespace Ludots.Core.Gameplay.AI.Fsm
{
    /// <summary>
    /// Dense SoA hierarchical FSM. Active configuration is a root→leaf stack per agent.
    /// Transition conditions and state OnEnter/OnTick/OnExit are host-bound graph ids.
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

        public int AddAgent(IHfsmGraphHost? host = null)
        {
            if (_count >= Capacity) throw new InvalidOperationException("HfsmWorld at capacity.");
            int agent = _count++;
            EnterDefaultPath(agent, _hfsm.RootIndex, host);
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

        public HfsmThinkStats TickAll(IHfsmGraphHost? host = null)
        {
            int predicates = 0;
            int taken = 0;
            int lifecycleRuns = 0;
            for (int agent = 0; agent < _count; agent++)
            {
                if (TryTransition(agent, host, ref predicates))
                {
                    taken++;
                }

                lifecycleRuns += RunTickCallbacks(agent, host);
            }

            return new HfsmThinkStats(_count, predicates, taken, lifecycleRuns);
        }

        private int RunTickCallbacks(int agent, IHfsmGraphHost? host)
        {
            int runs = 0;
            int baseIndex = agent * HfsmLimits.MaxStackDepth;
            int depth = _depth[agent];
            for (int i = 0; i < depth; i++)
            {
                int stateIndex = _stack[baseIndex + i];
                int tickGraph = _hfsm.States[stateIndex].OnTickGraphId;
                if (tickGraph <= 0)
                {
                    continue;
                }

                if (host == null)
                {
                    throw new InvalidOperationException(
                        $"HFSM state[{stateIndex}] OnTickGraphId={tickGraph} requires IHfsmGraphHost.");
                }

                host.RunAction(agent, tickGraph);
                runs++;
            }

            return runs;
        }

        private bool TryTransition(int agent, IHfsmGraphHost? host, ref int predicates)
        {
            int leaf = GetLeafState(agent);
            if (TryPickTransition(agent, leaf, host, ref predicates, out HfsmTransition chosen))
            {
                ApplyTransition(agent, chosen, host);
                return true;
            }

            int parent = _hfsm.States[leaf].ParentIndex;
            while (parent >= 0)
            {
                if (TryPickTransition(agent, parent, host, ref predicates, out chosen))
                {
                    ApplyTransition(agent, chosen, host);
                    return true;
                }

                parent = _hfsm.States[parent].ParentIndex;
            }

            return false;
        }

        private bool TryPickTransition(
            int agent,
            int fromState,
            IHfsmGraphHost? host,
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
                if (!EvalBuiltin(agent, tr.Predicate))
                {
                    continue;
                }

                if (tr.ConditionGraphId > 0)
                {
                    if (host == null)
                    {
                        throw new InvalidOperationException(
                            $"HFSM transition {tr.FromState}->{tr.ToState} requires IHfsmGraphHost for ConditionGraphId={tr.ConditionGraphId}.");
                    }

                    if (!host.EvalCondition(agent, tr.ConditionGraphId))
                    {
                        continue;
                    }
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

        private void ApplyTransition(int agent, in HfsmTransition transition, IHfsmGraphHost? host)
        {
            if (transition.Predicate == HfsmTransitionPredicate.StimulusLatched)
            {
                _stimulus[agent] = 0;
            }

            int targetLeaf = _hfsm.ResolveDefaultLeaf(transition.ToState);
            int fromLeaf = GetLeafState(agent);
            int lca = FindLca(fromLeaf, targetLeaf);
            ExitUpTo(agent, lca, host);
            EnterDownFrom(agent, lca, targetLeaf, host);
        }

        private void EnterDefaultPath(int agent, int stateIndex, IHfsmGraphHost? host)
        {
            int leaf = _hfsm.ResolveDefaultLeaf(stateIndex);
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
            _depth[agent] = 0;
            for (int i = n - 1; i >= 0; i--)
            {
                PushEnter(agent, path[i], host);
            }
        }

        private void ExitUpTo(int agent, int lca, IHfsmGraphHost? host)
        {
            int baseIndex = agent * HfsmLimits.MaxStackDepth;
            int depth = _depth[agent];
            while (depth > 0 && _stack[baseIndex + depth - 1] != lca)
            {
                int exiting = _stack[baseIndex + depth - 1];
                depth--;
                _depth[agent] = (byte)depth;
                int exitGraph = _hfsm.States[exiting].OnExitGraphId;
                if (exitGraph > 0)
                {
                    if (host == null)
                    {
                        throw new InvalidOperationException(
                            $"HFSM state[{exiting}] OnExitGraphId={exitGraph} requires IHfsmGraphHost.");
                    }

                    host.RunAction(agent, exitGraph);
                }
            }

            _depth[agent] = (byte)depth;
        }

        private void EnterDownFrom(int agent, int lca, int targetLeaf, IHfsmGraphHost? host)
        {
            Span<int> path = stackalloc int[HfsmLimits.MaxStackDepth];
            int n = 0;
            int cur = targetLeaf;
            while (cur != lca && cur >= 0)
            {
                path[n++] = cur;
                cur = _hfsm.States[cur].ParentIndex;
            }

            for (int i = n - 1; i >= 0; i--)
            {
                PushEnter(agent, path[i], host);
            }
        }

        private void PushEnter(int agent, int stateIndex, IHfsmGraphHost? host)
        {
            int baseIndex = agent * HfsmLimits.MaxStackDepth;
            int depth = _depth[agent];
            if (depth >= HfsmLimits.MaxStackDepth)
            {
                throw new InvalidOperationException("HFSM stack overflow on enter.");
            }

            _stack[baseIndex + depth] = stateIndex;
            _depth[agent] = (byte)(depth + 1);
            int enterGraph = _hfsm.States[stateIndex].OnEnterGraphId;
            if (enterGraph > 0)
            {
                if (host == null)
                {
                    throw new InvalidOperationException(
                        $"HFSM state[{stateIndex}] OnEnterGraphId={enterGraph} requires IHfsmGraphHost.");
                }

                host.RunAction(agent, enterGraph);
            }
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

        private bool EvalBuiltin(int agent, HfsmTransitionPredicate predicate)
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
        public HfsmThinkStats(int agents, int predicatesChecked, int transitionsTaken, int lifecycleRuns)
        {
            Agents = agents;
            PredicatesChecked = predicatesChecked;
            TransitionsTaken = transitionsTaken;
            LifecycleRuns = lifecycleRuns;
        }

        public int Agents { get; }
        public int PredicatesChecked { get; }
        public int TransitionsTaken { get; }
        public int LifecycleRuns { get; }
    }
}
