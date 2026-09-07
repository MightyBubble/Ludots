using System;
using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.Spatial
{
    public static class SpatialQueryPostProcessor
    {
        private static readonly Comparison<Entity> StableComparison = CompareStable;

        public static int SortStableDedup(Span<Entity> span)
        {
            if (span.Length <= 1) return span.Length;
            span.Sort(StableComparison);
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

        private static int CompareStable(Entity x, Entity y)
        {
            int c = x.WorldId.CompareTo(y.WorldId);
            if (c != 0) return c;
            c = x.Id.CompareTo(y.Id);
            if (c != 0) return c;
            return x.Version.CompareTo(y.Version);
        }
    }
}
