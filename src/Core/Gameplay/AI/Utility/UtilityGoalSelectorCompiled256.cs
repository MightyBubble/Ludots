using System;
using System.Runtime.CompilerServices;
using Ludots.Core.Gameplay.AI.WorldState;

namespace Ludots.Core.Gameplay.AI.Utility
{
    public sealed class UtilityGoalSelectorCompiled256
    {
        public readonly int[] GoalPresetId;
        public readonly int[] PlanningStrategyId;
        public readonly float[] Weight;
        public readonly UtilityConsiderationBool256[] Considerations;
        public readonly (int Offset, int Count)[] ConsiderationRanges;

        public readonly bool EnableCompensationFactor;
        public readonly float CompensationFactor;
        public readonly bool EnableMomentumBonus;
        public readonly float MomentumBonus;

        private UtilityGoalSelectorCompiled256(
            int[] goalPresetId,
            int[] planningStrategyId,
            float[] weight,
            UtilityConsiderationBool256[] considerations,
            (int Offset, int Count)[] ranges,
            bool enableCompensationFactor,
            float compensationFactor,
            bool enableMomentumBonus,
            float momentumBonus)
        {
            GoalPresetId = goalPresetId;
            PlanningStrategyId = planningStrategyId;
            Weight = weight;
            Considerations = considerations;
            ConsiderationRanges = ranges;
            EnableCompensationFactor = enableCompensationFactor;
            CompensationFactor = compensationFactor;
            EnableMomentumBonus = enableMomentumBonus;
            MomentumBonus = momentumBonus;
        }

        public int Count => GoalPresetId.Length;

        public static UtilityGoalSelectorCompiled256 Compile(
            UtilityGoalPresetDefinition[] goals,
            bool enableCompensationFactor = false,
            float compensationFactor = 0.0f,
            bool enableMomentumBonus = false,
            float momentumBonus = 1.0f)
        {
            goals ??= Array.Empty<UtilityGoalPresetDefinition>();
            int count = goals.Length;
            var goalPresetId = new int[count];
            var planningStrategyId = new int[count];
            var weight = new float[count];
            var ranges = new (int Offset, int Count)[count];

            int total = 0;
            for (int i = 0; i < count; i++) total += goals[i].Considerations.Length;

            var considerations = new UtilityConsiderationBool256[total];
            int cursor = 0;
            for (int i = 0; i < count; i++)
            {
                ref readonly var g = ref goals[i];
                goalPresetId[i] = g.GoalPresetId;
                planningStrategyId[i] = g.PlanningStrategyId;
                weight[i] = g.Weight;

                int n = g.Considerations.Length;
                ranges[i] = (cursor, n);
                if (n > 0)
                {
                    Array.Copy(g.Considerations, 0, considerations, cursor, n);
                    cursor += n;
                }
            }

            return new UtilityGoalSelectorCompiled256(
                goalPresetId,
                planningStrategyId,
                weight,
                considerations,
                ranges,
                enableCompensationFactor,
                compensationFactor,
                enableMomentumBonus,
                momentumBonus);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Evaluate(in WorldStateBits256 worldState, int lastGoalPresetId, out int bestGoalPresetId, out int bestPlanningStrategyId, out float bestScore)
        {
            bestGoalPresetId = 0;
            bestPlanningStrategyId = 0;
            bestScore = 0;

            for (int i = 0; i < GoalPresetId.Length; i++)
            {
                float s = Weight[i];
                var (offset, cnt) = ConsiderationRanges[i];
                for (int c = 0; c < cnt; c++)
                {
                    ref readonly var cons = ref Considerations[offset + c];
                    float v = worldState.GetBit(cons.AtomId) ? cons.TrueScore : cons.FalseScore;
                    if (EnableCompensationFactor)
                    {
                        v = AddCompensationFactor(v, CompensationFactor);
                    }
                    s *= v;
                    if (s <= 0) break;
                }

                if (EnableMomentumBonus && GoalPresetId[i] == lastGoalPresetId)
                {
                    s *= MomentumBonus;
                }

                if (s > bestScore)
                {
                    bestScore = s;
                    bestGoalPresetId = GoalPresetId[i];
                    bestPlanningStrategyId = PlanningStrategyId[i];
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float AddCompensationFactor(float score, float compensationFactor)
        {
            return score + score * ((1f - score) * compensationFactor);
        }
    }
}

