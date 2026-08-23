using Ludots.Core.Gameplay.AI.Planning;
using Ludots.Core.Gameplay.AI.Utility;
using Ludots.Core.Gameplay.AI.WorldState;

namespace Ludots.Core.Gameplay.AI.Config
{
    public readonly struct AiCompiledRuntime
    {
        public readonly AtomRegistry Atoms;
        public readonly WorldStateProjectionTable ProjectionTable;
        public readonly UtilityGoalSelectorCompiled256 GoalSelector;
        public readonly ActionLibraryCompiled256 ActionLibrary;
        public readonly GoapGoalTable256 GoapGoals;
        public readonly HtnDomainCompiled256 HtnDomain;
        public readonly HtnRootTable HtnRoots;
        public readonly UtilityAiCompiledRuntime UtilityRuntime;
        public readonly GraphBehaviorCatalog Behavior;

        public AiCompiledRuntime(
            AtomRegistry atoms,
            WorldStateProjectionTable projectionTable,
            UtilityGoalSelectorCompiled256 goalSelector,
            ActionLibraryCompiled256 actionLibrary,
            GoapGoalTable256 goapGoals,
            HtnDomainCompiled256 htnDomain,
            HtnRootTable htnRoots,
            UtilityAiCompiledRuntime utilityRuntime,
            GraphBehaviorCatalog? behavior = null)
        {
            Atoms = atoms;
            ProjectionTable = projectionTable;
            GoalSelector = goalSelector;
            ActionLibrary = actionLibrary;
            GoapGoals = goapGoals;
            HtnDomain = htnDomain;
            HtnRoots = htnRoots;
            UtilityRuntime = utilityRuntime;
            Behavior = behavior ?? new GraphBehaviorCatalog();
        }
    }
}

