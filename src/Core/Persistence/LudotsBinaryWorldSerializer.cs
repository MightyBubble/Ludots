using Arch.Core;
using Arch.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ludots.Core.Persistence
{
    public sealed class LudotsBinaryWorldSerializer
    {
        public byte[] Serialize(World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            EnsureWorldComponentFormatters(world);
            ArchBinarySerializer serializer = LudotsCorePersistenceFormatters.CreateBinarySerializer();
            using World filteredWorld = CreateFilteredWorld(world);
            EnsureWorldComponentFormatters(filteredWorld);
            return serializer.Serialize(filteredWorld);
        }

        public World Deserialize(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            ArchBinarySerializer serializer = LudotsCorePersistenceFormatters.CreateBinarySerializer();
            World world = serializer.Deserialize(bytes);
            SaveEntityWorldIdNormalizer.Normalize(world);
            SaveEntityReferenceValidator.Validate(world, SaveEntityInclusionPolicy.Default);
            return world;
        }

        public static void EnsureWorldComponentFormatters(World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            IReadOnlySet<Type> formatterTypes = LudotsCorePersistenceFormatters.GetFormatterComponentTypes();
            SaveEntityInclusionPolicy policy = SaveEntityInclusionPolicy.Default;
            var missing = new HashSet<Type>();
            var query = QueryDescription.Null;
            world.Query(in query, entity =>
            {
                if (!policy.ShouldInclude(world, entity))
                {
                    return;
                }

                Signature signature = world.GetSignature(entity);
                foreach (ComponentType componentType in signature.Components)
                {
                    Type type = componentType.Type;
                    if (!formatterTypes.Contains(type))
                    {
                        missing.Add(type);
                    }
                }
            });

            if (missing.Count > 0)
            {
                string missingList = string.Join(
                    ", ",
                    missing
                        .Select(type => type.FullName ?? type.Name)
                        .OrderBy(name => name, StringComparer.Ordinal));
                throw new SaveContextException(
                    $"Save world contains component types without Ludots persistence formatters: {missingList}.");
            }
        }

        private World CreateFilteredWorld(World source)
        {
            ArchBinarySerializer serializer = LudotsCorePersistenceFormatters.CreateBinarySerializer();
            World filtered = serializer.Deserialize(serializer.Serialize(source));
            SaveEntityWorldIdNormalizer.Normalize(filtered);

            var excluded = new List<Entity>();
            var policy = SaveEntityInclusionPolicy.Default;
            filtered.Query(in QueryDescription.Null, entity =>
            {
                if (!policy.ShouldInclude(filtered, entity))
                {
                    excluded.Add(entity);
                }
            });

            for (int i = 0; i < excluded.Count; i++)
            {
                if (filtered.IsAlive(excluded[i]))
                {
                    filtered.Destroy(excluded[i]);
                }
            }

            SaveEntityReferenceValidator.Validate(filtered, policy);
            return filtered;
        }
    }
}
