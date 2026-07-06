using System;
using Ludots.Core.Spatial;

namespace Ludots.Core.Map.Authoring
{
    public sealed class BoardAllocationPreview
    {
        public BoardAllocationPreview(
            bool isValid,
            bool withinEditorBudget,
            bool exceedsDefaultWorldFootprint,
            bool snappedToMacroTile,
            float requestedWidthMeters,
            float requestedHeightMeters,
            int cellSizeCm,
            float macroTileMeters,
            float terrainChunkMeters,
            int requestedWidthCells,
            int requestedHeightCells,
            int widthMacroTiles,
            int heightMacroTiles,
            int allocatedWidthCells,
            int allocatedHeightCells,
            int widthTerrainChunks,
            int heightTerrainChunks,
            int totalTerrainChunks,
            long fullTerrainBytes,
            float allocatedWidthMeters,
            float allocatedHeightMeters)
        {
            IsValid = isValid;
            WithinEditorBudget = withinEditorBudget;
            ExceedsDefaultWorldFootprint = exceedsDefaultWorldFootprint;
            SnappedToMacroTile = snappedToMacroTile;
            RequestedWidthMeters = requestedWidthMeters;
            RequestedHeightMeters = requestedHeightMeters;
            CellSizeCm = cellSizeCm;
            MacroTileMeters = macroTileMeters;
            TerrainChunkMeters = terrainChunkMeters;
            RequestedWidthCells = requestedWidthCells;
            RequestedHeightCells = requestedHeightCells;
            WidthMacroTiles = widthMacroTiles;
            HeightMacroTiles = heightMacroTiles;
            AllocatedWidthCells = allocatedWidthCells;
            AllocatedHeightCells = allocatedHeightCells;
            WidthTerrainChunks = widthTerrainChunks;
            HeightTerrainChunks = heightTerrainChunks;
            TotalTerrainChunks = totalTerrainChunks;
            FullTerrainBytes = fullTerrainBytes;
            AllocatedWidthMeters = allocatedWidthMeters;
            AllocatedHeightMeters = allocatedHeightMeters;
        }

        public bool IsValid { get; }
        public bool WithinEditorBudget { get; }
        public bool ExceedsDefaultWorldFootprint { get; }
        public bool SnappedToMacroTile { get; }
        public float RequestedWidthMeters { get; }
        public float RequestedHeightMeters { get; }
        public int CellSizeCm { get; }
        public float MacroTileMeters { get; }
        public float TerrainChunkMeters { get; }
        public int RequestedWidthCells { get; }
        public int RequestedHeightCells { get; }
        public int WidthMacroTiles { get; }
        public int HeightMacroTiles { get; }
        public int AllocatedWidthCells { get; }
        public int AllocatedHeightCells { get; }
        public int WidthTerrainChunks { get; }
        public int HeightTerrainChunks { get; }
        public int TotalTerrainChunks { get; }
        public long FullTerrainBytes { get; }
        public float AllocatedWidthMeters { get; }
        public float AllocatedHeightMeters { get; }
    }

    public static class BoardAllocationPreviewCalculator
    {
        public const int TerrainCellBytes = sizeof(int);
        public const int TerrainChunkBytes =
            SpatialScaleDefaults.TerrainChunkCells *
            SpatialScaleDefaults.TerrainChunkCells *
            TerrainCellBytes;
        public const int EagerFullTerrainFileMacroTilesPerAxis = 16;

        public static BoardAllocationPreview FromDesiredMeters(
            float widthMeters,
            float heightMeters,
            int cellSizeCm)
        {
            int safeCellSizeCm = Math.Max(1, cellSizeCm);
            float requestedWidthMeters = float.IsFinite(widthMeters) ? Math.Max(0f, widthMeters) : 0f;
            float requestedHeightMeters = float.IsFinite(heightMeters) ? Math.Max(0f, heightMeters) : 0f;
            int requestedWidthCells = requestedWidthMeters > 0f
                ? Math.Max(1, (int)MathF.Ceiling(requestedWidthMeters * 100f / safeCellSizeCm))
                : 0;
            int requestedHeightCells = requestedHeightMeters > 0f
                ? Math.Max(1, (int)MathF.Ceiling(requestedHeightMeters * 100f / safeCellSizeCm))
                : 0;
            int widthMacroTiles = requestedWidthCells > 0
                ? (int)MathF.Ceiling(requestedWidthCells / (float)SpatialScaleDefaults.MacroTileCells)
                : 0;
            int heightMacroTiles = requestedHeightCells > 0
                ? (int)MathF.Ceiling(requestedHeightCells / (float)SpatialScaleDefaults.MacroTileCells)
                : 0;

            return FromMacroTiles(
                widthMacroTiles,
                heightMacroTiles,
                safeCellSizeCm,
                requestedWidthMeters,
                requestedHeightMeters,
                requestedWidthCells,
                requestedHeightCells);
        }

        public static BoardAllocationPreview FromMacroTiles(
            int widthMacroTiles,
            int heightMacroTiles,
            int cellSizeCm)
        {
            int safeCellSizeCm = Math.Max(1, cellSizeCm);
            int width = Math.Max(0, widthMacroTiles);
            int height = Math.Max(0, heightMacroTiles);
            int requestedWidthCells = checked(width * SpatialScaleDefaults.MacroTileCells);
            int requestedHeightCells = checked(height * SpatialScaleDefaults.MacroTileCells);
            float requestedWidthMeters = requestedWidthCells * safeCellSizeCm / 100f;
            float requestedHeightMeters = requestedHeightCells * safeCellSizeCm / 100f;
            return FromMacroTiles(
                width,
                height,
                safeCellSizeCm,
                requestedWidthMeters,
                requestedHeightMeters,
                requestedWidthCells,
                requestedHeightCells);
        }

        private static BoardAllocationPreview FromMacroTiles(
            int widthMacroTiles,
            int heightMacroTiles,
            int cellSizeCm,
            float requestedWidthMeters,
            float requestedHeightMeters,
            int requestedWidthCells,
            int requestedHeightCells)
        {
            int allocatedWidthCells = checked(widthMacroTiles * SpatialScaleDefaults.MacroTileCells);
            int allocatedHeightCells = checked(heightMacroTiles * SpatialScaleDefaults.MacroTileCells);
            int chunksPerMacroTile = SpatialScaleDefaults.MacroTileCells / SpatialScaleDefaults.TerrainChunkCells;
            int widthTerrainChunks = checked(widthMacroTiles * chunksPerMacroTile);
            int heightTerrainChunks = checked(heightMacroTiles * chunksPerMacroTile);
            int totalTerrainChunks = checked(widthTerrainChunks * heightTerrainChunks);
            long fullTerrainBytes = checked((long)totalTerrainChunks * TerrainChunkBytes);
            float allocatedWidthMeters = allocatedWidthCells * cellSizeCm / 100f;
            float allocatedHeightMeters = allocatedHeightCells * cellSizeCm / 100f;
            float macroTileMeters = SpatialScaleDefaults.MacroTileCells * cellSizeCm / 100f;
            float terrainChunkMeters = SpatialScaleDefaults.TerrainChunkCells * cellSizeCm / 100f;
            bool snappedToMacroTile =
                MathF.Abs(allocatedWidthMeters - requestedWidthMeters) > 0.0001f ||
                MathF.Abs(allocatedHeightMeters - requestedHeightMeters) > 0.0001f;

            return new BoardAllocationPreview(
                isValid: requestedWidthMeters > 0f && requestedHeightMeters > 0f,
                withinEditorBudget: widthMacroTiles > 0 && heightMacroTiles > 0,
                exceedsDefaultWorldFootprint:
                    widthMacroTiles > SpatialScaleDefaults.DefaultWorldWidthMacroTiles ||
                    heightMacroTiles > SpatialScaleDefaults.DefaultWorldHeightMacroTiles,
                snappedToMacroTile,
                requestedWidthMeters,
                requestedHeightMeters,
                cellSizeCm,
                macroTileMeters,
                terrainChunkMeters,
                requestedWidthCells,
                requestedHeightCells,
                widthMacroTiles,
                heightMacroTiles,
                allocatedWidthCells,
                allocatedHeightCells,
                widthTerrainChunks,
                heightTerrainChunks,
                totalTerrainChunks,
                fullTerrainBytes,
                allocatedWidthMeters,
                allocatedHeightMeters);
        }
    }
}
