using System;

namespace Ludots.Core.Gameplay.AI.Fsm
{
    public sealed class FsmDefinition
    {
        public FsmDefinition(string id, int stateCount, int initialState, FsmTransition[] transitions)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("FSM id required.", nameof(id));
            if (stateCount <= 0 || stateCount > FsmLimits.MaxStates)
            {
                throw new ArgumentOutOfRangeException(nameof(stateCount));
            }

            if ((uint)initialState >= (uint)stateCount)
            {
                throw new ArgumentOutOfRangeException(nameof(initialState));
            }

            transitions ??= Array.Empty<FsmTransition>();
            if (transitions.Length > FsmLimits.MaxTransitions)
            {
                throw new ArgumentException("Too many transitions.", nameof(transitions));
            }

            for (int i = 0; i < transitions.Length; i++)
            {
                FsmTransition t = transitions[i];
                if ((uint)t.FromState >= (uint)stateCount || (uint)t.ToState >= (uint)stateCount)
                {
                    throw new InvalidOperationException($"FSM '{id}' transition[{i}] references invalid state.");
                }
            }

            Id = id;
            StateCount = stateCount;
            InitialState = initialState;
            Transitions = transitions;
        }

        public string Id { get; }
        public int StateCount { get; }
        public int InitialState { get; }
        public FsmTransition[] Transitions { get; }
    }

    public static class FsmFactory
    {
        /// <summary>Idle(0) --stimulus--> Alert(1) --Always--> Combat(2) --Always--> Retreat(3) --Always--> Idle.</summary>
        public static FsmDefinition CreateSentryLoop(string id)
        {
            var transitions = new[]
            {
                new FsmTransition(0, 1, FsmTransitionPredicate.StimulusLatched, priority: 0),
                new FsmTransition(1, 2, FsmTransitionPredicate.Always, priority: 0),
                new FsmTransition(2, 3, FsmTransitionPredicate.Always, priority: 0),
                new FsmTransition(3, 0, FsmTransitionPredicate.Always, priority: 0),
            };
            return new FsmDefinition(id, stateCount: 4, initialState: 0, transitions);
        }
    }
}
