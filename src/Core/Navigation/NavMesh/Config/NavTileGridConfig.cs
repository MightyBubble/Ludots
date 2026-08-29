using System;
using Ludots.Core.Spatial;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    /// <summary>
    /// Explicit nav tile grid for a map: the addressing frame nav tiles are baked and
    /// enumerated against. Authored alongside the bake; runtime tile loading and query
    /// addressing read this declaration only — never derived from boards or terrain.
    /// </summary>
    public sealed class NavTileGridConfig
    {
        public int WidthChunks { get; set; }
        public int HeightChunks { get; set; }
        public int ChunkSizeCells { get; set; } = SpatialScaleDefaults.TerrainChunkCells;
        public int CellSizeCm { get; set; } = SpatialScaleDefaults.CellCm;
        public int OriginXcm { get; set; }
        public int OriginZcm { get; set; }

        public int ChunkWidthCm => checked(CellSizeCm * ChunkSizeCells);

        public int ChunkHeightCm => checked(CellSizeCm * ChunkSizeCells);

        public NavTileGridConfig Clone() => new()
        {
            WidthChunks = WidthChunks,
            HeightChunks = HeightChunks,
            ChunkSizeCells = ChunkSizeCells,
            CellSizeCm = CellSizeCm,
            OriginXcm = OriginXcm,
            OriginZcm = OriginZcm
        };
    }
}
