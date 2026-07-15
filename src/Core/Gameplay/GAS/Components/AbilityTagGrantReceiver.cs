using Arch.Core;

namespace Ludots.Core.Gameplay.GAS.Components;

/// <summary>
/// Declares that an entity can receive direct ability timeline tag grants.
/// Authoring paths install the complete tag state before the entity becomes playable.
/// </summary>
public struct AbilityTagGrantReceiver
{
}

public static class AbilityTagGrantReceiverInstaller
{
    public static void EnsureInstalled(World world, Entity entity)
    {
        TagStateInstaller.EnsureInstalled(world, entity);
        if (!world.Has<TimedTagBuffer>(entity))
        {
            world.Add(entity, new TimedTagBuffer());
        }
    }
}
