using System;
using Ludots.Core.Map.Hex;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Spatial;

namespace Ludots.Core.Navigation.Terrain
{
    public enum LogicTerrainTopology : byte
    {
        Grid = 0,
        Hex = 1
    }

    [Flags]
    public enum LogicTerrainSurfaceFlags : byte
    {
        None = 0,
        Water = 1 << 0,
        Ramp = 1 << 1,
        Blocked = 1 << 2
    }

    public readonly struct LogicTerrainCell
    {
        public LogicTerrainCell(
            byte heightLevel,
            byte waterHeightLevel,
            LogicTerrainSurfaceFlags surfaceFlags,
            byte areaId = 0,
            float cost = 1f)
        {
            HeightLevel = ClampHeight(heightLevel);
            WaterHeightLevel = ClampHeight(waterHeightLevel);
            SurfaceFlags = surfaceFlags;
            AreaId = areaId;
            Cost = cost > 0f && !float.IsNaN(cost) ? cost : throw new ArgumentOutOfRangeException(nameof(cost));
        }

        public byte HeightLevel { get; }

        public byte WaterHeightLevel { get; }

        public LogicTerrainSurfaceFlags SurfaceFlags { get; }

        public byte AreaId { get; }

        public float Cost { get; }

        public bool IsRamp => (SurfaceFlags & LogicTerrainSurfaceFlags.Ramp) != 0;

        public bool IsBlocked => (SurfaceFlags & LogicTerrainSurfaceFlags.Blocked) != 0;

        public bool HasWater => (SurfaceFlags & LogicTerrainSurfaceFlags.Water) != 0 || WaterHeightLevel > 0;

        private static byte ClampHeight(byte value)
            => value > SpatialScaleDefaults.LogicTerrainMaxHeightLevel
                ? (byte)SpatialScaleDefaults.LogicTerrainMaxHeightLevel
                : value;
    }

    public abstract class LogicTerrainField
    {
        protected LogicTerrainField(int widthCells, int heightCells, int chunkSizeCells)
        {
            if (widthCells <= 0) throw new ArgumentOutOfRangeException(nameof(widthCells));
            if (heightCells <= 0) throw new ArgumentOutOfRangeException(nameof(heightCells));
            if (chunkSizeCells <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSizeCells));

            WidthCells = widthCells;
            HeightCells = heightCells;
            ChunkSizeCells = chunkSizeCells;
        }

        public abstract LogicTerrainTopology Topology { get; }

        public int WidthCells { get; }

        public int HeightCells { get; }

        public int ChunkSizeCells { get; }

        public int WidthChunks => (WidthCells + ChunkSizeCells - 1) / ChunkSizeCells;

        public int HeightChunks => (HeightCells + ChunkSizeCells - 1) / ChunkSizeCells;

        public abstract LogicTerrainCell GetCell(int col, int row);

        public virtual bool TryGetCliffStraightenEdge(int col, int row, int edgeIndex, out bool value)
        {
            value = false;
            return false;
        }

        public abstract void GetWorldPositionMeters(int col, int row, out float xMeters, out float zMeters);

        public abstract int HorizontalStepCm { get; }

        public abstract int VerticalStepCm { get; }

        public bool IsInBounds(int col, int row)
            => (uint)col < (uint)WidthCells && (uint)row < (uint)HeightCells;

        public int TileWidthCells(int chunkX)
            => Math.Max(0, Math.Min(ChunkSizeCells, WidthCells - chunkX * ChunkSizeCells));

        public int TileHeightCells(int chunkY)
            => Math.Max(0, Math.Min(ChunkSizeCells, HeightCells - chunkY * ChunkSizeCells));
    }

    public sealed class VertexMapLogicTerrainField : LogicTerrainField
    {
        public VertexMapLogicTerrainField(VertexMap vertexMap)
            : base(
                (vertexMap ?? throw new ArgumentNullException(nameof(vertexMap))).WidthInChunks * VertexChunk.ChunkSize,
                vertexMap.HeightInChunks * VertexChunk.ChunkSize,
                VertexChunk.ChunkSize)
        {
            VertexMap = vertexMap;
        }

        public VertexMap VertexMap { get; }

        public override LogicTerrainTopology Topology => LogicTerrainTopology.Hex;

        public override int HorizontalStepCm => HexCoordinates.EdgeLengthCm;

        public override int VerticalStepCm => HexCoordinates.EdgeLengthCm;

        public override LogicTerrainCell GetCell(int col, int row)
        {
            if (!IsInBounds(col, row))
            {
                return default;
            }

            var chunk = VertexMap.GetChunk(col, row, false);
            if (chunk == null)
            {
                return default;
            }

            int lx = col & VertexChunk.ChunkSizeMask;
            int ly = row & VertexChunk.ChunkSizeMask;
            byte height = chunk.GetHeight(lx, ly);
            byte water = chunk.GetWaterHeight(lx, ly);
            var flags = LogicTerrainSurfaceFlags.None;
            if (chunk.GetRamp(lx, ly)) flags |= LogicTerrainSurfaceFlags.Ramp;
            if (chunk.GetFlag(lx, ly)) flags |= LogicTerrainSurfaceFlags.Blocked;
            if (water > height) flags |= LogicTerrainSurfaceFlags.Water;
            return new LogicTerrainCell(height, water, flags, chunk.GetExtraByte(lx, ly, 0));
        }

        public override bool TryGetCliffStraightenEdge(int col, int row, int edgeIndex, out bool value)
        {
            value = false;
            if (!IsInBounds(col, row))
            {
                return false;
            }

            var chunk = VertexMap.GetChunk(col, row, false);
            if (chunk == null)
            {
                return false;
            }

            value = chunk.GetCliffStraightenEdge(col & VertexChunk.ChunkSizeMask, row & VertexChunk.ChunkSizeMask, edgeIndex);
            return true;
        }

        public override void GetWorldPositionMeters(int col, int row, out float xMeters, out float zMeters)
        {
            xMeters = HexCoordinates.HexWidth * (col + 0.5f * (row & 1));
            zMeters = HexCoordinates.RowSpacing * row;
        }
    }

    public sealed class FlatGridLogicTerrainField : LogicTerrainField
    {
        private readonly LogicTerrainCell _cell;

        public FlatGridLogicTerrainField(
            int widthCells,
            int heightCells,
            int cellSizeCm = SpatialScaleDefaults.CellCm,
            int chunkSizeCells = SpatialScaleDefaults.TerrainChunkCells,
            LogicTerrainCell cell = default)
            : base(widthCells, heightCells, chunkSizeCells)
        {
            if (cellSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeCm));
            CellSizeCm = cellSizeCm;
            _cell = cell.Cost > 0f ? cell : new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.None);
        }

        public override LogicTerrainTopology Topology => LogicTerrainTopology.Grid;

        public int CellSizeCm { get; }

        public override int HorizontalStepCm => CellSizeCm;

        public override int VerticalStepCm => CellSizeCm;

        public override LogicTerrainCell GetCell(int col, int row)
            => IsInBounds(col, row) ? _cell : default;

        public override void GetWorldPositionMeters(int col, int row, out float xMeters, out float zMeters)
        {
            xMeters = col * SpatialScaleDefaults.CentimetersToMeters(CellSizeCm);
            zMeters = row * SpatialScaleDefaults.CentimetersToMeters(CellSizeCm);
        }
    }

    public sealed class MutableGridLogicTerrainField : LogicTerrainField
    {
        private readonly LogicTerrainCell[] _cells;

        public MutableGridLogicTerrainField(
            int widthCells,
            int heightCells,
            int cellSizeCm = SpatialScaleDefaults.CellCm,
            int chunkSizeCells = SpatialScaleDefaults.TerrainChunkCells)
            : base(widthCells, heightCells, chunkSizeCells)
        {
            if (cellSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeCm));
            CellSizeCm = cellSizeCm;
            _cells = new LogicTerrainCell[checked(widthCells * heightCells)];
            Fill(new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.None));
        }

        public override LogicTerrainTopology Topology => LogicTerrainTopology.Grid;

        public int CellSizeCm { get; }

        public override int HorizontalStepCm => CellSizeCm;

        public override int VerticalStepCm => CellSizeCm;

        public void SetCell(int col, int row, LogicTerrainCell cell)
        {
            if (!IsInBounds(col, row)) throw new ArgumentOutOfRangeException();
            _cells[row * WidthCells + col] = cell;
        }

        public void Fill(LogicTerrainCell cell)
        {
            Array.Fill(_cells, cell);
        }

        public override LogicTerrainCell GetCell(int col, int row)
            => IsInBounds(col, row) ? _cells[row * WidthCells + col] : default;

        public override void GetWorldPositionMeters(int col, int row, out float xMeters, out float zMeters)
        {
            xMeters = col * SpatialScaleDefaults.CentimetersToMeters(CellSizeCm);
            zMeters = row * SpatialScaleDefaults.CentimetersToMeters(CellSizeCm);
        }
    }

    public readonly struct LogicTerrainProjectionOptions
    {
        public LogicTerrainProjectionOptions(int heightStepCm, int layerIndex = -1)
        {
            if (heightStepCm <= 0) throw new ArgumentOutOfRangeException(nameof(heightStepCm));
            HeightStepCm = heightStepCm;
            LayerIndex = layerIndex;
        }

        public int HeightStepCm { get; }

        public int LayerIndex { get; }

        public static LogicTerrainProjectionOptions Default { get; } =
            new LogicTerrainProjectionOptions(SpatialScaleDefaults.CellCm);
    }

    public static class VisualHeightmapLogicTerrainProjection
    {
        public static MutableGridLogicTerrainField ProjectToGrid(
            IVisualHeightmap visualHeightmap,
            int widthCells,
            int heightCells,
            int cellSizeCm,
            LogicTerrainProjectionOptions options)
        {
            if (visualHeightmap == null) throw new ArgumentNullException(nameof(visualHeightmap));

            var field = new MutableGridLogicTerrainField(widthCells, heightCells, cellSizeCm);
            for (int row = 0; row < heightCells; row++)
            {
                for (int col = 0; col < widthCells; col++)
                {
                    float xCm = col * cellSizeCm;
                    float yCm = row * cellSizeCm;
                    if (!visualHeightmap.TrySampleHeightCm(xCm, yCm, out float heightCm, options.LayerIndex))
                    {
                        throw new InvalidOperationException(
                            $"Visual heightmap projection failed at grid cell ({col},{row}).");
                    }

                    int level = (int)MathF.Round(heightCm / options.HeightStepCm);
                    level = Math.Clamp(level, 0, SpatialScaleDefaults.LogicTerrainMaxHeightLevel);
                    field.SetCell(col, row, new LogicTerrainCell((byte)level, 0, LogicTerrainSurfaceFlags.None));
                }
            }

            return field;
        }
    }
}
