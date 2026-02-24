using System;
using Arch.Core;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial
{
    public sealed class GridSpatialPartitionBackend : SpatialPartitionBackendBase
    {
        public GridSpatialPartitionBackend(GridSpatialPartitionWorld world, ISpatialCoordinateConverter coords)
            : base(world ?? throw new ArgumentNullException(nameof(world)),
                   (coords ?? throw new ArgumentNullException(nameof(coords))).GridCellSizeCm)
        {
        }
    }
}
