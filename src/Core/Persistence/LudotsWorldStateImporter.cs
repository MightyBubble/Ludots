using System;
using Arch.Core;
using Arch.Core.Extensions.Dangerous;
using Ludots.Core.Gameplay.GAS;

namespace Ludots.Core.Persistence
{
    public static class LudotsWorldStateImporter
    {
        public static void ImportInto(World source, World target)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            target.Clear();
            byte[] bytes = new LudotsBinaryWorldSerializer().Serialize(source);
            using World normalizedSource = new LudotsBinaryWorldSerializer().Deserialize(bytes);

            target.SetEntityDataArray(normalizedSource.GetEntityDataArray());
            target.SetRecycledEntityIds(normalizedSource.GetRecycledEntityIds());
            target.EnsureCapacity(normalizedSource.GetEntityDataArray().Capacity);
            foreach (Archetype archetype in normalizedSource)
            {
                target.SetArchetypes(new System.Collections.Generic.List<Archetype> { archetype });
            }

            NormalizeChunkEntityWorldIds(target);
            SaveEntityWorldIdNormalizer.Normalize(target);
            SaveEntityReferenceValidator.Validate(target, SaveEntityInclusionPolicy.Default);
        }

        private static void NormalizeChunkEntityWorldIds(World world)
        {
            foreach (Archetype archetype in world)
            {
                foreach (Chunk chunk in archetype)
                {
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        ref Entity entity = ref chunk.Entity(i);
                        entity = EntityUtil.Reconstruct(entity.Id, world.Id, entity.Version);
                    }
                }
            }
        }
    }
}
