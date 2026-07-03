using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Persistence
{
    public sealed class SaveEntityInclusionPolicy
    {
        public static readonly SaveEntityInclusionPolicy Default = new();

        private SaveEntityInclusionPolicy()
        {
        }

        public bool ShouldInclude(World world, Entity entity)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            return !world.Has<SaveExcludedTag>(entity) &&
                !world.Has<GameplayEvent>(entity) &&
                !world.Has<SimulationBudgetFuseEvent>(entity) &&
                !world.Has<PresentationDestroyPending>(entity);
        }
    }
}
