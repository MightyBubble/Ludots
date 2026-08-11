namespace Ludots.Core.Gameplay.AI.Fsm
{
    public enum HfsmStateKind : byte
    {
        Leaf = 0,
        Compound = 1
    }

    /// <summary>
    /// Fast builtin gate. When <see cref="HfsmTransition.ConditionGraphId"/> &gt; 0, the Script/Validation
    /// graph is the SSOT condition; builtins still apply as a prefilter (e.g. stimulus bit).
    /// </summary>
    public enum HfsmTransitionPredicate : byte
    {
        Never = 0,
        Always = 1,
        StimulusLatched = 2
    }

    public readonly struct HfsmState
    {
        public HfsmState(
            HfsmStateKind kind,
            int parentIndex,
            int childStart,
            int childCount,
            int defaultChildIndex,
            int onEnterGraphId = 0,
            int onTickGraphId = 0,
            int onExitGraphId = 0)
        {
            Kind = kind;
            ParentIndex = parentIndex;
            ChildStart = childStart;
            ChildCount = childCount;
            DefaultChildIndex = defaultChildIndex;
            OnEnterGraphId = onEnterGraphId;
            OnTickGraphId = onTickGraphId;
            OnExitGraphId = onExitGraphId;
        }

        public HfsmStateKind Kind { get; }
        public int ParentIndex { get; }
        public int ChildStart { get; }
        public int ChildCount { get; }
        public int DefaultChildIndex { get; }
        /// <summary>Script graph id run on enter (0 = none). Configured on the state node.</summary>
        public int OnEnterGraphId { get; }
        /// <summary>Script graph id run each think wave while active (0 = none).</summary>
        public int OnTickGraphId { get; }
        /// <summary>Script graph id run on exit (0 = none).</summary>
        public int OnExitGraphId { get; }
    }

    public readonly struct HfsmTransition
    {
        public HfsmTransition(
            int fromState,
            int toState,
            HfsmTransitionPredicate predicate,
            int priority,
            int conditionGraphId = 0)
        {
            FromState = fromState;
            ToState = toState;
            Predicate = predicate;
            Priority = priority;
            ConditionGraphId = conditionGraphId;
        }

        public int FromState { get; }
        public int ToState { get; }
        public HfsmTransitionPredicate Predicate { get; }
        public int Priority { get; }
        /// <summary>Condition Script/Validation graph id on the transition (0 = builtin predicate only).</summary>
        public int ConditionGraphId { get; }
    }

    public static class HfsmLimits
    {
        public const int MaxStates = 64;
        public const int MaxTransitions = 128;
        public const int MaxStackDepth = 8;
        public const int DefaultThinkPeriodTicks = 12;
        public const int DefaultConditionBudgetSteps = 32;
        public const int DefaultActionBudgetSteps = 32;
    }

    /// <summary>
    /// Host evaluates transition condition graphs and state lifecycle Scripts.
    /// Keeps HFSM free of Presentation and free of a second VM.
    /// </summary>
    public interface IHfsmGraphHost
    {
        /// <summary>Run condition graph to halt; true means transition may fire.</summary>
        bool EvalCondition(int agentIndex, int conditionGraphId);

        /// <summary>Run action/lifecycle Script to halt (Yield not allowed on these bindings for now).</summary>
        void RunAction(int agentIndex, int actionGraphId);
    }
}
