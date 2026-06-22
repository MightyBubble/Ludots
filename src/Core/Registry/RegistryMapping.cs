using System;
using System.Collections.Generic;

namespace Ludots.Core.Registry
{
    public readonly record struct RegistryMapping(string Name, int Id);

    public static class RegistryMappingSnapshot
    {
        public static RegistryMapping[] FromNameToId(IReadOnlyDictionary<string, int> nameToId)
        {
            if (nameToId == null) throw new ArgumentNullException(nameof(nameToId));

            var mappings = new RegistryMapping[nameToId.Count];
            int index = 0;
            foreach (KeyValuePair<string, int> pair in nameToId)
            {
                mappings[index++] = new RegistryMapping(pair.Key, pair.Value);
            }

            SortInPlace(mappings);
            return mappings;
        }

        public static RegistryMapping[] Merge(params IReadOnlyList<RegistryMapping>[] sources)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));

            int count = 0;
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] == null) throw new ArgumentException("Registry mapping source must not be null.", nameof(sources));
                count += sources[i].Count;
            }

            var mappings = new RegistryMapping[count];
            int index = 0;
            for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
            {
                IReadOnlyList<RegistryMapping> source = sources[sourceIndex];
                for (int i = 0; i < source.Count; i++)
                {
                    mappings[index++] = source[i];
                }
            }

            SortInPlace(mappings);
            return mappings;
        }

        public static void SortInPlace(RegistryMapping[] mappings)
        {
            if (mappings == null) throw new ArgumentNullException(nameof(mappings));

            Array.Sort(mappings, static (left, right) =>
            {
                int nameComparison = string.CompareOrdinal(left.Name, right.Name);
                return nameComparison != 0
                    ? nameComparison
                    : left.Id.CompareTo(right.Id);
            });
        }
    }
}
