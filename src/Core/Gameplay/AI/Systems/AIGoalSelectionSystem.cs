using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.AI.Utility;

namespace Ludots.Core.Gameplay.AI.Systems
{
    public sealed class AIGoalSelectionSystem : BaseSystem<World, float>
    {
        private readonly UtilityGoalSelectorCompiled256 _selector;

        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<AIAgent, AIWorldState256, AIGoalSelection>();

        public AIGoalSelectionSystem(World world, UtilityGoalSelectorCompiled256 selector)
            : base(world)
        {
            _selector = selector;
        }

        public override void Update(in float dt)
        {
            var job = new SelectJob(_selector);
            World.InlineQuery<SelectJob, AIAgent, AIWorldState256, AIGoalSelection>(in _query, ref job);
        }

        private struct SelectJob : IForEach<AIAgent, AIWorldState256, AIGoalSelection>
        {
            private readonly UtilityGoalSelectorCompiled256 _selector;

            public SelectJob(UtilityGoalSelectorCompiled256 selector)
            {
                _selector = selector;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref AIAgent agent, ref AIWorldState256 worldState, ref AIGoalSelection selection)
            {
                _selector.Evaluate(in worldState.Bits, selection.LastGoalPresetId, out int goalId, out int stratId, out float score);
                selection.LastGoalPresetId = selection.ActiveGoalPresetId;
                selection.ActiveGoalPresetId = goalId;
                selection.ActivePlanningStrategyId = stratId;
                selection.ActiveScore = score;
            }
        }
    }
}

