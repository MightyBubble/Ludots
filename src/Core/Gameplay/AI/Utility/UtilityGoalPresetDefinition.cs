namespace Ludots.Core.Gameplay.AI.Utility
{
    public readonly struct UtilityGoalPresetDefinition
    {
        public readonly int GoalPresetId;
        public readonly int PlanningStrategyId;
        public readonly float Weight;
        public readonly UtilityConsiderationBool256[] Considerations;

        public UtilityGoalPresetDefinition(int goalPresetId, int planningStrategyId, float weight, UtilityConsiderationBool256[] considerations)
        {
            GoalPresetId = goalPresetId;
            PlanningStrategyId = planningStrategyId;
            Weight = weight;
            Considerations = considerations ?? System.Array.Empty<UtilityConsiderationBool256>();
        }
    }
}

