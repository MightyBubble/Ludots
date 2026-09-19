using System;
using Ludots.Core.Spatial;

namespace Ludots.Core.Config
{
    /// <summary>
    /// World-level spatial budget (#1567 slice 4): partition granularity and streaming
    /// capacity belong to the world, not to boards. Null entries mean not authored;
    /// when authored, they are the single budget every board runs on.
    /// </summary>
    public class WorldTuningConfig
    {
        public int? PartitionChunkCells { get; set; }

        public int? LoadedChunkCapacity { get; set; }

        public bool IsAuthored => PartitionChunkCells.HasValue || LoadedChunkCapacity.HasValue;

        public WorldTuningConfig Clone() => new()
        {
            PartitionChunkCells = PartitionChunkCells,
            LoadedChunkCapacity = LoadedChunkCapacity
        };
    }
}
