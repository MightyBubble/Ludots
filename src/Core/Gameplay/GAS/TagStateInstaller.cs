using Arch.Core;
using Ludots.Core.Gameplay.GAS.Capacity;
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
            world.Add(entity, GameplayTagContainer.CreateAttached(world, entity));
        }
        else
        {
            ref var existing = ref world.Get<GameplayTagContainer>(entity);
            if (existing.RowId == GameplayTagContainer.InvalidRow)
            {
                existing = GameplayTagContainer.CreateAttached(world, entity);
            }
        }

        if (!world.Has<TagCountContainer>(entity))
        {
            var counts = new TagCountContainer();
            ref GameplayTagContainer tags = ref world.Get<GameplayTagContainer>(entity);
            int maxTagId = GasLoadTimeCapacitySession.Plan.MaxUsableTagId;
            for (int tagId = 1; tagId <= maxTagId; tagId++)
            {
                if (tags.HasTag(tagId) && !counts.AddCount(tagId))
                {
                    throw new InvalidOperationException(
                        $"{TagOps.TagCountOverflowError}: entity={entity.Id}, source=TagStateInstaller.");
                }
            }
            world.Add(entity, counts);
        }

        if (!world.Has<DirtyFlags>(entity))
        {
            world.Add(entity, new DirtyFlags());
        }
    }
}
