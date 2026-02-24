using System;
using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.Spatial
{
    public static class SpatialQueryPostProcessor
    {
        private static readonly IComparer<Entity> StableComparerInstance = new StableEntityComparer();

        public static int SortStableDedup(Span<Entity> span)
        {
            if (span.Length <= 1) return span.Length;
            span.Sort(StableComparerInstance);
            return DedupSorted(span);
        }

        private static int DedupSorted(Span<Entity> sorted)
        {
            int write = 1;
            for (int read = 1; read < sorted.Length; read++)
            {
                if (!sorted[read].Equals(sorted[write - 1]))
                {
                    sorted[write++] = sorted[read];
                }
            }
            return write;
        }

        private sealed class StableEntityComparer : IComparer<Entity>
        {
            public int Compare(Entity x, Entity y)
            {
                int c = x.WorldId.CompareTo(y.WorldId);
                if (c != 0) return c;
                c = x.Id.CompareTo(y.Id);
                if (c != 0) return c;
                return x.Version.CompareTo(y.Version);
            }
        }
    }
}
