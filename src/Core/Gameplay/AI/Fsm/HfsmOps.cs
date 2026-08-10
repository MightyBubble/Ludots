namespace Ludots.Core.Gameplay.AI.Fsm
{
    public enum HfsmStateKind : byte
    {
        Leaf = 0,
        Compound = 1
    }

    public enum HfsmTransitionPredicate : byte
    {
        Never = 0,
        Always = 1,
        StimulusLatched = 2
    }

    /// <summary>
    /// Flat state table entry. Compound states own a contiguous child range and a default child.
    /// Pattern note: similar to AnimatorController's flat state list + indexed transitions — hierarchy is explicit parent/child links.
    /// </summary>
    public readonly struct HfsmState
    {
        public HfsmState(
            HfsmStateKind kind,
            int parentIndex,
            int childStart,
            int childCount,
            int defaultChildIndex)
        {
            Kind = kind;
            ParentIndex = parentIndex;
            ChildStart = childStart;
            ChildCount = childCount;
            DefaultChildIndex = defaultChildIndex;
        }

        public HfsmStateKind Kind { get; }
        public int ParentIndex { get; }
        public int ChildStart { get; }
        public int ChildCount { get; }
        public int DefaultChildIndex { get; }
    }

    public readonly struct HfsmTransition
    {
        public HfsmTransition(int fromState, int toState, HfsmTransitionPredicate predicate, int priority)
        {
            FromState = fromState;
            ToState = toState;
            Predicate = predicate;
            Priority = priority;
        }

        public int FromState { get; }
        public int ToState { get; }
        public HfsmTransitionPredicate Predicate { get; }
        public int Priority { get; }
    }

    public static class HfsmLimits
    {
        public const int MaxStates = 64;
        public const int MaxTransitions = 128;
        public const int MaxStackDepth = 8;
        public const int DefaultThinkPeriodTicks = 12;
    }
}
