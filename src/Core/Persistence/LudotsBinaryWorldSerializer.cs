using Arch.Core;
using Arch.Persistence;
using MessagePack.Formatters;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ludots.Core.Persistence
{
    public sealed class LudotsBinaryWorldSerializer
    {
        private readonly IMessagePackFormatter[] _additionalFormatters;
        private readonly IMessagePackFormatter[]? _formatters;
        private readonly IReadOnlySet<Type>? _componentTypes;

        public LudotsBinaryWorldSerializer()
            : this(Array.Empty<IMessagePackFormatter>())
        {
        }

        internal LudotsBinaryWorldSerializer(params IMessagePackFormatter[] additionalFormatters)
        {
            if (additionalFormatters == null) throw new ArgumentNullException(nameof(additionalFormatters));
            if (additionalFormatters.Any(formatter => formatter == null))
            {
                throw new ArgumentException("Additional persistence formatters cannot contain null entries.", nameof(additionalFormatters));
            }

            _additionalFormatters = additionalFormatters.ToArray();
        }

        internal LudotsBinaryWorldSerializer(
            IMessagePackFormatter[] formatters,
            IReadOnlySet<Type> componentTypes)
        {
            if (formatters == null) throw new ArgumentNullException(nameof(formatters));
            if (componentTypes == null) throw new ArgumentNullException(nameof(componentTypes));
            if (formatters.Any(formatter => formatter == null))
            {
                throw new ArgumentException("Persistence formatters cannot contain null entries.", nameof(formatters));
            }

            _formatters = formatters.ToArray();
            _componentTypes = componentTypes;
            _additionalFormatters = Array.Empty<IMessagePackFormatter>();
        }

        public byte[] Serialize(World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            EnsureSerializableWorldComponents(world);
            ArchBinarySerializer serializer = CreateSerializer();
            using World filteredWorld = CloneIncludedWorld(world);
            EnsureSerializableWorldComponents(filteredWorld);
            return serializer.Serialize(filteredWorld);
        }

        public World Deserialize(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            ArchBinarySerializer serializer = CreateSerializer();
            World world = serializer.Deserialize(bytes);
            SaveEntityWorldIdNormalizer.Normalize(world);
            SaveEntityReferenceValidator.Validate(world, SaveEntityInclusionPolicy.Default);
            return world;
        }

        internal World CloneIncludedWorld(World source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            ArchBinarySerializer serializer = CreateSerializer();
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

        private ArchBinarySerializer CreateSerializer()
        {
            if (_formatters != null)
            {
                return new ArchBinarySerializer(_formatters);
            }

            if (_additionalFormatters.Length == 0)
            {
                return LudotsCorePersistenceFormatters.CreateBinarySerializer();
            }

            return new ArchBinarySerializer(
                LudotsCorePersistenceFormatters.CreateFormatters()
                    .Concat(_additionalFormatters)
                    .ToArray());
        }

        private void EnsureSerializableWorldComponents(World world)
        {
            EnsureWorldComponentFormatters(
                world,
                _componentTypes ?? LudotsCorePersistenceFormatters.GetFormatterComponentTypes());
        }

        public static void EnsureWorldComponentFormatters(World world)
        {
            EnsureWorldComponentFormatters(
                world,
                LudotsCorePersistenceFormatters.GetFormatterComponentTypes());
        }

        private static void EnsureWorldComponentFormatters(
            World world,
            IReadOnlySet<Type> formatterTypes)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (formatterTypes == null) throw new ArgumentNullException(nameof(formatterTypes));

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

    }
}
