using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS;

public static class TagStateInstaller
{
    public const string DeadEntityError = "GAS.TAG_STATE.ERR.DeadEntity";

    public static void EnsureInstalled(World world, Entity entity)
    {
        if (world == null)
        {
            throw new ArgumentNullException(nameof(world));
        }

        if (!world.IsAlive(entity))
        {
            throw new InvalidOperationException(DeadEntityError);
        }

        if (!world.Has<GameplayTagContainer>(entity))
        {
            world.Add(entity, new GameplayTagContainer());
        }

        if (!world.Has<TagCountContainer>(entity))
        {
            world.Add(entity, new TagCountContainer());
        }

        if (!world.Has<DirtyFlags>(entity))
        {
            world.Add(entity, new DirtyFlags());
        }
    }
}
