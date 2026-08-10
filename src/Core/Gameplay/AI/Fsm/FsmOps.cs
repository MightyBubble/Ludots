namespace Ludots.Core.Gameplay.AI.Fsm
{
    public enum FsmTransitionPredicate : byte
    {
        Never = 0,
        Always = 1,
        /// <summary>Host sets a per-agent stimulus bit; consumed on successful transition.</summary>
        StimulusLatched = 2
    }

    public readonly struct FsmTransition
    {
        public FsmTransition(int fromState, int toState, FsmTransitionPredicate predicate, int priority)
        {
            FromState = fromState;
            ToState = toState;
            Predicate = predicate;
            Priority = priority;
        }

        public int FromState { get; }
        public int ToState { get; }
        public FsmTransitionPredicate Predicate { get; }
        public int Priority { get; }
    }

    public static class FsmLimits
    {
        public const int MaxStates = 16;
        public const int MaxTransitions = 64;
        public const int DefaultThinkPeriodTicks = 12; // 0.2s @ 60Hz
    }
}
