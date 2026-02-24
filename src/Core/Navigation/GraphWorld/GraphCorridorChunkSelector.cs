using System;
using System.Collections.Generic;

namespace Ludots.Core.Navigation.GraphWorld
{
    public static class GraphCorridorChunkSelector
    {
        public static long[] Expand(IReadOnlyList<long> coarseChunkPath, int radius)
        {
            if (coarseChunkPath == null) throw new ArgumentNullException(nameof(coarseChunkPath));
            if (coarseChunkPath.Count == 0) return Array.Empty<long>();
            if (radius < 0) radius = 0;

            var set = new HashSet<long>(coarseChunkPath.Count * (radius * 2 + 1) * (radius * 2 + 1));
            for (int i = 0; i < coarseChunkPath.Count; i++)
            {
                long key = coarseChunkPath[i];
                (int cx, int cy) = GraphChunkKey.Unpack(key);
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        set.Add(GraphChunkKey.Pack(cx + dx, cy + dy));
                    }
                }
            }

            var arr = new long[set.Count];
            int w = 0;
            foreach (var k in set) arr[w++] = k;
            return arr;
        }
    }
}

