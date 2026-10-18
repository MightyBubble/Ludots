using System;
using Arch.Core;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial
{
    public interface ISpatialPartitionWorld
    {
        void Add(Entity entity, int cellX, int cellY);
        void Remove(Entity entity, int cellX, int cellY);
        int Query(in IntRect cellRect, Span<Entity> buffer, out int dropped);
        void Clear();
    }
}
