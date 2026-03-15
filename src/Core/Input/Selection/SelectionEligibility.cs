using Arch.Core;

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
    }
}
