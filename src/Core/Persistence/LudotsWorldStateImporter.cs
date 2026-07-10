using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.Core.Extensions.Dangerous;
using Arch.LowLevel.Jagged;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Modding;

namespace Ludots.Core.Persistence
{
    public static class LudotsWorldStateImporter
    {
        public static void ImportInto(World source, World target)
        {
            ImportInto(source, target, modLoader: null);
        }

        public static void ImportInto(World source, World target, ModLoader? modLoader)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            LudotsBinaryWorldSerializer serializer = modLoader == null
                ? new LudotsBinaryWorldSerializer()
                : LudotsPersistenceSerializerFactory.Create(modLoader);
            using World normalizedSource = serializer.CloneIncludedWorld(source);
            ImportOwnedSnapshotInto(normalizedSource, target);
        }

        internal static void ImportOwnedSnapshotInto(World source, World target)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (ReferenceEquals(source, target))
            {
                throw new ArgumentException("Save world import requires distinct source and target worlds.", nameof(target));
            }

            PrepareIncludedSource(source);
            target.Clear();
            SaveEntityWorldIdNormalizer.Normalize(source);
            SaveEntityReferenceValidator.Validate(source, SaveEntityInclusionPolicy.Default);

            var entityData = source.GetEntityDataArray();
            target.SetEntityDataArray(entityData);
            target.SetRecycledEntityIds(source.GetRecycledEntityIds());
            target.EnsureCapacity(entityData.Capacity);
            var archetypes = new List<Archetype>();
            foreach (Archetype archetype in source)
            {
                archetypes.Add(archetype);
            }

            // Arch's SetArchetypes appends; target.Clear above establishes replacement semantics.
            target.SetArchetypes(archetypes);
            DetachTransferredStorage(source);
            NormalizeChunkEntityWorldIds(target);
            SaveEntityWorldIdNormalizer.Normalize(target);
            SaveEntityReferenceValidator.Validate(target, SaveEntityInclusionPolicy.Default);
        }

        private static void PrepareIncludedSource(World source)
        {
            var excluded = new List<Entity>();
            SaveEntityInclusionPolicy policy = SaveEntityInclusionPolicy.Default;
            source.Query(in QueryDescription.Null, entity =>
            {
                if (!policy.ShouldInclude(source, entity))
                {
                    excluded.Add(entity);
                }
            });

            for (int i = 0; i < excluded.Count; i++)
            {
                if (source.IsAlive(excluded[i]))
                {
                    source.Destroy(excluded[i]);
                }
            }
        }

        private static void DetachTransferredStorage(World source)
        {
            source.SetEntityDataArray(new JaggedArray<EntityData>(
                source.BaseChunkSize / System.Runtime.CompilerServices.Unsafe.SizeOf<EntityData>(),
                new EntityData(null!, new Slot(-1, -1), 0),
                0));
            source.Clear();
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
