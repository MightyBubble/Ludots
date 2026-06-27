using Ludots.Core.Map;

namespace Ludots.Core.Spatial
{
    /// <summary>
    /// Shared spatial scale names for board, terrain bake, mass-flow, and broadphase code.
    /// </summary>
    public static class SpatialScaleDefaults
    {
        public const int CellCm = 100;
        public const int DefaultHexEdgeLengthCm = 400;
        public const int MacroTileCells = MapTile.Size;
        public const int DefaultWorldWidthMacroTiles = 64;
        public const int DefaultWorldHeightMacroTiles = 64;
        public const int DefaultTerrainWidthChunks = 64;
        public const int DefaultTerrainHeightChunks = 64;
        public const int PartitionChunkCells = 64;
        public const int TerrainChunkCells = 64;
        public const int FlowCellCm = CellCm;
        public const int AvoidanceSeparationHashCellCm = CellCm;
        public const int AvoidanceHardResolveHashCellCm = 50;
        public const int PhysicsBroadphaseCellCm = CellCm;
        public const int LogicTerrainHeightLevels = 16;
        public const int LogicTerrainMaxHeightLevel = LogicTerrainHeightLevels - 1;
        public const int LogicDenseEquivalentBytesPerCell = 4;
        public const int VisualHeightmapR16BytesPerSample = 2;
        public const int BitsPerFlagWord = sizeof(ulong) * 8;
        public const int NavPortalInitialCapacity = TerrainChunkCells;
        public const int ContourRingInitialCapacity = TerrainChunkCells;

        public static float MetersToCentimeters(float meters) => meters * CellCm;

        public static float CentimetersToMeters(int centimeters) => centimeters / (float)CellCm;
    }
}
