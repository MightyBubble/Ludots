using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.AI.Planning;

namespace Ludots.Core.Gameplay.AI.Systems
{
    public sealed class GoapPlanningSystem : BaseSystem<World, float>
    {
        private readonly GoapAStarPlanner256 _planner;
        private readonly ActionLibraryCompiled256 _library;
        private readonly GoapGoalTable256 _goals;

        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<AIAgent, AIWorldState256, AIGoalSelection, AIPlanningState, AIPlan32>();

        public GoapPlanningSystem(World world, GoapAStarPlanner256 planner, ActionLibraryCompiled256 library, GoapGoalTable256 goals)
            : base(world)
        {
            _planner = planner;
            _library = library;
            _goals = goals;
        }

        public override void Update(in float dt)
        {
            var job = new PlanJob(_planner, _library, _goals);
            World.InlineQuery<PlanJob, AIAgent, AIWorldState256, AIGoalSelection, AIPlanningState, AIPlan32>(in _query, ref job);
        }

        private struct PlanJob : IForEach<AIAgent, AIWorldState256, AIGoalSelection, AIPlanningState, AIPlan32>
        {
            private readonly GoapAStarPlanner256 _planner;
            private readonly ActionLibraryCompiled256 _library;
            private readonly GoapGoalTable256 _goals;

            public PlanJob(GoapAStarPlanner256 planner, ActionLibraryCompiled256 library, GoapGoalTable256 goals)
            {
                _planner = planner;
                _library = library;
                _goals = goals;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref AIAgent agent, ref AIWorldState256 worldState, ref AIGoalSelection selection, ref AIPlanningState planning, ref AIPlan32 plan)
            {
                if (selection.ActivePlanningStrategyId != AIPlanningStrategyIds.Goap) return;

                bool dirty = worldState.Version != planning.LastWorldStateVersion
                          || selection.ActiveGoalPresetId != planning.LastGoalPresetId
                          || plan.IsDone;

                if (!dirty) return;

                if (!_goals.TryGetGoal(selection.ActiveGoalPresetId, out var goal, out int heuristicWeight))
                {
                    plan.Clear();
                    planning.LastWorldStateVersion = worldState.Version;
                    planning.LastGoalPresetId = selection.ActiveGoalPresetId;
                    return;
                }

                Span<int> tmp = stackalloc int[AIPlan32.MaxActions];
                bool ok = _planner.TryPlan(in worldState.Bits, in goal, _library, tmp, out int len, heuristicWeight);
                plan.Clear();
                if (ok)
                {
                    for (int i = 0; i < len; i++) plan.TryAdd(tmp[i]);
                }

                planning.LastWorldStateVersion = worldState.Version;
                planning.LastGoalPresetId = selection.ActiveGoalPresetId;
            }
        }
    }
}

