using System;
using Arch.Core;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial
{
    /// <summary>
    /// Abstraction for a spatial partition data structure that tracks entities by cell coordinates.
    /// Implementations include grid-based and chunked-grid-based partitions.
    /// </summary>
    public interface ISpatialPartitionWorld
    {
        void Add(Entity entity, int cellX, int cellY);
        void Remove(Entity entity, int cellX, int cellY);
        int Query(in IntRect cellRect, Span<Entity> buffer, out int dropped);
        void Clear();
    }
}
