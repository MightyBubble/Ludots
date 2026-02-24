using System;
using Arch.Core;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial
{
    public sealed class ChunkedGridSpatialPartitionBackend : SpatialPartitionBackendBase
    {
        public ChunkedGridSpatialPartitionBackend(ChunkedGridSpatialPartitionWorld world, WorldSizeSpec spec)
            : base(world ?? throw new ArgumentNullException(nameof(world)),
                   spec.GridCellSizeCm)
        {
        }
    }
}
