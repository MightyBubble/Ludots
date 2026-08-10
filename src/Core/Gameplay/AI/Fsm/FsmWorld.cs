using System;

namespace Ludots.Core.Gameplay.AI.Fsm
{
    /// <summary>Dense SoA FSM agents sharing one definition. No graph-layer stagger.</summary>
    public sealed class FsmWorld
    {
        private readonly FsmDefinition _fsm;
        private readonly int[] _state;
        private readonly byte[] _stimulus;
        private readonly int[] _fromIndex;
        private readonly int[] _fromCount;
        private int _count;

        public FsmWorld(FsmDefinition fsm, int capacity)
        {
            _fsm = fsm ?? throw new ArgumentNullException(nameof(fsm));
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
            _state = new int[capacity];
            _stimulus = new byte[capacity];
            _fromIndex = new int[fsm.StateCount];
            _fromCount = new int[fsm.StateCount];
            BuildTransitionIndex();
        }

        public FsmDefinition Definition => _fsm;
        public int Capacity { get; }
        public int Count => _count;
        public int[] States => _state;

        public int AddAgent()
        {
            if (_count >= Capacity) throw new InvalidOperationException("FsmWorld at capacity.");
            int i = _count++;
            _state[i] = _fsm.InitialState;
            _stimulus[i] = 0;
            return i;
        }

        public void LatchStimulus(int agent)
        {
            if ((uint)agent >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(agent));
            _stimulus[agent] = 1;
        }

        public FsmThinkStats TickAll()
        {
            int transitionsTaken = 0;
            int predicatesChecked = 0;
            for (int agent = 0; agent < _count; agent++)
            {
                int state = _state[agent];
                int start = _fromIndex[state];
                int count = _fromCount[state];
                int bestPriority = int.MinValue;
                int bestTo = -1;
                FsmTransitionPredicate bestPred = FsmTransitionPredicate.Never;
                for (int t = 0; t < count; t++)
                {
                    FsmTransition tr = _fsm.Transitions[start + t];
                    predicatesChecked++;
                    if (!Eval(agent, tr.Predicate))
                    {
                        continue;
                    }

                    if (bestTo < 0 || tr.Priority >= bestPriority)
                    {
                        bestPriority = tr.Priority;
                        bestTo = tr.ToState;
                        bestPred = tr.Predicate;
                    }
                }

                if (bestTo >= 0)
                {
                    if (bestPred == FsmTransitionPredicate.StimulusLatched)
                    {
                        _stimulus[agent] = 0;
                    }

                    _state[agent] = bestTo;
                    transitionsTaken++;
                }
            }

            return new FsmThinkStats(_count, predicatesChecked, transitionsTaken);
        }

        private bool Eval(int agent, FsmTransitionPredicate predicate)
            => predicate switch
            {
                FsmTransitionPredicate.Never => false,
                FsmTransitionPredicate.Always => true,
                FsmTransitionPredicate.StimulusLatched => _stimulus[agent] != 0,
                _ => throw new InvalidOperationException($"Unknown predicate {predicate}.")
            };

        private void BuildTransitionIndex()
        {
            Array.Sort(_fsm.Transitions, (a, b) =>
            {
                int c = a.FromState.CompareTo(b.FromState);
                return c != 0 ? c : b.Priority.CompareTo(a.Priority);
            });

            Array.Fill(_fromIndex, 0);
            Array.Fill(_fromCount, 0);
            for (int i = 0; i < _fsm.Transitions.Length; i++)
            {
                int from = _fsm.Transitions[i].FromState;
                if (_fromCount[from] == 0)
                {
                    _fromIndex[from] = i;
                }

                _fromCount[from]++;
            }
        }
    }

    public readonly struct FsmThinkStats
    {
        public FsmThinkStats(int agents, int predicatesChecked, int transitionsTaken)
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
