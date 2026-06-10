using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;

namespace Ludots.Core.Input.Selection
{
    /// <summary>
    /// Formal selection candidate checks shared by click/box/tab input.
    /// </summary>
    public static class SelectionEligibility
    {
        public static bool IsSelectableNow(World world, Entity entity)
        {
            if (!world.IsAlive(entity) || !world.Has<SelectionSelectableTag>(entity))
            {
                return false;
            }

            return !world.Has<SelectionSelectableState>(entity) ||
                   world.Get<SelectionSelectableState>(entity).Enabled;
        }

        public static bool CanAcquire(World world, Entity selector, Entity candidate, RelationshipFilter relationFilter)
        {
            if (!world.IsAlive(selector) || !IsSelectableNow(world, candidate))
            {
                return false;
            }

            if (relationFilter == RelationshipFilter.All)
            {
                return true;
            }

            return world.TryGet(selector, out Team selectorTeam) &&
                   world.TryGet(candidate, out Team candidateTeam) &&
                   RelationshipFilterUtil.Passes(relationFilter, selectorTeam.Id, candidateTeam.Id);
        }
    }
}
