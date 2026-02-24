using System;
using Ludots.Core.Gameplay.AI.WorldState;

namespace Ludots.Core.Gameplay.AI.Planning
{
    public sealed class GoapGoalTable256
    {
        private readonly GoapGoalPreset256[] _presets;

        public GoapGoalTable256(GoapGoalPreset256[] presets)
        {
            _presets = presets ?? Array.Empty<GoapGoalPreset256>();
        }

        public int Count => _presets.Length;

        public bool TryGetGoal(int goalPresetId, out WorldStateCondition256 goal, out int heuristicWeight)
        {
            for (int i = 0; i < _presets.Length; i++)
            {
                if (_presets[i].GoalPresetId != goalPresetId) continue;
                goal = _presets[i].Goal;
                heuristicWeight = _presets[i].HeuristicWeight;
                return true;
            }
            goal = default;
            heuristicWeight = 1;
            return false;
        }
    }
}
