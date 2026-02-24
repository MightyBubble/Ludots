using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.AI.Planning;

namespace Ludots.Core.Gameplay.AI.Systems
{
    public sealed class HtnPlanningSystem : BaseSystem<World, float>
    {
        private readonly HtnPlanner256 _planner;
        private readonly HtnDomainCompiled256 _domain;
        private readonly HtnRootTable _roots;

        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<AIAgent, AIWorldState256, AIGoalSelection, AIPlanningState, AIPlan32>();

        public HtnPlanningSystem(World world, HtnPlanner256 planner, HtnDomainCompiled256 domain, HtnRootTable roots)
            : base(world)
        {
            _planner = planner;
            _domain = domain;
            _roots = roots;
        }

        public override void Update(in float dt)
        {
            var job = new PlanJob(_planner, _domain, _roots);
            World.InlineQuery<PlanJob, AIAgent, AIWorldState256, AIGoalSelection, AIPlanningState, AIPlan32>(in _query, ref job);
        }

        private struct PlanJob : IForEach<AIAgent, AIWorldState256, AIGoalSelection, AIPlanningState, AIPlan32>
        {
            private readonly HtnPlanner256 _planner;
            private readonly HtnDomainCompiled256 _domain;
            private readonly HtnRootTable _roots;

            public PlanJob(HtnPlanner256 planner, HtnDomainCompiled256 domain, HtnRootTable roots)
            {
                _planner = planner;
                _domain = domain;
                _roots = roots;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref AIAgent agent, ref AIWorldState256 worldState, ref AIGoalSelection selection, ref AIPlanningState planning, ref AIPlan32 plan)
            {
                if (selection.ActivePlanningStrategyId != AIPlanningStrategyIds.Htn) return;

                bool dirty = worldState.Version != planning.LastWorldStateVersion
                          || selection.ActiveGoalPresetId != planning.LastGoalPresetId
                          || plan.IsDone;

                if (!dirty) return;

                if (!_roots.TryGetRootTask(selection.ActiveGoalPresetId, out int rootTaskId))
                {
                    plan.Clear();
                    planning.LastWorldStateVersion = worldState.Version;
                    planning.LastGoalPresetId = selection.ActiveGoalPresetId;
                    return;
                }

                Span<int> tmp = stackalloc int[AIPlan32.MaxActions];
                bool ok = _planner.TryPlan(in worldState.Bits, _domain, rootTaskId, tmp, out int len);
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

