using Arch.Core;

namespace Ludots.Core.MassNavigation.Runtime;

public static class MassNavigationAgentBinding
{
    public static void MarkDirty(World world, Entity entity)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!world.IsAlive(entity) ||
            !world.Has<MassNavigationAgent>(entity) ||
            !world.Has<MassNavigationAgentIndex>(entity))
        {
            throw new InvalidOperationException(
                $"MassNavigation binding dirty notification requires a live, bound MassNavigationAgent entity, got {entity.Id}.");
        }

        if (!world.Has<MassNavigationAgentBindingDirty>(entity))
        {
            world.Add(entity, new MassNavigationAgentBindingDirty());
        }
    }
}
