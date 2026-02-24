using Ludots.Core.Gameplay.AI.WorldState;

namespace Ludots.Core.Gameplay.AI.Planning
{
    public readonly struct GoapGoalPreset256
    {
        public readonly int GoalPresetId;
        public readonly WorldStateCondition256 Goal;
        public readonly int HeuristicWeight;

        public GoapGoalPreset256(int goalPresetId, in WorldStateCondition256 goal, int heuristicWeight = 1)
        {
            GoalPresetId = goalPresetId;
            Goal = goal;
            HeuristicWeight = heuristicWeight;
        }
    }
}

